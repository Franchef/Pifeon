# Contributing to Pifeon

Thank you for considering a contribution. Issues and pull requests are welcome.
For larger changes, open an issue first to discuss the approach.

## Development setup

Pifeon targets .NET 10. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

On Windows, `scripts/setup/windows_dev_env.ps1` can install the .NET SDK,
Docker Desktop, and the .NET Aspire workload through WinGet. Run it from an
elevated PowerShell session. The script currently installs the .NET 10 SDK.

There is no Linux setup script at this time. Install the .NET 10 SDK using the
instructions for your Linux distribution. The AppHost uses .NET Aspire; follow
the Aspire setup instructions if you want to run the AppHost locally. The
solution can be restored, built, and tested with the commands below.

From the repository root:

```shell
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
```

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
