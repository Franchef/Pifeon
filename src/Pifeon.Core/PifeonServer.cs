using System;
using System.Collections.Generic;
using System.Text;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Configuration;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling;

namespace Pifeon.Core;

public sealed class PifeonServer : IPifeonServer
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public string ServerUrl { get; private set; }

    public PifeonServer(string? customUrl = null, string? appSettingsUrl = null, HttpClient? httpClient = null)
    {
        ServerUrl = PifeonConfigurationResolver.ResolveSignalingUrl(customUrl, appSettingsUrl);
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<ISender> CreateSessionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 1. Creiamo il canale di trasporto concreto
        WebSocketTransportChannel transportChannel = await WebSocketTransportChannel.ConnectAsync(ServerUrl, ct);

        // 2. Iniettiamo il canale nel servizio di segnalazione
        var signalingService = new WebSocketSignalingService(transportChannel);

        // 3. Iniettiamo sia la segnalazione sia il canale di trasporto in PifeonSender
        var sender = new PifeonSender(signalingService, transportChannel);

        await sender.InitializeSessionAsync(ct);
        return sender;
    }

    public async Task<IReceiver> JoinSessionAsync(string code, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        WebSocketTransportChannel transportChannel = await WebSocketTransportChannel.ConnectAsync(ServerUrl, ct);
        var signalingService = new WebSocketSignalingService(transportChannel);

        var receiver = new PifeonReceiver(signalingService, transportChannel);
        await receiver.ConnectAndGetManifestAsync(code, ct);

        return receiver;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            // Se inizia con "wss" o "https" usiamo "https", altrimenti "http"
            bool isSecure = ServerUrl.StartsWith("wss", StringComparison.OrdinalIgnoreCase) ||
                            ServerUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase);

            var httpBuilder = new UriBuilder(ServerUrl)
            {
                Scheme = isSecure ? "https" : "http",
                Path = "/health"
            };

            using HttpResponseMessage response = await _httpClient.GetAsync(httpBuilder.Uri, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
            ServerUrl = string.Empty;
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
