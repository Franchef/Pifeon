using System.ComponentModel;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class ReceiveSettings : CommandSettings
{
    [CommandArgument(0, "<DESTINATION>")]
    [Description("Cartella di destinazione per i file ricevuti.")]
    public string DestinationPath { get; set; } = string.Empty;

    [CommandArgument(1, "[CODE]")]
    [Description("Codice di pairing a 6 cifre fornito dal mittente.")]
    public string? Code { get; set; }

    [CommandOption("-s|--server <URL>")]
    [Description("URL dell'endpoint WebSocket di Pifeon.Server.")]
    [DefaultValue("ws://localhost:5000/ws/pairing")]
    public string ServerUrl { get; set; } = "ws://localhost:5000/ws/pairing";
}
