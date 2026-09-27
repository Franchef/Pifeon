using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Signaling;

public sealed class WebSocketSignalingService : ISignalingService
{
    private readonly ITransportChannel _channel;
    private readonly TaskCompletionSource<bool> _receiverJoinedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private bool _disposed;

    public event Action? OnReceiverJoined;
    public event Action<string>? OnSignalDataReceived;

    public WebSocketSignalingService(ITransportChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Se l'ascolto non è ancora partito, avviamo il loop di ricezione in background
        if (_listenTask == null)
        {
            _listenCts = new CancellationTokenSource();
            _listenTask = StartListeningLoopAsync(_listenCts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task<string> CreateSessionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await ConnectAsync(ct);

        // Invio richiesta di creazione sessione
        byte[] payload = Encoding.UTF8.GetBytes("{\"action\":\"CREATE\"}");
        await _channel.SendAsync(payload, ct);

        // In uno scenario reale, il codice a 6 cifre viene letto dal ciclo di ascolto o da una risposta attesa
        // Qui simuleriamo il ritorno del codice generato dal server di segnalazione
        return "123456";
    }

    public async Task JoinSessionAsync(string code, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await ConnectAsync(ct);

        string json = $"{{\"action\":\"JOIN\",\"code\":\"{code}\"}}";
        byte[] payload = Encoding.UTF8.GetBytes(json);
        await _channel.SendAsync(payload, ct);
    }

    public async Task WaitForReceiverAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Attende che il flag RECEIVER_JOINED sia impostato dal ciclo di lettura
        using CancellationTokenRegistration reg = ct.Register(() => _receiverJoinedTcs.TrySetCanceled(ct));
        await _receiverJoinedTcs.Task;
    }

    public async Task SendSignalDataAsync(string payload, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string json = $"{{\"action\":\"SIGNAL\",\"data\":\"{payload}\"}}";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await _channel.SendAsync(bytes, ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_listenCts != null)
        {
            await _listenCts.CancelAsync();
            _listenCts.Dispose();
            _listenCts = null;
        }

        await _channel.CloseAsync(ct);
    }

    private async Task StartListeningLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested && _channel.IsConnected)
            {
                int bytesRead = await _channel.ReceiveAsync(buffer, ct);
                if (bytesRead == 0)
                {
                    break; // Canale chiuso
                }

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                ProcessIncomingMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Chiusura normale del loop di ascolto
        }
        catch (Exception)
        {
            // Gestione o logging disconnessione
        }
    }

    private void ProcessIncomingMessage(string jsonMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonMessage);
            if (doc.RootElement.TryGetProperty("type", out JsonElement typeProp))
            {
                string? messageType = typeProp.GetString();

                switch (messageType)
                {
                    case "RECEIVER_JOINED":
                        _receiverJoinedTcs.TrySetResult(true);
                        OnReceiverJoined?.Invoke();
                        break;

                    case "SIGNAL_DATA":
                        if (doc.RootElement.TryGetProperty("payload", out JsonElement payloadProp))
                        {
                            OnSignalDataReceived?.Invoke(payloadProp.GetString() ?? string.Empty);
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Gestione eventuale formato non valido
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;

            await DisconnectAsync(CancellationToken.None);
            await _channel.DisposeAsync();

            GC.SuppressFinalize(this);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
