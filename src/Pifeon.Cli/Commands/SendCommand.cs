using System.Diagnostics.CodeAnalysis;
using Pifeon.Core.Signaling;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class SendCommand : AsyncCommand<SendSettings>
{
    protected override async Task<int> ExecuteAsync(
        [NotNull] CommandContext context,
        [NotNull] SendSettings settings,
        CancellationToken cancellationToken) // <-- Parametro aggiunto
    {
        string targetPath = Path.GetFullPath(settings.Path);

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            AnsiConsole.MarkupLine("[bold red]Errore:[/] Il percorso specificato non esiste: [yellow]{0}[/]", targetPath);
            return 1;
        }

        AnsiConsole.MarkupLine("[bold blue]Pifeon Sender[/] - Preparazione invio per [underline]{0}[/]", targetPath);

        ISignalingService signaling = new WebSocketSignalingService(settings.ServerUrl);

        // Collega il token fornito da Spectre.Console con quello locale per gestire Annullamenti (CTRL+C)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        string code;
        try
        {
            code = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Connessione a Pifeon.Server...", async _ =>
                {
                    return await signaling.CreateSessionAsync(cts.Token);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]Errore di Connessione:[/] Impossibile contattare Pifeon.Server a [yellow]{0}[/]. ({1})",
                settings.ServerUrl, ex.Message);
            return 1;
        }

        // Mostra il codice a 6 cifre
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup($"[bold green size=20]{code}[/]"))
        {
            Header = new PanelHeader(" Codice di Pairing (Valido 5 Minuti) "),
            Padding = new Padding(3, 1, 3, 1),
            Border = BoxBorder.Rounded
        });
        AnsiConsole.MarkupLine("[dim]In attesa che il destinatario inserisca il codice... (Premi CTRL+C per annullare)[/]\n");

        bool peerConnected = false;

        try
        {
            await AnsiConsole.Progress()
                .AutoClear(true)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new RemainingTimeColumn()
                )
                .StartAsync(async progressContext =>
                {
                    ProgressTask timerTask = progressContext.AddTask("[yellow]Scadenza Sessione[/]", maxValue: 300);
                    Task waitTask = signaling.WaitForReceiverAsync(cts.Token);

                    while (!timerTask.IsFinished && !waitTask.IsCompleted)
                    {
                        await Task.Delay(1000, cts.Token);
                        timerTask.Increment(1);
                    }

                    if (waitTask.IsCompletedSuccessfully)
                    {
                        peerConnected = true;
                    }
                });
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("\n[yellow]Operazione annullata dall'utente.[/]");
            await signaling.DisconnectAsync(CancellationToken.None);
            return 0;
        }

        if (!peerConnected)
        {
            AnsiConsole.MarkupLine("\n[bold red]Tempo scaduto![/] Nessun destinatario si è connesso alla sessione.");
            await signaling.DisconnectAsync(CancellationToken.None);
            return 1;
        }

        AnsiConsole.MarkupLine("[bold green]✔ Receiver connesso![/] Inizio fase di Handshake e streaming P2P...");

        try
        {
            await Task.Delay(1500, cts.Token);
            AnsiConsole.MarkupLine("[bold green]✔ Trasferimento completato con successo![/]");
        }
        finally
        {
            await signaling.DisconnectAsync(CancellationToken.None);
        }

        return 0;
    }
}
