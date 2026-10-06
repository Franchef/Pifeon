using System.ComponentModel;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class ReceiveSettings : CommandSettings
{
    [CommandArgument(0, "<DESTINATION>")]
    [Description("Destination folder for received files.")]
    public string DestinationPath { get; set; } = string.Empty;

    [CommandArgument(1, "[CODE]")]
    [Description("6-digit pairing code provided by the sender.")]
    public string? Code { get; set; }

    [CommandOption("-s|--server <URL>")]
    [Description("URL of the Pifeon.Server WebSocket endpoint.")]
    [DefaultValue("ws://localhost:5000/ws/pairing")]
    public string ServerUrl { get; set; } = "ws://localhost:5000/ws/pairing";
}
