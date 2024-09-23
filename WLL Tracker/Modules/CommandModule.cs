﻿using Discord.Interactions;
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
    // Dependencies can be accessed through Property injection, public properties with public setters will be set by the service provider
    public InteractionService Commands { get; set; }

    private InteractionHandler _handler;

    // Constructor injection is also a valid way to access the dependencies
    public CommandModule(InteractionHandler handler)
    {
        _handler = handler;
    }

    // [Group] will create a command group. [SlashCommand]s and [ComponentInteraction]s will be registered with the group prefix
    [DefaultMemberPermissions(GuildPermission.ManageMessages)]
    [Group("tracker", "desc")]
    public class GroupExample : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("setup", "Initial setup of a tracker.")]
        public async Task SetupTracker(TrackerType type)
        {
            long seconds = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;

            var embed = new EmbedBuilder()
                .WithTitle("RR Container Yard")
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
    }
}
