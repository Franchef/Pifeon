using System.Diagnostics.CodeAnalysis;
using Pifeon.Cli.Commands;
using Spectre.Console.Cli;

#pragma warning disable IL3050
CommandApp app = new();
#pragma warning restore IL3050

app.Configure(config =>
{
    config.SetApplicationName("pifeon");

    // Registrazione esplicita AOT-safe dei comandi
    config.AddCommand<SendCommand>("send")
          .WithDescription("Invia un file o una cartella a un peer remoto aprendo una sessione di pairing.");

    config.AddCommand<ReceiveCommand>("receive")
          .WithDescription("Riceve file da un peer tramite il codice di pairing a 6 cifre.");
});

return await app.RunAsync(args);
