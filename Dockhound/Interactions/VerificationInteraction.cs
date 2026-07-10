using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Dockhound.Components;
using Dockhound.Enums;
using Dockhound.Logs;
using Dockhound.Modals;
using Dockhound.Models;
using Dockhound.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using static Dockhound.Services.VerificationHistoryService;

namespace Dockhound.Interactions
{
    public class VerificationInteraction : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DockhoundContext _dbContext;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IGuildSettingsService _guildSettingsService;
        private readonly IVerificationHistoryService _verificationHistory;
        private readonly ISteamService _steamService;
        private readonly IWarService _warService;

        private const int SteamHistoryTake = 5;

        private long seconds = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;

        public VerificationInteraction(DockhoundContext dbContext, HttpClient httpClient, IConfiguration config, IGuildSettingsService guildSettingsService, IVerificationHistoryService verificationHistoryService, ISteamService steamService, IWarService warService)
        {
            _dbContext = dbContext;
            _httpClient = httpClient;
            _configuration = config;
            _guildSettingsService = guildSettingsService;
            _verificationHistory = verificationHistoryService;
            _steamService = steamService;
            _warService = warService;
        }

        // APPROVE
        [ComponentInteraction("verify:approve")]
        public async Task ApproveVerification()
        {
            var comp = (SocketMessageComponent)Context.Interaction;
            var guild = Context.Guild;

            var cfg = await _guildSettingsService.GetAsync(Context.Guild.Id);

            var embed = comp.Message.Embeds.FirstOrDefault()?.ToEmbedBuilder();
            if (embed == null)
                return;

            var userIdField = embed.Fields.FirstOrDefault(f => f.Name == "User ID");
            if (userIdField == null || !ulong.TryParse(userIdField.Value?.ToString(), out var userId))
                return;

            var factionField = embed.Fields.FirstOrDefault(f => f.Name == "Faction");
            if (factionField == null)
                return;

            var user = guild?.GetUser(userId);
            if (user == null)
                return;

            var notificationChannel = cfg.Verify.NotificationChannelId is ulong id
                        ? guild.GetTextChannel(id)
                        : null;

            if (notificationChannel is null)
                return;

            await DeferAsync(ephemeral: true);

            // Assign roles, update message, log, notify user etc. — delegated to helper so it can be reused.
            var reviewMessage = comp.Message as IUserMessage;

            // Try to resolve steam64Id from the embed's "Steam Profile" field (if present).
            ulong? steam64Id = null;
            var steamProfileField = embed.Fields.FirstOrDefault(f => f.Name == "Steam Profile")?.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(steamProfileField))
            {
                try
                {
                    // Split into lines and pick the first non-empty line that doesn't look like a warning marker or placeholder.
                    var candidate = steamProfileField
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("⚠️") && s != "-" && s != "\u200b");

                    if (string.IsNullOrWhiteSpace(candidate))
                        candidate = steamProfileField.Trim();

                    // If the candidate contains additional text after the URL/ID (e.g. appended warnings),
                    // take the first whitespace-separated token to isolate the URL/ID.
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        candidate = candidate.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();

                        // Try resolving the isolated candidate
                        var steamResult = await _steamService.ResolveSteam64IdAsync(candidate);
                        if (steamResult.Status == SteamResolveStatus.Resolved)
                            steam64Id = steamResult.Steam64Id;
                    }
                }
                catch
                {
                    // ignore resolution errors here; history logging can continue without steam64
                }
            }

            var factionEnum = FactionParser.Parse(factionField.Value?.ToString());

            await CompleteApprovalAsync(reviewMessage, user, factionEnum, Context.User.Id, Context.User.Username, steam64Id);
            await FollowupAsync("Approved.", ephemeral: true);
        }

        // DENY
        [ComponentInteraction("verify:deny")]
        public async Task DenyVerification()
        {
            var comp = (SocketMessageComponent)Context.Interaction;
            var embed = comp.Message.Embeds.FirstOrDefault()?.ToEmbedBuilder();

            if (embed == null)
                return;

            var userIdField = embed.Fields.FirstOrDefault(f => f.Name == "User ID");
            if (userIdField == null || !ulong.TryParse(userIdField.Value?.ToString(), out var userId))
                return;

            // Pass both the target user and the review-message ID into the modal CustomId
            var modal = new ModalBuilder("Denial Reason", $"verify-deny-reason:{userId}:{comp.Message.Id}")
                .AddTextInput("Why are you denying this?", "deny-reason-text", TextInputStyle.Paragraph, maxLength: 500,
                              placeholder: "Enter the reason for denial...");

            await RespondWithModalAsync(modal.Build());
        }

        // DENY REASON
        [ModalInteraction("verify-deny-reason:*:*")]
        public async Task SubmitDenyReason(ulong userId, ulong messageId, DenyReasonModal modal)
        {
            await DeferAsync(ephemeral: true);

            var guild = Context.Guild;
            if (guild is null)
            {
                await FollowupAsync("Operation failed: guild context is not available.", ephemeral: true);
                return;
            }

            var cfg = await _guildSettingsService.GetAsync(Context.Guild.Id);

            var user = guild.GetUser(userId);
            if (user is null)
            {
                await FollowupAsync("Could not find the target user in this guild.", ephemeral: true);
                return;
            }

            var verificationChannel = cfg.Verify.ReviewChannelId is ulong id
                        ? guild.GetTextChannel(id)
                        : null;

            if (verificationChannel is null)
            {
                await FollowupAsync("Verification review channel not configured or not found.", ephemeral: true);
                return;
            }

            var message = await verificationChannel.GetMessageAsync(messageId) as IUserMessage;
            if (message is null)
            {
                await FollowupAsync("Could not find the review message to update.", ephemeral: true);
                return;
            }

            // Update the embed with the denial reason
            var embed = message.Embeds.FirstOrDefault()?.ToEmbedBuilder() ?? new EmbedBuilder();
            embed.AddField("Denial Reason", modal.Reason, inline: true);
            embed.WithFooter($"Denied ❌ by {Context.User.Username}");
            embed.WithColor(Discord.Color.Red);

            await message.ModifyAsync(m =>
            {
                m.Embeds = new[] { embed.Build() };
                m.Components = new ComponentBuilder().Build(); // remove approve/deny buttons
            });

            // Notify user via DM and then send a single followup about the result
            try
            {
                var displayName = await _guildSettingsService.GetGuildDisplayNameAsync(Context.Guild.Id) ?? "the server";
                await user.SendMessageAsync(
                    components: VerifyComponents.BuildDenialDmComponents(displayName, modal.Reason)
                );

                await FollowupAsync("Denial reason submitted and user has been notified.", ephemeral: true);
            }
            catch
            {
                await FollowupAsync($"Could not DM {user.Username}. They may have DMs disabled.", ephemeral: true);
            }

            // Log the denial
            try
            {
                var log = new LogEvent(
                    eventType: LogEventType.VerificationDenied,
                    guildId: Context.Guild.Id,
                    messageId: messageId,
                    username: Context.User.Username,
                    userId: Context.User.Id,
                    changes: $"{Context.User.Username} denied access for {user.Username} with reason: {modal.Reason}"
                );

                _dbContext.LogEvents.Add(log);
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Failed to log event: {e.Message}\n{e.StackTrace}");
            }
        }

        [ComponentInteraction("verify:metoo")]
        public async Task VerifyMeTooButtonAsync()
        {
            var cfg = await _guildSettingsService.GetAsync(Context.Guild.Id);

            var steamRequired = cfg?.Verify?.IsSteamRequired == true;
            var restrictionLevel = cfg?.Verify?.RestrictedAccess?.CurrentRestrictionLevel ?? AccessRestriction.Open;

            // Always attempt to log diagnostic info (best-effort) before any early returns
            try
            {
                var member = Context.User as SocketGuildUser;
                var userRoleNames = member is not null
                    ? member.Roles.Select(r => r.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                    : new List<string>();

                string allowedRolesDisplay;
                if (restrictionLevel == AccessRestriction.MembersOnly)
                {
                    var memberOnlyRoles = cfg?.Verify?.RestrictedAccess?.MemberOnlyRoles ?? new List<ulong>();
                    var alwaysRestrictRoles = cfg?.Verify?.RestrictedAccess?.AlwaysRestrictRoles ?? new List<ulong>();
                    var allowed = memberOnlyRoles.Except(alwaysRestrictRoles).ToList();
                    var allowedNames = allowed.Select(id => Context.Guild?.GetRole(id)?.Name ?? id.ToString()).ToList();
                    allowedRolesDisplay = allowedNames.Any() ? string.Join(", ", allowedNames) : "(none)";
                }
                else if (restrictionLevel == AccessRestriction.Open)
                {
                    allowedRolesDisplay = "(all)";
                }
                else // Restricted
                {
                    allowedRolesDisplay = "(none)";
                }

                Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} [VERIFY] User={Context.User.Username}({Context.User.Id}) Roles=[{string.Join(", ", userRoleNames)}] Mode={restrictionLevel} AllowedRoles=[{allowedRolesDisplay}]");
            }
            catch
            {
                // keep logging best-effort and never block the user flow
            }

            if (restrictionLevel == AccessRestriction.Restricted)
            {
                await RespondAsync(
                    "Verification is currently restricted. Please try again later.",
                    ephemeral: true);

                return;
            }

            if (restrictionLevel == AccessRestriction.MembersOnly)
            {
                var member = Context.User as SocketGuildUser;

                if (member is null)
                {
                    await RespondAsync(
                        "This can only be used inside a server.",
                        ephemeral: true);

                    return;
                }

                var memberOnlyRoles = cfg?.Verify?.RestrictedAccess?.MemberOnlyRoles?.ToHashSet()
                    ?? new HashSet<ulong>();

                var alwaysRestrictRoles = cfg?.Verify?.RestrictedAccess?.AlwaysRestrictRoles?.ToHashSet()
                    ?? new HashSet<ulong>();

                var allowedRoles = memberOnlyRoles
                    .Except(alwaysRestrictRoles)
                    .ToHashSet();

                var userRoleIds = member.Roles
                    .Select(role => role.Id);

                var hasAllowedRole = allowedRoles.Overlaps(userRoleIds);
                var isAlwaysRestricted = alwaysRestrictRoles.Overlaps(userRoleIds);

                if (isAlwaysRestricted || !hasAllowedRole)
                {
                    await RespondAsync(
                        "Verification is currently limited to configured allowed roles.",
                        ephemeral: true);

                    return;
                }
            }

            if (steamRequired)
            {
                await RespondWithModalAsync<VerifyMeSteamRequiredModal>("verify_me_required");
            }
            else
            {
                await RespondWithModalAsync<VerifyMeSteamOptionalModal>("verify_me_optional");
            }
        }

        [ModalInteraction("verify_me_required")]
        public async Task HandleVerifyRequiredAsync(VerifyMeSteamRequiredModal modal)
        {
            await DeferAsync(ephemeral: true);

            var result = await VerifyAsync(
                modal.File,
                modal.Faction,
                modal.Steam64Id,
                steamRequired: true);

            await FollowupAsync(result, ephemeral: true);
        }

        [ModalInteraction("verify_me_optional")]
        public async Task HandleVerifyOptionalAsync(VerifyMeSteamOptionalModal modal)
        {
            await DeferAsync(ephemeral: true);

            var result = await VerifyAsync(
                modal.File,
                modal.Faction,
                modal.Steam64Id,
                steamRequired: false);

            await FollowupAsync(result, ephemeral: true);
        }

        private async Task<string> VerifyAsync(IAttachment file, string faction, string steamInput, bool steamRequired)
        {
            if (steamRequired && string.IsNullOrWhiteSpace(steamInput))
                return "Steam ID is required for this server.";

            // Ensure only image files are accepted
            if (file == null)
                return "No file uploaded. Please attach an image.";

            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".gif", ".webp"
            };

            bool isImage = false;

            // Prefer ContentType when available
            if (!string.IsNullOrWhiteSpace(file.ContentType) && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                isImage = true;
            }
            else
            {
                // Fallback to file extension
                var ext = Path.GetExtension(file.Filename ?? string.Empty);
                if (!string.IsNullOrEmpty(ext) && allowedExtensions.Contains(ext))
                    isImage = true;
            }

            if (!isImage)
            {
                return "Only image files are allowed. Please upload a PNG, JPG, GIF or WEBP image.";
            }

            ulong? steam64Id = null;
            string? steamProfileUrl = null;

            if (!string.IsNullOrWhiteSpace(steamInput))
            {
                var steamResult = await _steamService.ResolveSteam64IdAsync(steamInput);

                if (steamResult.Status != SteamResolveStatus.Resolved)
                    return GetSteamResolveErrorMessage(steamResult);

                steam64Id = steamResult.Steam64Id;
                steamProfileUrl = steamResult.ProfileUrl;
            }

            var cfg = await _guildSettingsService.GetAsync(Context.Guild.Id);

            var reviewChannel = cfg.Verify.ReviewChannelId is ulong id
                    ? Context.Guild.GetTextChannel(id)
                    : null;

            if (reviewChannel == null)
            {
                await FollowupAsync("Verification channel not found. Please contact an admin.", ephemeral: true);
            }

            // Download attachment content
            using var response = await _httpClient.GetAsync(file.Url);
            response.EnsureSuccessStatusCode();
            await using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());

            // Build recent faction history (for the requester)
            var history = await _verificationHistory.GetTrackRecordAsync(Context.User.Id);
            string? currentWarNumber = null;
            try
            {
                currentWarNumber = await _warService.GetActiveWarNumberAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[WARN] Failed to resolve current war for verification history: {e.Message}");
            }

            var track = "_No previous approvals._";
            if (history.Count > 0)
            {
                var lines = new List<string>();
                foreach (var h in history)
                {
                    var guildName = await _guildSettingsService.GetGuildDisplayNameAsync(h.GuildId) ?? $"Guild {h.GuildId}";
                    var warLabel = string.IsNullOrWhiteSpace(h.WarNumber) ? "Legacy" : $"WC{h.WarNumber}";
                    var relativeTime = currentWarNumber is not null &&
                                       currentWarNumber != "000" &&
                                       currentWarNumber == h.WarNumber
                        ? $" — <t:{new DateTimeOffset(h.ApprovedAtUtc).ToUnixTimeSeconds()}:R>"
                        : string.Empty;
                    lines.Add($"• {h.Faction} — {warLabel}{relativeTime} — {guildName}");
                }
                track = string.Join("\n", lines);
            }

            // Steam checks
            string steamHistory = string.Empty;
            bool steamUsedByOthers = false;
            bool steamDiffersFromUserLast = false;

            // recent records for this user (fetch extra to allow merging/deduping)
            var userRecent = await _dbContext.VerificationRecords
                .Where(r => r.UserId == Context.User.Id && r.Steam64Id.HasValue)
                .OrderByDescending(r => r.ApprovedAtUtc)
                .Take(SteamHistoryTake * 2)
                .ToListAsync();

            // If user has a last steam and we provided a steam, detect difference
            var userLast = userRecent.FirstOrDefault();
            if (userLast != null && userLast.Steam64Id.HasValue && steam64Id.HasValue && userLast.Steam64Id.Value != steam64Id.Value)
                steamDiffersFromUserLast = true;

            // recent records for this exact Steam64Id (if provided)
            var exactMatches = new List<VerificationRecord>();
            if (steam64Id.HasValue)
            {
                exactMatches = await _dbContext.VerificationRecords
                    .Where(r => r.Steam64Id.HasValue && r.Steam64Id == steam64Id.Value)
                    .OrderByDescending(r => r.ApprovedAtUtc)
                    .Take(SteamHistoryTake * 2)
                    .ToListAsync();
            }

            // Merge, dedupe by record id, sort by time and take up to configured amount
            var merged = exactMatches
                .Concat(userRecent)
                .OrderByDescending(r => r.ApprovedAtUtc)
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .Take(SteamHistoryTake)
                .ToList();

            if (merged.Any())
            {
                // Precompute steam usage counts for steam ids present in merged
                var steamIds = merged.Where(r => r.Steam64Id.HasValue).Select(r => r.Steam64Id!.Value).Distinct().ToList();
                var steamUsageCounts = new Dictionary<ulong, int>();
                if (steamIds.Any())
                {
                    var groups = await _dbContext.VerificationRecords
                        .Where(r => r.Steam64Id.HasValue && steamIds.Contains(r.Steam64Id.Value))
                        .GroupBy(r => r.Steam64Id!.Value)
                        .Select(g => new { Steam = g.Key, DistinctUserCount = g.Select(x => x.UserId).Distinct().Count() })
                        .ToListAsync();

                    foreach (var g in groups)
                        steamUsageCounts[g.Steam] = g.DistinctUserCount;

                    // Set global flag whether the provided steam64Id (if any) is used by multiple accounts
                    if (steam64Id.HasValue && steamUsageCounts.TryGetValue(steam64Id.Value, out var cnt) && cnt > 1)
                        steamUsedByOthers = true;
                }

                // If the merged results all reference the same Steam64Id (and that Steam64Id exists),
                // condense the steam history into a single friendly line to save space and show positivity.
                if (steamIds.Count == 1)
                {
                    var singleSteam = steamIds[0];
                    // oldest record in the merged set (earliest ApprovedAtUtc)
                    var oldestUtc = merged.Min(r => r.ApprovedAtUtc);
                    var sharedMark = steamUsageCounts.TryGetValue(singleSteam, out var totalUsers) && totalUsers > 1
                        ? " ⚠️ Shared"
                        : string.Empty;

                    steamHistory = $"No changes since <t:{new DateTimeOffset(oldestUtc).ToUnixTimeSeconds()}:R>{sharedMark}";
                }
                else
                {
                    // Cache resolved Discord usernames to avoid repeated REST calls
                    var resolvedUsernames = new Dictionary<ulong, string>();

                    async Task<string> ResolveUsernameAsync(ulong uid)
                    {
                        if (resolvedUsernames.TryGetValue(uid, out var cached))
                            return cached;

                        string name;
                        try
                        {
                            IUser? u = Context.Client.GetUser(uid);
                            if (u == null)
                                u = await Context.Client.Rest.GetUserAsync(uid);

                            if (u != null)
                            {
                                name = u.Username;
                            }
                            else
                            {
                                name = $"User {uid}";
                            }
                        }
                        catch
                        {
                            name = $"User {uid}";
                        }

                        resolvedUsernames[uid] = name;
                        return name;
                    }

                    var lines = new List<string>();
                    foreach (var r in merged)
                    {
                        var guildName = await _guildSettingsService.GetGuildDisplayNameAsync(r.GuildId) ?? $"Guild {r.GuildId}";
                        var ownerName = await ResolveUsernameAsync(r.UserId);
                        var steamStr = r.Steam64Id.HasValue ? r.Steam64Id.Value.ToString() : "-";

                        var marks = new List<string>();

                        // Mark if this steam was used by multiple distinct Discord accounts
                        if (r.Steam64Id.HasValue && steamUsageCounts.TryGetValue(r.Steam64Id.Value, out var cnt) && cnt > 1 && r.UserId != Context.User.Id)
                            marks.Add("⚠️ Shared");

                        // Mark if the record's steam differs from the currently provided steam (when provided) for user's own records
                        if (steam64Id.HasValue && r.Steam64Id.HasValue && r.Steam64Id.Value != steam64Id.Value && r.UserId == Context.User.Id)
                            marks.Add("⚠️ Different");

                        var markText = marks.Any() ? " " + string.Join(" ", marks) : string.Empty;

                        lines.Add($"• {steamStr} — {ownerName} — <t:{new DateTimeOffset(r.ApprovedAtUtc).ToUnixTimeSeconds()}:R> — {guildName}{markText}");
                    }

                    steamHistory = string.Join("\n", lines);
                }
            }
            else
            {
                // No merged records -> leave empty
                steamHistory = "No known Steam history.";
            }

            // Resolve member and display name
            IGuildUser? member = Context.Guild.GetUser(Context.User.Id);
            if (member is null)
            {
                member = await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, Context.User.Id);
            }

            var displayName = (member as SocketGuildUser)?.DisplayName
                              ?? member?.Username
                              ?? Context.User.Username;

            var roleMentions = "-";
            if (member is not null)
            {
                var factionEnum = FactionParser.Parse(faction);
                roleMentions = await DiscordRolesList.GetDeltaRoleMentionsAsync(
                    _guildSettingsService,
                    member,
                    factionEnum
                );
                if (string.IsNullOrWhiteSpace(roleMentions))
                    roleMentions = "-";
            }

            var desc = $"A verification has been submitted by {displayName} — ({Context.User.Mention})";

            var steamProfileField = !string.IsNullOrWhiteSpace(steamProfileUrl) ? steamProfileUrl : "-";

            if (steamDiffersFromUserLast)
                steamProfileField += "\n⚠️ Provided Steam ID differs from this user's last used Steam64.";

            if (steamUsedByOthers)
                steamProfileField += "\n⚠️ Provided Steam ID was used by another Discord account.";

            bool isTrusted = cfg.Verify.TrustedRoles?.Any(member.RoleIds.Contains) == true;

            if (isTrusted)
            {
                // Build approved embed (no action buttons)

                var embedApproved = VerifyComponents.BuildEmbed(
                    title: "Verification Submission (Auto-Approved)",
                    description: $"A verification has been auto-approved for {displayName} — ({Context.User.Mention})",
                    faction: faction,
                    userId: Context.User.Id,
                    steamProfile: steamProfileField,
                    rolesToBeGranted: roleMentions,
                    steamHistory: steamHistory,
                    factionHistory: track,
                    color: faction == "Colonial" ? Discord.Color.DarkGreen : Discord.Color.DarkBlue,
                    footer: $"Auto-approved ✅ by ({Context.Client.CurrentUser.Username})"
                );

                var msgReview = await reviewChannel.SendFileAsync(stream, file.Filename, embed: embedApproved, components: new ComponentBuilder().Build());

                // Log the submission event
                try
                {
                    var msg = await GetOriginalResponseAsync();

                    var log = new LogEvent(
                        eventType: LogEventType.VerificationSubmitted,
                        guildId: Context.Guild.Id,
                        messageId: msg.Id,
                        username: Context.User.Username,
                        userId: Context.User.Id,
                        changes: $"{Context.User.Username} has submitted a verification submission."
                    );

                    _dbContext.LogEvents.Add(log);
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[ERROR] Failed to log event: {e.Message}\n{e.StackTrace}");
                }

                await CompleteApprovalAsync(
                    reviewMessage: msgReview, // no message to update since we're auto-approving
                    user: member,
                    factionEnum: FactionParser.Parse(faction),
                    approvedByUserId: null, // no approver since this is an auto-approval
                    approvedByName: string.Empty,
                    steam64Id: steam64Id
                );

                return "Verification submitted. Please wait for approval.";
            }
            else
            {
                var embedForReview = VerifyComponents.BuildEmbed(
                    title: "New Verification Submission",
                    description: desc,
                    faction: faction,
                    userId: Context.User.Id,
                    steamProfile: steamProfileField,
                    rolesToBeGranted: roleMentions,
                    steamHistory: steamHistory,
                    factionHistory: track,
                    color: faction == "Colonial" ? Discord.Color.DarkGreen : Discord.Color.DarkBlue,
                    footer: "Awaiting Approval"
                );

                await reviewChannel.SendFileAsync(stream, file.Filename, embed: embedForReview, components: VerifyComponents.BuildReviewComponents());

                // Log the submission event
                try
                {
                    var msg = await GetOriginalResponseAsync();

                    var log = new LogEvent(
                        eventType: LogEventType.VerificationSubmitted,
                        guildId: Context.Guild.Id,
                        messageId: msg.Id,
                        username: Context.User.Username,
                        userId: Context.User.Id,
                        changes: $"{Context.User.Username} has submitted a verification submission."
                    );

                    _dbContext.LogEvents.Add(log);
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[ERROR] Failed to log event: {e.Message}\n{e.StackTrace}");
                }

                return "Verification submitted. Please wait for approval.";
            }
        }

        private async Task CompleteApprovalAsync(IUserMessage? reviewMessage, IGuildUser user, Faction factionEnum, ulong? approvedByUserId, string approvedByName, ulong? steam64Id = null)
        {
            if (reviewMessage is null) return;

            // 1) Assign roles
            try
            {
                var rolesToAssign = await DiscordRolesList.GetDeltaRoleIdListAsync(
                    _guildSettingsService,
                    user,
                    factionEnum
                );

                if (rolesToAssign.Count > 0)
                {
                    if (user is SocketGuildUser socketUser)
                    {
                        await socketUser.AddRolesAsync(rolesToAssign);
                    }
                    else if (user is RestGuildUser restUser)
                    {
                        foreach (var r in rolesToAssign)
                        {
                            try { await restUser.AddRoleAsync(r); }
                            catch (Exception ex) { Console.WriteLine($"[WARN] AddRoleAsync failed for {restUser.Username}: {ex.Message}"); }
                        }
                    }
                    else
                    {
                        try { await user.AddRolesAsync(rolesToAssign); } catch { /* ignore */ }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Assigning roles failed: {e.Message}");
            }

            // 2) Update the review message
            if (approvedByUserId != null)
            {
                // This is manual approval via the "Approve" button.
                try
                {
                    var eb = reviewMessage.Embeds.FirstOrDefault()?.ToEmbedBuilder() ?? new EmbedBuilder();
                    eb.WithFooter($"Approved ✅ by {approvedByName}");
                    await reviewMessage.ModifyAsync(m =>
                    {
                        m.Embeds = new[] { eb.Build() };
                        m.Components = new ComponentBuilder().Build();
                    });
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[ERROR] Updating review message failed: {e.Message}");
                }
            }
            else
            {
                // This is an automatic approval (e.g. via a "Verify Me Too" button).
                try
                {
                    var displayName = (user as SocketGuildUser)?.DisplayName
                                      ?? user?.Username
                                      ?? Context.User.Username;

                    var eb = reviewMessage.Embeds.FirstOrDefault()?.ToEmbedBuilder() ?? new EmbedBuilder();

                    await reviewMessage.ModifyAsync(m =>
                    {
                        m.Embeds = new[] { eb.Build() };
                        m.Components = new ComponentBuilder().Build();
                    });
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[ERROR] Updating review message failed: {e.Message}");
                }
            }

            // 3) Record verification history and attempt DM
            try
            {
                var attachmentUrl = reviewMessage.Attachments.FirstOrDefault()?.Url;
                string? warNumber = null;

                try
                {
                    warNumber = await _warService.GetActiveWarNumberAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[WARN] Failed to resolve current war for verification approval: {e.Message}");
                }

                await _verificationHistory.LogApprovalAsync(
                    guildId: Context.Guild.Id,
                    userId: user.Id,
                    faction: factionEnum,
                    imageUrl: attachmentUrl,
                    approvedByUserId: approvedByUserId,
                    steam64Id: steam64Id,
                    warNumber: warNumber
                );
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Failed to log verification history: {e.Message}");
            }

            try
            {
                var cfg = await _guildSettingsService.GetAsync(Context.Guild.Id);

                var factionSecureChannel = factionEnum switch
                {
                    Faction.Colonial => cfg.Verify.ColonialSecureChannelId is ulong colonialId ? Context.Guild.GetTextChannel(colonialId) : null,
                    Faction.Warden => cfg.Verify.WardenSecureChannelId is ulong wardenId ? Context.Guild.GetTextChannel(wardenId) : null,
                    _ => null
                };
                var factionLogoUrl = factionEnum switch
                {
                    Faction.Colonial => cfg.GuildLogoColonial,
                    Faction.Warden => cfg.GuildLogoWarden,
                    _ => null
                };

                var displayName = await _guildSettingsService.GetGuildDisplayNameAsync(Context.Guild.Id) ?? "";

                await user.SendMessageAsync(
                    components: VerifyComponents.BuildApprovalDmComponents(
                        displayName,
                        factionSecureChannel,
                        factionLogoUrl)
                );
            }
            catch
            {
                Console.WriteLine($"[ERROR] Failed to send a DM to {user.Username}. They may have DMs disabled.");
            }

            // 4) Persist a LogEvent (use approvedByUserId when available, otherwise fall back to target user id)
            try
            {
                var log = new LogEvent(
                    eventType: LogEventType.VerificationApproved,
                    guildId: Context.Guild.Id,
                    messageId: reviewMessage.Id,
                    username: approvedByName,
                    userId: approvedByUserId ?? user.Id,
                    changes: $"{approvedByName} approved access for {user.Username}"
                );

                _dbContext.LogEvents.Add(log);
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Failed to log approval event: {e.Message}\n{e.StackTrace}");
            }
        }

        private static string GetSteamResolveErrorMessage(SteamResolveResult steamResult)
        {
            return steamResult.Status switch
            {
                SteamResolveStatus.EmptyInput =>
                    "Steam ID is required for this server.",

                SteamResolveStatus.MissingApiKey =>
                    "Steam verification is not configured correctly. Please contact an administrator.",

                SteamResolveStatus.InvalidInput =>
                    "Please provide a valid Steam64 ID, Steam profile URL, or Steam vanity name.",

                SteamResolveStatus.NotFound =>
                    "Could not find a Steam profile matching that input.",

                SteamResolveStatus.RateLimited =>
                    "Steam is currently rate-limiting profile lookups. Please try again later.",

                SteamResolveStatus.SteamUnavailable =>
                    "Steam is currently unavailable. Please try again later.",

                SteamResolveStatus.Failed when !string.IsNullOrWhiteSpace(steamResult.Message) =>
                    $"Could not resolve the Steam profile. {steamResult.Message}",

                _ =>
                    "Could not resolve the Steam profile. Please check the input and try again."
            };
        }
    }
}
