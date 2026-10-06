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
            AnsiConsole.MarkupLine("[bold red]Error:[/] The specified path does not exist: [yellow]{0}[/]", targetPath);
            return 1;
        }

        AnsiConsole.MarkupLine("[bold blue]Pifeon Sender[/] - Preparing to send [underline]{0}[/]", targetPath);

        // 1. Scan the path using FolderScanner
        List<TransferItem> items;
        try
        {
            items = await FolderScanner.ScanPath(targetPath).ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]Error scanning the path:[/] {0}", ex.Message);
            return 1;
        }

        long totalBytes = items.Sum(i => i.FileSize);
        AnsiConsole.MarkupLine("[dim]Items found: {0} ({1} bytes)[/]\n", items.Count, totalBytes);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 2. Use the PifeonServer facade
        await using PifeonServer pifeonServer = new PifeonServer(customUrl: settings.ServerUrl);

        // 3. Preventive health check
        bool isServerHealthy = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Checking server availability...", async _ =>
            {
                return await pifeonServer.IsHealthyAsync(cts.Token);
            });

        if (!isServerHealthy)
        {
            AnsiConsole.MarkupLine("[bold red]Error:[/] Unable to reach Pifeon.Server at [yellow]{0}[/]", pifeonServer.ServerUrl);
            return 1;
        }

        // 4. Create session and generate code
        ISender sender;
        try
        {
            sender = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Connecting to Pifeon.Server and generating code...", async _ =>
                {
                    return await pifeonServer.CreateSessionAsync(cts.Token);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]Error during connection:[/] {0}", ex.Message);
            return 1;
        }

        await using (sender)
        {
            // 5. Display the 6-digit code
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(new Markup($"[bold green size=20]{sender.Code}[/]"))
            {
                Header = new PanelHeader(" Pairing Code "),
                Padding = new Padding(3, 1, 3, 1),
                Border = BoxBorder.Rounded
            });
            AnsiConsole.MarkupLine("[dim]Waiting for recipient to enter the code... (Press CTRL+C to cancel)[/]\n");

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
                        ProgressTask transferTask = progressContext.AddTask("[green]Sending P2P data[/]", maxValue: totalBytes);

                        sender.OnProgressChanged += (bytesSent, total, currentFile) =>
                        {
                            transferTask.Value = bytesSent;
                            if (!string.IsNullOrEmpty(currentFile))
                            {
                                transferTask.Description = $"[green]Sending:[/] {Path.GetFileName(currentFile)}";
                            }
                        };

                        await sender.SendAsync(targetPath, cts.Token);
                    });

                AnsiConsole.MarkupLine("\n[bold green]✔ Transfer completed successfully![/]");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("\n[yellow]Transfer canceled by user.[/]");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("\n[bold red]Error during P2P transfer:[/] {0}", ex.Message);
                return 1;
            }
        }

        return 0;
    }
}
