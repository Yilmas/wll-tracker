using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic;
using System;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.XPath;

namespace WLL_Tracker;


public class InteractionHandler
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _handler;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;

    public InteractionHandler(DiscordSocketClient client, InteractionService handler, IServiceProvider services, IConfiguration config)
    {
        _client = client;
        _handler = handler;
        _services = services;
        _configuration = config;
    }

    public async Task InitializeAsync()
    {
        _client.Ready += ReadyAsync;
        _handler.Log += LogAsync;

        await _handler.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

        _client.InteractionCreated += HandleInteraction;
        _handler.InteractionExecuted += HandleInteractionExecute;
        _client.SelectMenuExecuted += SelectMenuExecuted;
        _client.ModalSubmitted += ModalSubmitted;
        _client.ButtonExecuted += ButtonExecuted;
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log);
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        //await _handler.RegisterCommandsGloballyAsync();
        foreach (var item in _client.Guilds)
        {
            await _handler.RegisterCommandsToGuildAsync(guildId: item.Id, deleteMissing: true);
        }
    }

    private async Task HandleInteraction(SocketInteraction interaction)
    {
        try
        {
            // Create an execution context that matches the generic type parameter of your InteractionModuleBase<T> modules.
            var context = new SocketInteractionContext(_client, interaction);

            // Execute the incoming command.
            var result = await _handler.ExecuteCommandAsync(context, _services);

            // Due to async nature of InteractionFramework, the result here may always be success.
            // That's why we also need to handle the InteractionExecuted event.
            if (!result.IsSuccess)
                switch (result.Error)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        await context.Interaction.RespondAsync($"Unmet Precondition: {result.ErrorReason}", ephemeral: true);
                        break;
                    case InteractionCommandError.UnknownCommand:
                        //await context.Interaction.RespondAsync("Unknown command", ephemeral: true);
                        break;
                    case InteractionCommandError.BadArgs:
                        await context.Interaction.RespondAsync("Invalid number or arguments", ephemeral: true);
                        break;
                    case InteractionCommandError.Exception:
                        await context.Interaction.RespondAsync($"Command exception: {result.ErrorReason}", ephemeral: true);
                        break;
                    case InteractionCommandError.Unsuccessful:
                        await context.Interaction.RespondAsync("Command could not be executed", ephemeral: true);
                        break;
                    default:
                        break;
                }
        }
        catch
        {
            if (interaction.Type is InteractionType.ApplicationCommand)
                await interaction.GetOriginalResponseAsync().ContinueWith(async (msg) => await msg.Result.DeleteAsync());
        }
    }

    private Task HandleInteractionExecute(ICommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
            switch (result.Error)
            {
                case InteractionCommandError.UnmetPrecondition:
                    context.Interaction.RespondAsync($"Unmet Precondition: {result.ErrorReason}", ephemeral: true);
                    break;
                case InteractionCommandError.UnknownCommand:
                    //context.Interaction.RespondAsync("Unknown command", ephemeral: true);
                    break;
                case InteractionCommandError.BadArgs:
                    context.Interaction.RespondAsync("Invalid number or arguments", ephemeral: true);
                    break;
                case InteractionCommandError.Exception:
                    context.Interaction.RespondAsync($"Command exception: {result.ErrorReason}", ephemeral: true);
                    break;
                case InteractionCommandError.Unsuccessful:
                    context.Interaction.RespondAsync("Command could not be executed", ephemeral: true);
                    break;
                default:
                    break;
            }

        return Task.CompletedTask;
    }

    public async Task SelectMenuExecuted(SocketMessageComponent arg)
    {
        throw new NotImplementedException();
    }

    public async Task ButtonExecuted(SocketMessageComponent arg)
    {
        if (arg.Data.CustomId == "btn-container-count")
        {
            var msgEmbed = arg.Message.Embeds.First().ToEmbedBuilder();
            var countRed = msgEmbed.Fields.Single(x => x.Name.ToLower() == "red").Value;
            var countGreen = msgEmbed.Fields.Single(x => x.Name.ToLower() == "green").Value;
            var countBlue = msgEmbed.Fields.Single(x => x.Name.ToLower() == "blue").Value;
            var countDarkBlue = msgEmbed.Fields.Single(x => x.Name.ToLower() == "darkblue").Value;
            var countWhite = msgEmbed.Fields.Single(x => x.Name.ToLower() == "white").Value;

            var modal = new ModalBuilder()
                .WithTitle($"Update Container Count")
                .WithCustomId("update-count-modal")
                .AddTextInput("Red", $"update-count-red", value: countRed.ToString(), minLength: 1, maxLength: 2)
                .AddTextInput("Green", $"update-count-green", value: countGreen.ToString(), minLength: 1, maxLength: 2)
                .AddTextInput("Blue", $"update-count-blue", value: countBlue.ToString(), minLength: 1, maxLength: 2)
                .AddTextInput("Dark Blue", $"update-count-darkblue", value: countDarkBlue.ToString(), minLength: 1, maxLength: 2)
                .AddTextInput("White", $"update-count-white", value: countWhite.ToString(), minLength: 1, maxLength: 2);

            await arg.RespondWithModalAsync(modal.Build());
        }

        if (arg.Data.CustomId == "btn-board-edit")
        {
            if (arg.GuildId == null) 
            {
                await arg.RespondAsync("Must be used in a guild.", ephemeral: true);
                return;
            }

            var user = _client.GetGuild((ulong)arg.GuildId).GetUser(arg.User.Id);
            var channel = _client.GetGuild((ulong)arg.GuildId).Channels.Single(x => x.Id == arg.ChannelId);

            // User must have ManageMessages or be a Veteran to edit the Board.
            if (user.Roles.Any(x => x.Name != "Veteran"))
            {
                if (!user.GetPermissions(channel).ManageMessages)
                {
                    await arg.RespondAsync("Only Veterans and above may update the Job Board.", ephemeral: true);
                    return;
                }
            }

            var msgBoardEmbed = arg.Message.Embeds.First().ToEmbedBuilder();
            var boardValue = msgBoardEmbed.Fields.Single(x => x.Name == "Job Board").Value;

            var modalBoard = new ModalBuilder()
                .WithTitle($"Update job board")
                .WithCustomId("update-board-modal")
                .AddTextInput("Count", "update-board-edit", TextInputStyle.Paragraph, value: boardValue.ToString(), maxLength: 255, required: false);

            await arg.RespondWithModalAsync(modalBoard.Build());
        }
    }

    public async Task ModalSubmitted(SocketModal arg)
    {
        long seconds = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        var comp = arg.Data.Components.First();

        /// Count
        if (arg.Data.CustomId == "update-count-modal")
        {
            var red = arg.Data.Components.Single(x => x.CustomId == "update-count-red").Value;
            var green = arg.Data.Components.Single(x => x.CustomId == "update-count-green").Value;
            var blue = arg.Data.Components.Single(x => x.CustomId == "update-count-blue").Value;
            var darkblue = arg.Data.Components.Single(x => x.CustomId == "update-count-darkblue").Value;
            var white = arg.Data.Components.Single(x => x.CustomId == "update-count-white").Value;

            if (!Regex.IsMatch(red, @"^\d+$") ||
                !Regex.IsMatch(green, @"^\d+$") ||
                !Regex.IsMatch(blue, @"^\d+$") ||
                !Regex.IsMatch(darkblue, @"^\d+$") ||
                !Regex.IsMatch(white, @"^\d+$"))
            {
                await arg.RespondAsync("You must input a number!", ephemeral: true);
                return;
            }

            red = red == "0" ? "0" : red.TrimStart('0');
            green = green == "0" ? "0" : green.TrimStart('0');
            blue = blue == "0" ? "0" : blue.TrimStart('0');
            darkblue = darkblue == "0" ? "0" : darkblue.TrimStart('0');
            white = white == "0" ? "0" : white.TrimStart('0');


            var msgEmbed = arg.Message.Embeds.First().ToEmbedBuilder();
            msgEmbed.WithDescription("Last Updated by " + arg.User.Mention + " <t:" + seconds + ":R>");
            msgEmbed.Fields.Single(x => x.Name.ToLower() == "red").Value = red;
            msgEmbed.Fields.Single(x => x.Name.ToLower() == "green").Value = green;
            msgEmbed.Fields.Single(x => x.Name.ToLower() == "blue").Value = blue;
            msgEmbed.Fields.Single(x => x.Name.ToLower() == "darkblue").Value = darkblue;
            msgEmbed.Fields.Single(x => x.Name.ToLower() == "white").Value = white;

            await arg.UpdateAsync(x =>
            {
                x.Embed = msgEmbed.Build();
            });
        }

        /// Board
        if (arg.Data.CustomId == "update-board-modal")
        {
            var msg = await arg.Channel.GetMessageAsync(arg.Message.Id);

            var msgEmbed = msg.Embeds.First().ToEmbedBuilder();
            msgEmbed.WithDescription("Last Updated by " + arg.User.Mention + " <t:" + seconds + ":R>");
            msgEmbed.Fields.Single(x => x.Name == "Job Board").Value = (comp.Value == string.Empty ? "Waiting for Jobs ..." : comp.Value);

            await arg.UpdateAsync(x =>
            {
                x.Embed = msgEmbed.Build();
            });
        }

    }
}
