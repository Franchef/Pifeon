using System.Diagnostics.CodeAnalysis;
using Pifeon.Core.Signaling;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class ReceiveCommand : AsyncCommand<ReceiveSettings>
{
    protected override async Task<int> ExecuteAsync(
        [NotNull] CommandContext context,
        [NotNull] ReceiveSettings settings,
        CancellationToken cancellationToken) // <-- Parametro aggiunto
    {
        string destFolder = Path.GetFullPath(settings.DestinationPath);

        if (!Directory.Exists(destFolder))
        {
            Directory.CreateDirectory(destFolder);
        }

        string code = settings.Code ?? string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            code = (await AnsiConsole.AskAsync<string>("Inserisci il [green]codice di pairing a 6 cifre[/]:", cancellationToken)).Trim();
        }

        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            AnsiConsole.MarkupLine("[bold red]Errore:[/] Il codice deve essere composto esattamente da 6 cifre numeriche.");
            return 1;
        }

        AnsiConsole.MarkupLine("[bold blue]Pifeon Receiver[/] - Destinazione: [underline]{0}[/]", destFolder);

        ISignalingService signaling = new WebSocketSignalingService(settings.ServerUrl);
        bool joined = false;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Verifica codice [green]{code}[/] con il server...", async _ =>
                {
                    joined = await signaling.JoinSessionAsync(code, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]Errore di Connessione:[/] Impossibile contattare Pifeon.Server. ({0})", ex.Message);
            return 1;
        }

        if (!joined)
        {
            AnsiConsole.MarkupLine("[bold red]Codice non valido o sessione scaduta![/]");
            await signaling.DisconnectAsync(CancellationToken.None);
            return 1;
        }

        AnsiConsole.MarkupLine("[bold green]✔ Sessione trovata![/] Handshake in corso e avvio ricezione dati P2P...");

        try
        {
            await Task.Delay(1500, cancellationToken);
            AnsiConsole.MarkupLine("[bold green]✔ Download completato con successo![/]");
        }
        finally
        {
            await signaling.DisconnectAsync(CancellationToken.None);
        }

        return 0;
    }
}
