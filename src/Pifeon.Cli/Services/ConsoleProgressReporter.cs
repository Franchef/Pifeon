using System;
using System.Collections.Generic;
using System.Text;
using Spectre.Console;

namespace Pifeon.Cli.Services;

public static class ConsoleProgressReporter
{
    public static async Task StreamWithProgressAsync(string fileName, long totalBytes, Func<IProgress<long>, Task> downloadTask)
    {
        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new DownloadedColumn(),
                new TransferSpeedColumn(),
                new RemainingTimeColumn()
            )
            .StartAsync(async ctx =>
            {
                ProgressTask progressTask = ctx.AddTask($"[green]{fileName}[/]", maxValue: totalBytes);
                Progress<long> progress = new Progress<long>(bytesRead => progressTask.Value = bytesRead);

                await downloadTask(progress);
            });
    }
}
