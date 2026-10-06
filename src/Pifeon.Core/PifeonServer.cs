using System;
using System.Net.WebSockets;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Configuration;
using Pifeon.Core.Exceptions;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;

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

    public async Task<ISenderSession> CreateSessionHandleAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            WebSocketTransportChannel transportChannel = await WebSocketTransportChannel.ConnectAsync(BuildSessionUri("/ws/session/create").ToString(), ct);
            var serverChannel = new ServerMessageChannel(transportChannel);
            var peerChannel = new DirectPeerMessageChannel();

            try
            {
                return await PifeonSession.CreateSenderAsync(serverChannel, peerChannel, ct);
            }
            catch
            {
                await transportChannel.DisposeAsync();
                throw;
            }
        }
        catch (WebSocketException ex) when (IsStatusInMessage(ex, "429"))
        {
            throw new CreateSessionRateLimitedException(ex.Message);
        }
    }

    public async Task<IReceiverSession> JoinSessionHandleAsync(string code, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Session code is required.", nameof(code));
        }

        try
        {
            string normalizedCode = code.Trim();
            WebSocketTransportChannel transportChannel = await WebSocketTransportChannel.ConnectAsync(BuildSessionUri($"/ws/session/join/{normalizedCode}").ToString(), ct);
            var serverChannel = new ServerMessageChannel(transportChannel);
            var peerChannel = new DirectPeerMessageChannel();

            try
            {
                return await PifeonSession.CreateReceiverAsync(normalizedCode, serverChannel, peerChannel, ct);
            }
            catch
            {
                await transportChannel.DisposeAsync();
                throw;
            }
        }
        catch (WebSocketException ex) when (IsStatusInMessage(ex, "404"))
        {
            throw new SessionNotFoundException(code);
        }
    }

    public async Task<ISender> CreateSessionAsync(CancellationToken ct = default)
    {
        ISenderSession session = await CreateSessionHandleAsync(ct);
        return await session.GetSenderAsync(ct);
    }

    public async Task<IReceiver> JoinSessionAsync(string code, CancellationToken ct = default)
    {
        IReceiverSession session = await JoinSessionHandleAsync(code, ct);
        try
        {
            return await session.GetReceiverAsync(ct);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private Uri BuildSessionUri(string sessionPath)
    {
        Uri baseUri = new(ServerUrl, UriKind.Absolute);
        if (baseUri.Scheme != "wss" && !(baseUri.Scheme == "ws" && baseUri.IsLoopback))
        {
            throw new InvalidOperationException("Trusted peer key exchange requires WSS. WS is allowed only for loopback development.");
        }

        return new UriBuilder(baseUri)
        {
            Path = sessionPath,
            Query = string.Empty
        }.Uri;
    }

    private static bool IsStatusInMessage(Exception ex, string statusCode)
    {
        return ex.Message.Contains(statusCode, StringComparison.OrdinalIgnoreCase);
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
