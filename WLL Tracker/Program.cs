using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace WLL_Tracker;

public class Program
{
    private static IConfiguration _configuration;
    private static IServiceProvider _services;

    private static readonly DiscordSocketConfig _socketConfig = new()
    {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
    };

    private static readonly InteractionServiceConfig _interactionServiceConfig = new()
    {
        //
    };

    public static async Task Main()
    {
        _configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables(prefix: "WLL_")
            .Build();

        _services = new ServiceCollection()
            .AddSingleton(_configuration)
            .AddSingleton(_socketConfig)
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>(), _interactionServiceConfig))
            .AddSingleton<InteractionHandler>()
            .BuildServiceProvider();

        var client = _services.GetRequiredService<DiscordSocketClient>();

        client.Log += LogAsync;

        await _services.GetRequiredService<InteractionHandler>().InitializeAsync();

        await client.LoginAsync(TokenType.Bot, _configuration["token"]);
        await client.StartAsync();

        await Task.Delay(Timeout.Infinite);

    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }


}