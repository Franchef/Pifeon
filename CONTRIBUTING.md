# Contributing to Pifeon

Thank you for considering a contribution. Issues and pull requests are welcome.
For larger changes, open an issue first to discuss the approach.

## Development setup

Pifeon targets .NET 11. Install the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0).

On Windows, `scripts/setup/windows_dev_env.ps1` can install the .NET SDK,
Docker Desktop, and the .NET Aspire workload through WinGet. Run it from an
elevated PowerShell session. The script currently installs the .NET 11 SDK.

There is no Linux setup script at this time. Install the .NET 11 SDK using the
instructions for your Linux distribution. The AppHost uses .NET Aspire; follow
the Aspire setup instructions if you want to run the AppHost locally. The
solution can be restored, built, and tested with the commands below.

From the repository root:

```shell
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
```

`Pifeon.IntegrationTests` uses `WebApplicationFactory` for in-process server
tests. `Pifeon.AspireTests` starts the real AppHost with `Aspire.Hosting.Testing`
and covers invalid receiver session codes and complete sender-to-receiver
folder transfers. Transfers use isolated temporary `Sender` and `Receiver`
directories and include a single-file case and a random 2-12 file case from
`Sender/FolderToSend` to `Receiver/DestinationFolder`, with distinct file sizes.
Tests verify all downloaded filenames, sizes, and contents and clean up
afterward. These tests require the local Aspire AppHost prerequisites, but no
Docker containers or external services.

Run just the Aspire tests with:

```shell
dotnet test --project tests/Pifeon.AspireTests/Pifeon.AspireTests.csproj
```

CI collects unit and integration coverage separately and uses the pinned
`dotnet-coverage` tool in `.config/dotnet-tools.json` to merge hits for shared code.
Run `dotnet tool restore` to make this tool available locally. The coverage gate
uses the merged report with an 80% minimum line coverage and a 90% green indicator.
Branch coverage and complexity are hidden because the merged report does not
preserve reliable values for those metrics. Existing assembly coverage exclusions
are unchanged.

To see the CLI commands:

```shell
dotnet run --project src/Pifeon.Cli -- --help
```

For CLI usage, see [docs/CLI.md](docs/CLI.md). For protocol and behavior
context, see [docs/FLOWS.md](docs/FLOWS.md).

## Pull requests

- Keep changes focused and explain the problem they solve.
- Add or update tests for behavior changes.
- Update documentation when commands, setup, or user-visible behavior changes.
- Run the build and tests above before submitting, and mention any checks you
  could not run.
- Do not include credentials, private keys, personal data, or generated build
  and test output in issues or pull requests.

By submitting a contribution, you agree that it may be distributed under the
project's [GPL-3.0 license](LICENSE).
