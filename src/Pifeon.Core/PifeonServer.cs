using System;
using System.Collections.Generic;
using System.Text;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Configuration;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling;

namespace Pifeon.Core;

public class PifeonServer : IPifeonServer
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public string ServerUrl { get; set; }

    /// <summary>
    /// Inizializza il server applicando la gerarchia delle configurazioni.
    /// </summary>
    public PifeonServer(string? customUrl = null, string? appSettingsUrl = null)
    {
        ServerUrl = PifeonConfigurationResolver.ResolveSignalingUrl(customUrl, appSettingsUrl);
    }

    /// <summary>
    /// Esegue un ping/health-check verso l'endpoint del server.
    /// Convertiamo temporaneamente 'ws://' / 'wss://' in 'http://' / 'https://' per chiamare /health.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var httpBuilder = new UriBuilder(ServerUrl);
            httpBuilder.Scheme = httpBuilder.Scheme switch
            {
                "wss" => "https",
                "ws" => "http",
                _ => httpBuilder.Scheme
            };

            // Assumendo che il server di segnalazione esponga l'endpoint /health
            httpBuilder.Path = "/health";

            using var response = await HttpClient.GetAsync(httpBuilder.Uri, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ISender> CreateSessionAsync(CancellationToken ct = default)
    {
        var signalingService = new WebSocketSignalingService(ServerUrl);
        var sender = new PifeonSender(signalingService);

        await sender.InitializeSessionAsync(ct);
        return sender;
    }

    public async Task<IReceiver> JoinSessionAsync(string code, CancellationToken ct = default)
    {
        var signalingService = new WebSocketSignalingService(ServerUrl);
        var receiver = new PifeonReceiver(signalingService);

        await receiver.ConnectAndGetManifestAsync(code, ct);
        return receiver;
    }
}
