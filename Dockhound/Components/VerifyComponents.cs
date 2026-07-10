using Discord;
using Dockhound.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dockhound.Components
{
    public static class VerifyComponents
    {
        /// <summary>
        /// Builds the standard component row for a verify review message.
        /// </summary>
        public static MessageComponent BuildReviewComponents()
        {
            return new ComponentBuilder()
                .WithButton(
                    label: "Approve",
                    customId: $"verify:approve",
                    style: ButtonStyle.Success)
                .WithButton(
                    label: "Deny",
                    customId: $"verify:deny",
                    style: ButtonStyle.Danger)
                .Build();
        }

        /// <summary>
        /// Builds the Components V2 direct message sent after a verification is approved.
        /// </summary>
        public static MessageComponent BuildApprovalDmComponents(
            string displayName,
            ITextChannel? factionSecureChannel,
            string? factionLogoUrl)
        {
            var guildName = string.IsNullOrWhiteSpace(displayName) ? "this server" : displayName;
            var accessMessage = factionSecureChannel is not null
                ? $"You now have access to faction-specific channels, such as:\n{factionSecureChannel.Mention}."
                : "Faction-specific channels are now available to you.";
            var approvalContainer = new ContainerBuilder()
                .WithAccentColor(Color.DarkGreen);

            if (!string.IsNullOrWhiteSpace(factionLogoUrl))
            {
                approvalContainer.WithSection(new SectionBuilder()
                    .WithTextDisplay("## ✅ Verification approved")
                    .WithTextDisplay($"Your verification for **{guildName}** has been approved!")
                    .WithAccessory(new ThumbnailBuilder(
                        new UnfurledMediaItemProperties(factionLogoUrl),
                        "Faction logo")));
            }
            else
            {
                approvalContainer
                    .WithTextDisplay("## ✅ Verification approved")
                    .WithTextDisplay($"Your verification for **{guildName}** has been approved!");
            }

            approvalContainer
                .WithSeparator()
                .WithTextDisplay("### Secure Channels")
                .WithTextDisplay(accessMessage);

            return new ComponentBuilderV2()
                .WithContainer(approvalContainer)
                .Build();
        }

        /// <summary>
        /// Builds the Components V2 direct message sent after a verification is denied.
        /// </summary>
        public static MessageComponent BuildDenialDmComponents(string displayName, string? reason)
        {
            var guildName = string.IsNullOrWhiteSpace(displayName) ? "this server" : displayName;
            var denialReason = string.IsNullOrWhiteSpace(reason) ? "No reason was provided." : reason;

            return new ComponentBuilderV2()
                .WithContainer(new ContainerBuilder()
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("## ❌ Verification denied")
                    .WithTextDisplay($"Your verification for **{guildName}** has been denied.")
                    .WithSeparator()
                    .WithTextDisplay("### Reason")
                    .WithTextDisplay(denialReason))
                .Build();
        }

        /// <summary>
        /// Build a verification review embed used for both manual and auto-approved posts.
        /// </summary>
        public static Embed BuildEmbed(string title, string description, string faction, ulong userId, string? steamProfile, string rolesToBeGranted, string? steamHistory, string factionHistory, Color color, string footer)
        {
            var eb = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(color)
                .WithCurrentTimestamp()
                .WithFooter(footer);

            // Faction + User ID
            eb.AddField("Faction", string.IsNullOrWhiteSpace(faction) ? "-" : faction, inline: true);
            eb.AddField("User ID", userId.ToString(), inline: true);

            // Steam Profile - show explicit "-" if not provided
            eb.AddField("Steam Profile", !string.IsNullOrWhiteSpace(steamProfile) ? steamProfile! : "-", inline: false);

            // Roles
            eb.AddField("Roles to be granted", string.IsNullOrWhiteSpace(rolesToBeGranted) ? "-" : rolesToBeGranted, inline: false);

            // Steam history: render visually blank if empty (no offense), otherwise display provided content
            var steamHistoryField = string.IsNullOrEmpty(steamHistory) ? "\u200b" : steamHistory!;
            eb.AddField("Steam history (recent)", steamHistoryField, inline: false);

            // Faction history (last 5)
            eb.AddField("Faction history (last 5)", string.IsNullOrWhiteSpace(factionHistory) ? "-" : factionHistory, inline: false);

            return eb.Build();
        }

        /// <summary>
        /// Builds the Components V2 verification information card.
        /// </summary>
        public static MessageComponent BuildInfoV2Components(string? imageUrl, AccessRestriction restriction, string displayName, bool isSteamRequired)
        {
            var guildName = string.IsNullOrWhiteSpace(displayName) ? "this server" : displayName;
            var container = new ContainerBuilder()
                .WithAccentColor(Color.Gold)
                .WithTextDisplay("## Looking to Verify?");

            if (restriction == AccessRestriction.Restricted)
            {
                container.WithTextDisplay("⚠️ Verification is currently **restricted**. No verification is allowed at this time.");
            }
            else
            {
                var stepNumber = isSteamRequired ? 4 : 3;
                var steps = $"1. Click **Verify** below\n2. Select `Colonial` or `Warden`" +
                    (isSteamRequired ? "\n3. Provide your Steam profile URL or Steam64ID." : string.Empty) +
                    $"\n{stepNumber}. Upload your `MAP SCREEN Screenshot`.";

                container.WithTextDisplay("Follow the steps below to get yourself verified.");

                if (restriction == AccessRestriction.MembersOnly)
                {
                    container
                        .WithSeparator()
                        .WithTextDisplay("### ⚠️ Members only")
                        .WithTextDisplay($"Verification is currently limited to **{guildName}** members.");
                }

                container
                    .WithSeparator()
                    .WithTextDisplay("### Steps to verify")
                    .WithTextDisplay(steps)
                    .WithSeparator()
                    .WithTextDisplay("### Required screenshot")
                    .WithTextDisplay("Map Screenshot **ONLY**. You will be **rejected** if you submit a screenshot of the Secure Map or from Home Region.")
                    .WithSeparator()
                    .WithTextDisplay("### How long will it take?")
                    .WithTextDisplay("If you have given us the correct information, one of the officers will handle your request as soon as possible.");

                if (!string.IsNullOrWhiteSpace(imageUrl))
                    container.WithMediaGallery(new[] { imageUrl });
            }

            container.WithActionRow(new[]
            {
                new ButtonBuilder("Verify", "verify:metoo", ButtonStyle.Success)
            });

            return new ComponentBuilderV2()
                .WithContainer(container)
                .Build();
        }
    }
}
