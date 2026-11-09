using System.Diagnostics.CodeAnalysis;
using Pifeon.Core;
using Pifeon.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class ReceiveCommand : AsyncCommand<ReceiveSettings>
{
    private const string PairingCodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

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

        // Interactive prompt if code is not provided in arguments
        if (string.IsNullOrWhiteSpace(code))
        {
            code = (await AnsiConsole.AskAsync<string>("Enter the [green]10-character pairing code[/]:", cancellationToken)).Trim();
        }

        code = code.ToUpperInvariant();
        if (code.Length != 10 || code.Any(character => !PairingCodeAlphabet.Contains(character)))
        {
            AnsiConsole.MarkupLine("[bold red]Error:[/] Code must contain exactly 10 valid letters or digits.");
            return 1;
        }

        AnsiConsole.MarkupLine("[bold blue]Pifeon Receiver[/] - Destination: [underline]{0}[/]", destFolder);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 1. Use the PifeonServer facade
        await using PifeonServer pifeonServer = new PifeonServer(customUrl: settings.ServerUrl);

        // 2. Preventive health check
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

        // 3. Connect to the session
        IReceiver receiver;
        try
        {
            receiver = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Verifying code [green]{code}[/] and connecting to peer...", async _ =>
                {
                    return await pifeonServer.JoinSessionAsync(code, cts.Token);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[bold red]Connection error or invalid code:[/] {0}", ex.Message);
            return 1;
        }

        await using (receiver)
        {
            AnsiConsole.MarkupLine("[bold green]✔ Connected to peer![/] Starting P2P data reception...");

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
                        ProgressTask totalTask = progressContext.AddTask(
                            "[green]Receiving total[/]",
                            maxValue: 1);
                        ProgressTask fileTask = progressContext.AddTask(
                            "[green]Waiting for transfer[/]",
                            maxValue: 1);

                        receiver.OnTransferProgressChanged += progress =>
                        {
                            totalTask.MaxValue = Math.Max(progress.TotalSizeBytes, 1);
                            totalTask.Value = progress.TotalSizeBytes == 0
                                ? 1
                                : progress.TotalBytesTransferred;
                            fileTask.MaxValue = Math.Max(progress.FileSizeBytes, 1);
                            fileTask.Value = progress.FileSizeBytes == 0
                                ? 1
                                : progress.FileBytesTransferred;
                            fileTask.Description = progress.TotalFiles == 0
                                ? "[green]No files to receive[/]"
                                : $"[green]Receiving file {progress.FileIndex}/{progress.TotalFiles}:[/] " +
                                  $"{Markup.Escape(progress.RelativePath)}";
                        };

                        // Start actual download to destination folder
                        await receiver.ReceiveToDirectoryAsync(destFolder, cts.Token);
                        totalTask.Value = totalTask.MaxValue;
                        fileTask.Value = fileTask.MaxValue;
                        totalTask.Description = "[green]Transfer complete[/]";
                        fileTask.Description = "[green]Transfer complete[/]";
                    });

                AnsiConsole.MarkupLine("\n[bold green]✔ Download completed successfully![/]");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("\n[yellow]Download canceled by user.[/]");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("\n[bold red]Error during P2P reception:[/] {0}", ex.Message);
                return 1;
            }
        }

        return 0;
    }
}
