using Discord;
using Discord.Interactions;
using Dockhound.Components;
using Dockhound.Enums;
using Dockhound.Models;
using Dockhound.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Dockhound.Modules
{
    public partial class DockAdminModule
    {
        [Group("verify", "Admin root for Verify Module")]
        public class VerifyAdminSetup : InteractionModuleBase<SocketInteractionContext>
        {
            private readonly AppSettings _settings;
            private readonly IGuildSettingsService _guildSettingsService;

            public VerifyAdminSetup(DockhoundContext dbContext, HttpClient httpClient, IConfiguration config, IOptions<AppSettings> appSettings, IAppSettingsService appSettingsService, IGuildSettingsService guildSettingsService)
            {
                _settings = appSettings.Value;
                _guildSettingsService = guildSettingsService;
            }

                [RequireUserPermission(GuildPermission.ManageGuild)]
                [SlashCommand("info", "Provides information on the verification process.")]
                public async Task VerifyInfo()
                {
                    await DeferAsync(ephemeral: true);

                    var cfg = await _guildSettingsService.GetAsync(Context.Guild.Id);
                    string imageUrl = cfg.Verify.ImageUrl;
                    var displayName = await _guildSettingsService.GetGuildDisplayNameAsync(Context.Guild.Id) ?? "the server";
                    var restriction = cfg.Verify?.RestrictedAccess?.CurrentRestrictionLevel ?? AccessRestriction.Open;
                    var steamRequired = cfg.Verify?.IsSteamRequired ?? false;

                    var infoComponents = VerifyComponents.BuildInfoV2Components(imageUrl, restriction, displayName, steamRequired);

                    if (Context.Channel is IMessageChannel postChannel)
                    {
                        var msg = await postChannel.SendMessageAsync(components: infoComponents);

                        await _guildSettingsService.UpdateRestrictedAccessAsync(
                            Context.Guild.Id,
                            postChannel.Id,
                            msg.Id,
                            restriction,
                            Context.User.Username + "#" + Context.User.Id
                        );

                        await FollowupAsync($"Info message posted and saved (Channel: {postChannel.Id}, Message: {msg.Id}).", ephemeral: true);
                        return;
                    }

                    await FollowupAsync(components: infoComponents, ephemeral: true);
                }

                [RequireUserPermission(GuildPermission.ManageGuild)]
                [SlashCommand("restrict", "Set verification access mode")]
                public async Task VerifyRestrict([Summary("setting", "Restricted / MembersOnly / Open")] AccessRestriction accessLevel)
                {
                    await DeferAsync(ephemeral: true);

                    var cfg = await _guildSettingsService.GetAsync(Context.Guild.Id);

                    var ra = await _guildSettingsService.GetRestrictedAccessAsync(Context.Guild.Id);
                    await _guildSettingsService.UpdateRestrictedAccessAsync(
                        Context.Guild.Id,
                        ra.ChannelId,
                        ra.MessageId,
                        accessLevel,
                        Context.User.Username + "#" + Context.User.Id
                    );

                    if (ra.ChannelId.HasValue && ra.MessageId.HasValue)
                    {
                        var infoChannel = Context.Guild.GetTextChannel(ra.ChannelId.Value);
                        if (infoChannel is not null)
                        {
                            try
                            {
                                var msg = await infoChannel.GetMessageAsync(ra.MessageId.Value) as IUserMessage;
                                if (msg is not null)
                                {
                                    var displayName = await _guildSettingsService.GetGuildDisplayNameAsync(Context.Guild.Id) ?? "the server";
                                    var imageUrl = cfg.Verify.ImageUrl;
                                    var steamRequired = cfg.Verify.IsSteamRequired;

                                    var infoComponents = VerifyComponents.BuildInfoV2Components(imageUrl, accessLevel, displayName, steamRequired);
                                    var flags = (msg.Flags ?? MessageFlags.None) | MessageFlags.ComponentsV2;

                                    await msg.ModifyAsync(m =>
                                    {
                                        m.Embeds = Array.Empty<Embed>();
                                        m.Components = infoComponents;
                                        m.Flags = flags;
                                    });
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                    else
                    {
                        if (Context.Channel is IMessageChannel createChannel)
                        {
                            try
                            {
                                var displayName = await _guildSettingsService.GetGuildDisplayNameAsync(Context.Guild.Id) ?? "the server";
                                var imageUrl = cfg.Verify.ImageUrl;
                                var steamRequired = cfg.Verify.IsSteamRequired;

                                var infoComponents = VerifyComponents.BuildInfoV2Components(imageUrl, accessLevel, displayName, steamRequired);
                                var posted = await createChannel.SendMessageAsync(components: infoComponents);

                                await _guildSettingsService.UpdateRestrictedAccessAsync(
                                    Context.Guild.Id,
                                    createChannel.Id,
                                    posted.Id,
                                    accessLevel,
                                    Context.User.Username + "#" + Context.User.Id
                                );
                            }
                            catch
                            {
                            }
                        }
                    }

                    await FollowupAsync(
                        accessLevel switch
                        {
                            AccessRestriction.Open =>
                                "Verification mode set to **Open**.",
                            AccessRestriction.MembersOnly =>
                                "Verification mode set to **MembersOnly**.",
                            AccessRestriction.Restricted =>
                                "Verification mode set to **Restricted**.",
                            _ => $"Verification mode set to **{accessLevel}**."
                        },
                        ephemeral: true
                    );
                }
        }
    }
}
