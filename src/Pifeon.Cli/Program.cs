using System.Diagnostics.CodeAnalysis;
using Pifeon.Cli.Commands;
using Spectre.Console.Cli;

#pragma warning disable IL3050
CommandApp app = new();
#pragma warning restore IL3050

app.Configure(config =>
{
    config.SetApplicationName("pifeon");

    // Explicit AOT-safe command registration
    config.AddCommand<SendCommand>("send")
          .WithDescription("Sends a file or folder to a remote peer by opening a pairing session.");

    config.AddCommand<ReceiveCommand>("receive")
          .WithDescription("Receives files from a peer using the 6-digit pairing code.");
});

return await app.RunAsync(args);
