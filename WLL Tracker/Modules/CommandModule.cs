using Discord.Interactions;
using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WLL_Tracker.Enums;
using System.Reflection.Emit;

namespace WLL_Tracker.Modules;

public class CommandModule : InteractionModuleBase<SocketInteractionContext>
{
    public InteractionService Commands { get; set; }

    private InteractionHandler _handler;

    public CommandModule(InteractionHandler handler)
    {
        _handler = handler;
    }

    [CommandContextType(InteractionContextType.Guild)]
    [DefaultMemberPermissions(GuildPermission.ManageMessages)]
    [Group("tracker", "desc")]
    public class GroupSetup : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("setup", "Initial setup of a tracker.")]
        public async Task SetupTracker(TrackerType type, string location = "RR")
        {
            long seconds = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;

            var embed = new EmbedBuilder()
                .WithTitle($"{location} Container Yard")
                .WithDescription("Last Updated by " + Context.User.Mention + " <t:" + seconds + ":R>")
                .AddField("Red", 0, true)
                .AddField("Green", 0, true)
                .AddField("Blue", 0, true)
                .AddField("DarkBlue", 0, true)
                .AddField("White", 0, true)
                .WithFields(
                    new EmbedFieldBuilder()
                        .WithName("Job Board")
                        .WithValue("Waiting for Jobs ...")
                    )
                .WithFooter("Brough to you by WLL Cannonsmoke");

            var builder = new ComponentBuilder()
                .WithButton(label: "Edit Container Count", "btn-container-count", style: ButtonStyle.Secondary)
                .WithButton(label: "Edit Job Board", "btn-board-edit", style: ButtonStyle.Secondary);

            await RespondAsync(embed: embed.Build(), components: builder.Build());
        }

        [SlashCommand("log", "Display recent log of activity.")]
        public async Task TrackerLog(int rows, string messageId)
        {
            long seconds = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;

            var msg = await Context.Channel.GetMessageAsync(ulong.Parse(messageId));
            var location = msg.Embeds.First().Title.Split(' ')[0];

            if (msg != null)
            {
                var embed = new EmbedBuilder()
                .WithTitle($"Log for: {location}")
                .WithDescription("Last Update <t:" + seconds + ":R>")
                .WithFields(
                    new EmbedFieldBuilder()
                        .WithName("Log")
                        .WithValue("location\nasd")
                    )
                .WithFooter("Brough to you by WLL Cannonsmoke");

                await RespondAsync(embed: embed.Build());
            }
            else
            {
                await RespondAsync("No message with that Id.", ephemeral: true);
                return;
            }
            
        }


    }
}

