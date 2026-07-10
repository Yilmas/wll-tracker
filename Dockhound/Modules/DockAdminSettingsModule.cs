using Discord;
using Discord.Interactions;
using Dockhound.Config;
using Dockhound.Logs;
using Dockhound.Models;
using Dockhound.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dockhound.Modules
{
    public partial class DockAdminModule
    {
        [Group("settings", "Settings for Dockhound")]
        public class DockSettings : InteractionModuleBase<SocketInteractionContext>
        {
            private readonly DockhoundContext _dbContext;
            private readonly HttpClient _httpClient;
            private readonly IGuildSettingsService _guildSettingsService;
            private readonly IDbContextFactory<DockhoundContext> _dbFactory;

            public DockSettings(
                DockhoundContext dbContext,
                HttpClient httpClient,
                IConfiguration config,
                IOptions<AppSettings> appSettings,
                IOptionsMonitor<AppSettings> monitorSettings,
                IAppSettingsService appSettingsService,
                IGuildSettingsService guildSettingsService,
                IDbContextFactory<DockhoundContext> dbFactory)
            {
                _dbContext = dbContext;
                _httpClient = httpClient;
                _guildSettingsService = guildSettingsService;
                _dbFactory = dbFactory;
            }

                [RequireUserPermission(GuildPermission.ManageGuild)]
                [SlashCommand("view", "Displays current guild settings")]
                public async Task GetGuildSettings()
                {
                    await DeferAsync(ephemeral: true);

                    var cfg = await _guildSettingsService.GetAsync(Context.Guild.Id);

                    var node = JsonSerializer.SerializeToNode(
                        cfg,
                        new JsonSerializerOptions { WriteIndented = true }
                    ) as JsonObject;

                    var pretty = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
                    var bytes = Encoding.UTF8.GetBytes(pretty);

                    await using var ms = new MemoryStream(bytes);
                    ms.Position = 0;

                    var filename = $"guild-{Context.Guild.Id}-settings.json";
                    await FollowupWithFileAsync(ms, filename,
                        text: $"Guild settings for `{Context.Guild.Id}` (attached).", ephemeral: true);
                }

                [RequireContext(ContextType.Guild)]
                [RequireUserPermission(GuildPermission.ManageGuild)]
                [SlashCommand("config-update", "Upload a GuildConfig JSON file to update this guild's configuration.")]
                public async Task UpdateConfigAsync(
                    [Summary("file", "JSON file containing Dockhound GuildConfig")] IAttachment file)
                {
                    await DeferAsync(ephemeral: true);

                    var targetGuildId = Context.Guild!.Id;

                    if (file.Size <= 0 || file.Size > 2_000_000)
                    {
                        await FollowupAsync("❌ The file is empty or larger than 2 MB. Please upload a valid JSON under 2 MB.", ephemeral: true);
                        return;
                    }

                    string json;
                    try
                    {
                        using var stream = await _httpClient.GetStreamAsync(file.Url);
                        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                        json = await reader.ReadToEndAsync();
                    }
                    catch (Exception ex)
                    {
                        await FollowupAsync($"❌ Failed to download the attachment: `{ex.Message}`", ephemeral: true);
                        return;
                    }

                    var deserializeOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = JsonNumberHandling.AllowReadingFromString,
                    };

                    GuildConfig? cfg;
                    try
                    {
                        cfg = JsonSerializer.Deserialize<GuildConfig>(json, deserializeOptions)
                              ?? throw new JsonException("Deserialized result was null.");
                    }
                    catch (JsonException jx)
                    {
                        await FollowupAsync($"❌ Invalid JSON for `GuildConfig`: `{jx.Message}`", ephemeral: true);
                        return;
                    }

                    cfg.SchemaVersion = cfg.SchemaVersion <= 0 ? _guildSettingsService.CurrentSchemaVersion : cfg.SchemaVersion;
                    cfg.Roles ??= new List<GuildConfig.RoleSet>();
                    cfg.Verify ??= new GuildConfig.VerificationSettings();
                    cfg.Verify.RestrictedAccess ??= new GuildConfig.RestrictedAccessSettings();
                    cfg.Verify.RecruitAssignerRoles ??= new List<ulong>();
                    cfg.Verify.AllyAssignerRoles ??= new List<ulong>();
                    cfg.Verify.TrustedRoles ??= new List<ulong>();
                    cfg.Verify.RestrictedAccess.AlwaysRestrictRoles ??= new List<ulong>();
                    cfg.Verify.RestrictedAccess.MemberOnlyRoles ??= new List<ulong>();
                    cfg.Honeypot ??= new GuildConfig.HoneypotSettings();
                    cfg.Honeypot.MessagePruneDays = Math.Clamp(cfg.Honeypot.MessagePruneDays, 0, 7);

                    try
                    {
                        await _guildSettingsService.UpdateAsync(
                            targetGuildId,
                            cfg,
                            changedBy: $"{Context.User.Username}#{Context.User.Id}"
                        );
                    }
                    catch (InvalidOperationException cex)
                    {
                        await FollowupAsync($"⚠️ Concurrency conflict while saving. Please retry.\n`{cex.InnerException?.Message ?? cex.Message}`", ephemeral: true);
                        return;
                    }
                    catch (Exception ex)
                    {
                        await FollowupAsync($"❌ Failed to save configuration: `{ex.Message}`", ephemeral: true);
                        return;
                    }

                    var saved = await _guildSettingsService.GetAsync(targetGuildId);
                    var pretty = JsonSerializer.Serialize(saved, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    if (pretty.Length > 1800)
                    {
                        var bytes = Encoding.UTF8.GetBytes(pretty);
                        await using var s = new MemoryStream(bytes);
                        await FollowupWithFileAsync(s, $"guild-{targetGuildId}-config.json",
                            text: $"✅ Updated configuration for guild `{targetGuildId}`. Here is the saved JSON:", ephemeral: true);
                    }
                    else
                    {
                        await FollowupAsync($"✅ Updated configuration for guild `{targetGuildId}`. Saved JSON:\n```json\n{pretty}\n```", ephemeral: true);
                    }

                    if (Context.Guild != null && Context.Guild.SafetyAlertsChannel.Id is ulong modChannelId)
                    {
                        if (Context.Guild.GetChannel(modChannelId) is IMessageChannel modChannel)
                        {
                            var embed = new EmbedBuilder()
                                .WithTitle("Configuration Updated")
                                .WithDescription($"Guild configuration was updated by {Context.User.Mention}")
                                .WithColor(Color.Orange)
                                .WithCurrentTimestamp()
                                .Build();

                            await modChannel.SendMessageAsync(embed: embed);
                        }
                    }
                }

                [RequireContext(ContextType.Guild)]
                [RequireUserPermission(GuildPermission.ManageGuild)]
                [SlashCommand("guild-update", "Update this guild's name and tag.")]
                public async Task UpdateGuildAsync(
                    [Summary("name", "New name of the guild")] string? name = null,
                    [Summary("tag", "Short tag/identifier for the guild")] string? tag = null)
                {
                    await DeferAsync(ephemeral: true);

                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(tag))
                    {
                        await FollowupAsync("❌ You must specify at least one of `name` or `tag`.", ephemeral: true);
                        return;
                    }

                    await using var db = await _dbFactory.CreateDbContextAsync();
                    var guildId = Context.Guild.Id;

                    var guild = await db.Guilds.FirstOrDefaultAsync(g => g.GuildId == guildId);
                    if (guild is null)
                    {
                        await FollowupAsync("❌ You must run the command config-update first!", ephemeral: true);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                        guild.Name = name;

                    if (!string.IsNullOrWhiteSpace(tag))
                        guild.Tag = tag;

                    try
                    {
                        await db.SaveChangesAsync();
                    }
                    catch (DbUpdateException ex)
                    {
                        await FollowupAsync($"❌ Failed to update guild info: {ex.InnerException?.Message ?? ex.Message}", ephemeral: true);
                        return;
                    }

                    var embed = new EmbedBuilder()
                        .WithTitle("✅ Guild Updated")
                        .WithColor(Color.Green)
                        .AddField("Name", string.IsNullOrWhiteSpace(guild.Name) ? "-" : guild.Name, inline: true)
                        .AddField("Tag", string.IsNullOrWhiteSpace(guild.Tag) ? "-" : guild.Tag, inline: true)
                        .WithFooter($"Updated by {Context.User}")
                        .WithCurrentTimestamp()
                        .Build();

                    await FollowupAsync(embed: embed, ephemeral: true);

                    if (Context.Guild != null && Context.Guild.SafetyAlertsChannel.Id is ulong modChannelId)
                    {
                        if (Context.Guild.GetChannel(modChannelId) is IMessageChannel modChannel)
                        {
                            var alertEmbed = new EmbedBuilder()
                                .WithTitle("Guild Updated")
                                .WithDescription($"Guild name or tag was updated by {Context.User.Mention}")
                                .WithColor(Color.Orange)
                                .WithCurrentTimestamp()
                                .Build();

                            await modChannel.SendMessageAsync(embed: alertEmbed);
                        }
                    }
                }

                [RequireUserPermission(GuildPermission.ViewAuditLog | GuildPermission.ManageMessages)]
                [SlashCommand("logs", "Export Dockhound log events matching optional filters.")]
                public async Task ExportLogsAsync(
                    [Summary("days", "Days into the past, from 1 to 365.")] int days,
                    [Summary("user", "Only include events created by this user.")] IUser? user = null,
                    [Summary("message_id", "Only include events for this Discord message ID.")] string? messageId = null,
                    [Summary("event_type", "Only include this event type.")] LogEventType? eventType = null,
                    [Summary("limit", "Maximum rows to export, from 1 to 5000.")] int limit = 1000)
                {
                    await DeferAsync(ephemeral: true);

                    var clampedDays = Math.Clamp(days, 1, 365);
                    var clampedLimit = Math.Clamp(limit, 1, 5000);
                    ulong? parsedMessageId = null;

                    if (!string.IsNullOrWhiteSpace(messageId))
                    {
                        if (!ulong.TryParse(messageId, out var msgId))
                        {
                            await FollowupAsync("Invalid message ID format.", ephemeral: true);
                            return;
                        }

                        parsedMessageId = msgId;
                    }

                    var guildId = Context.Guild.Id;
                    var generatedAt = DateTime.UtcNow;
                    var cutoff = generatedAt.AddDays(-clampedDays);

                    var query = _dbContext.LogEvents
                        .AsNoTracking()
                        .Where(l => (l.GuildId == guildId || l.GuildId == null) && l.Updated >= cutoff);

                    if (user is not null)
                        query = query.Where(l => l.UserId == user.Id);

                    if (parsedMessageId is ulong msgFilter)
                        query = query.Where(l => l.MessageId == msgFilter);

                    if (eventType is LogEventType typeFilter)
                    {
                        var eventNameMatches = typeFilter.GetStoredEventNameMatches();
                        query = query.Where(l => eventNameMatches.Contains(l.EventName));
                    }

                    var logs = await query
                        .OrderByDescending(l => l.Updated)
                        .Take(clampedLimit + 1)
                        .ToListAsync();

                    var truncated = logs.Count > clampedLimit;
                    if (truncated)
                        logs = logs.Take(clampedLimit).ToList();

                    var report = BuildLogExportReport(
                        guildId,
                        Context.Guild.Name,
                        generatedAt,
                        clampedDays,
                        user,
                        parsedMessageId,
                        eventType,
                        clampedLimit,
                        truncated,
                        logs);

                    var bytes = Encoding.UTF8.GetBytes(report);
                    await using var stream = new MemoryStream(bytes);
                    var filename = $"dockhound-logs-{guildId}-{generatedAt:yyyyMMdd-HHmmss}.txt";

                    await FollowupWithFileAsync(
                        stream,
                        filename,
                        text: $"Exported {logs.Count} log event(s){(truncated ? $"; limited to the newest {clampedLimit}." : ".")}",
                        ephemeral: true);
                }

                private static string BuildLogExportReport(
                    ulong guildId,
                    string guildName,
                    DateTime generatedAtUtc,
                    int days,
                    IUser? user,
                    ulong? messageId,
                    LogEventType? eventType,
                    int limit,
                    bool truncated,
                    IReadOnlyCollection<LogEvent> logs)
                {
                    var sb = new StringBuilder();

                    sb.AppendLine("Dockhound Log Export");
                    sb.AppendLine($"Guild: {guildName} ({guildId})");
                    sb.AppendLine($"Generated: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
                    sb.AppendLine("Filters:");
                    sb.AppendLine($"  Days: {days}");
                    sb.AppendLine($"  User: {(user is null ? "-" : $"{user.Username} ({user.Id})")}");
                    sb.AppendLine($"  MessageId: {(messageId?.ToString() ?? "-")}");
                    sb.AppendLine($"  EventType: {(eventType?.ToDisplayName() ?? "-")}");
                    sb.AppendLine($"  Limit: {limit}");
                    sb.AppendLine($"Results: {logs.Count}");
                    sb.AppendLine($"Truncated: {(truncated ? "yes" : "no")}");
                    sb.AppendLine();

                    foreach (var log in logs)
                    {
                        sb.AppendLine($"[{DateTime.SpecifyKind(log.Updated, DateTimeKind.Utc):yyyy-MM-dd HH:mm:ss} UTC]");
                        sb.AppendLine($"Event: {log.EventType.ToDisplayName()} ({log.EventType})");
                        sb.AppendLine($"User: {log.Username} ({log.UserId})");
                        sb.AppendLine($"MessageId: {log.MessageId}");
                        sb.AppendLine("Changes:");
                        sb.AppendLine(Indent(log.Changes));
                        sb.AppendLine();
                    }

                    return sb.ToString();
                }

                private static string Indent(string? value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        return "  -";

                    return string.Join(
                        Environment.NewLine,
                        value.Replace("\r\n", "\n").Split('\n').Select(line => $"  {line}"));
                }
        }
    }
}
