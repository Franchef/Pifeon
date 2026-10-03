using System.Diagnostics.CodeAnalysis;
using Pifeon.Core;
using Pifeon.Core.Abstractions;
using Pifeon.Core.IO;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class SendCommand : AsyncCommand<SendSettings>
{
    public override async Task<int> ExecuteAsync(
        [NotNull] CommandContext context,
        [NotNull] SendSettings settings,
        CancellationToken cancellationToken)
    {
        string targetPath = Path.GetFullPath(settings.Path);

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            AnsiConsole.MarkupLine("[bold red]Errore:[/] Il percorso specificato non esiste: [yellow]{0}[/]", targetPath);
            return 1;
        }

        AnsiConsole.MarkupLine("[bold blue]Pifeon Sender[/] - Preparazione invio per [underline]{0}[/]", targetPath);

        // 1. Scansione del percorso tramite FolderScanner
        List<TransferItem> items;
        try
        {
            items = await FolderScanner.ScanPath(targetPath).ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]Errore durante la scansione del percorso:[/] {0}", ex.Message);
            return 1;
        }

        long totalBytes = items.Sum(i => i.FileSize);
        AnsiConsole.MarkupLine("[dim]Elementi trovati: {0} ({1} bytes)[/]\n", items.Count, totalBytes);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 2. Utilizzo della Facade PifeonServer
        await using PifeonServer pifeonServer = new PifeonServer(customUrl: settings.ServerUrl);

        // 3. Health Check preventivo
        bool isServerHealthy = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Verifica disponibilità del server...", async _ =>
            {
                return await pifeonServer.IsHealthyAsync(cts.Token);
            });

        if (!isServerHealthy)
        {
            AnsiConsole.MarkupLine("[bold red]Errore:[/] Impossibile raggiungere Pifeon.Server su [yellow]{0}[/]", pifeonServer.ServerUrl);
            return 1;
        }

        // 4. Creazione della sessione e generazione del codice
        ISender sender;
        try
        {
            sender = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Connessione a Pifeon.Server e generazione codice...", async _ =>
                {
                    return await pifeonServer.CreateSessionAsync(cts.Token);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]Errore durante la connessione:[/] {0}", ex.Message);
            return 1;
        }

        await using (sender)
        {
            // 5. Visualizzazione del Codice a 6 cifre
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(new Markup($"[bold green size=20]{sender.Code}[/]"))
            {
                Header = new PanelHeader(" Codice di Pairing "),
                Padding = new Padding(3, 1, 3, 1),
                Border = BoxBorder.Rounded
            });
            AnsiConsole.MarkupLine("[dim]In attesa che il destinatario inserisca il codice... (Premi CTRL+C per annullare)[/]\n");

            // 6. Streaming dei dati con avanzamento in tempo reale
            try
            {
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new DownloadedColumn(),
                        new TransferSpeedColumn(),
                        new RemainingTimeColumn()
                    )
                    .StartAsync(async progressContext =>
                    {
                        ProgressTask transferTask = progressContext.AddTask("[green]Invio dati P2P[/]", maxValue: totalBytes);

                        sender.OnProgressChanged += (bytesSent, total, currentFile) =>
                        {
                            transferTask.Value = bytesSent;
                            if (!string.IsNullOrEmpty(currentFile))
                            {
                                transferTask.Description = $"[green]Invio:[/] {Path.GetFileName(currentFile)}";
                            }
                        };

                        await sender.SendAsync(targetPath, cts.Token);
                    });

                AnsiConsole.MarkupLine("\n[bold green]✔ Trasferimento completato con successo![/]");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("\n[yellow]Trasferimento interrotto dall'utente.[/]");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("\n[bold red]Errore durante il trasferimento P2P:[/] {0}", ex.Message);
                return 1;
            }
        }

        return 0;
    }
}
