using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Spectre.Console.Cli;

namespace Pifeon.Cli.Commands;

public sealed class SendSettings : CommandSettings
{
    [CommandArgument(0, "<PATH>")]
    [Description("Path to the file or folder to send.")]
    public string Path { get; set; } = string.Empty;

    [CommandOption("-s|--server <URL>")]
    [Description("URL of the Pifeon.Server WebSocket endpoint.")]
    [DefaultValue("ws://localhost:5000/ws/pairing")]
    public string ServerUrl { get; set; } = "ws://localhost:5000/ws/pairing";
}
