using System.Diagnostics.CodeAnalysis;
using Pifeon.Core;
using Pifeon.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class ReceiveCommand : AsyncCommand<ReceiveSettings>
{
    public override async Task<int> ExecuteAsync(
        [NotNull] CommandContext context,
        [NotNull] ReceiveSettings settings,
        CancellationToken cancellationToken)
    {
        string destFolder = Path.GetFullPath(settings.DestinationPath);

        if (!Directory.Exists(destFolder))
        {
            Directory.CreateDirectory(destFolder);
        }

        string code = settings.Code ?? string.Empty;

        // Prompt interattivo se il codice non è fornito negli argomenti
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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 1. Utilizzo della Facade PifeonServer
        await using PifeonServer pifeonServer = new PifeonServer(customUrl: settings.ServerUrl);

        // 2. Health Check preventivo
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

        // 3. Connessione alla sessione
        IReceiver receiver;
        try
        {
            receiver = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Verifica codice [green]{code}[/] e connessione al peer...", async _ =>
                {
                    return await pifeonServer.JoinSessionAsync(code, cts.Token);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]Errore di connessione o codice non valido:[/] {0}", ex.Message);
            return 1;
        }

        await using (receiver)
        {
            AnsiConsole.MarkupLine("[bold green]✔ Connesso al peer![/] Avvio ricezione dati P2P...");

            // 4. Download P2P con Progress Bar
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
                        ProgressTask downloadTask = progressContext.AddTask("[green]Download P2P[/]", maxValue: 100);

                        // Tracciamento avanzamento tramite l'evento del Receiver
                        receiver.OnProgressChanged += (bytesReceived, totalBytes, currentFile) =>
                        {
                            if (totalBytes > 0)
                            {
                                downloadTask.MaxValue = totalBytes;
                            }

                            downloadTask.Value = bytesReceived;

                            if (!string.IsNullOrEmpty(currentFile))
                            {
                                downloadTask.Description = $"[green]Ricezione:[/] {Path.GetFileName(currentFile)}";
                            }
                        };

                        // Avvio effettivo del download verso la cartella di destinazione
                        await receiver.ReceiveToDirectoryAsync(destFolder, cts.Token);
                    });

                AnsiConsole.MarkupLine("\n[bold green]✔ Download completato con successo![/]");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("\n[yellow]Download interrotto dall'utente.[/]");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("\n[bold red]Errore durante la ricezione P2P:[/] {0}", ex.Message);
                return 1;
            }
        }

        return 0;
    }
}
//                                downloadTask.MaxValue = totalBytes;
//                            }

//                            downloadTask.Value = bytesReceived;

//                            if (!string.IsNullOrEmpty(currentFile))
//                            {
//                                downloadTask.Description = $"[green]Ricezione:[/] {Path.GetFileName(currentFile)}";
//                            }
//                        };

//                        // Avvio effettivo del download verso la cartella di destinazione
//                        await receiver.ReceiveToDirectoryAsync(destFolder, cts.Token);
//                    });

//                AnsiConsole.MarkupLine("\n[bold green]✔ Download completato con successo![/]");
//            }
//            catch (OperationCanceledException)
//            {
//                AnsiConsole.MarkupLine("\n[yellow]Download interrotto dall'utente.[/]");
//                return 0;
//            }
//            catch (Exception ex)
//            {
//                AnsiConsole.MarkupLine("\n[bold red]Errore durante la ricezione P2P:[/] {0}", ex.Message);
//                return 1;
//            }
//        }

//        return 0;
//    }
//}
