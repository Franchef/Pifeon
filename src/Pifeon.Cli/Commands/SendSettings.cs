using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class SendSettings : CommandSettings
{
    [CommandArgument(0, "<PATH>")]
    [Description("Percorso del file o della cartella da inviare.")]
    public string Path { get; set; } = string.Empty;

    [CommandOption("-s|--server <URL>")]
    [Description("URL dell'endpoint WebSocket di Pifeon.Server.")]
    [DefaultValue("ws://localhost:5000/ws/pairing")]
    public string ServerUrl { get; set; } = "ws://localhost:5000/ws/pairing";
}
