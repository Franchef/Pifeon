using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling.Models;

namespace Pifeon.Core.Services;

public class PairingManager<TConnection> : IPairingManager<TConnection>
{
    private readonly ConcurrentDictionary<string, PairingSession<TConnection>> _sessions = new();
    private readonly TimeSpan _defaultTimeout;

    public PairingManager(TimeSpan? timeout = null)
    {
        _defaultTimeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public Task<string> CreateSessionAsync(TConnection senderConnection, CancellationToken ct = default)
    {
        string code;
        var cts = new CancellationTokenSource(_defaultTimeout);

        // Generazione sicura del codice a 6 cifre
        do
        {
            code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }
        while (_sessions.ContainsKey(code));

        var session = new PairingSession<TConnection>(
            code,
            senderConnection,
            new TaskCompletionSource<TConnection>(TaskCreationOptions.RunContinuationsAsynchronously),
            cts
        );

        while (!_sessions.TryAdd(code, session))
        {
            code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            session = new PairingSession<TConnection>(
                code,
                senderConnection,
                new TaskCompletionSource<TConnection>(TaskCreationOptions.RunContinuationsAsynchronously),
                cts
            );
        }

        // Auto-pulizia allo scadere del timeout
        cts.Token.Register(() => RemoveSession(code));

        return Task.FromResult(code);
    }

    public async Task<bool> TryJoinSessionAsync(string code, TConnection receiverConnection, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(code, out PairingSession<TConnection>? session))
        {
            return false;
        }

        // Notifica il completamento dell'accoppiamento al sender
        bool setSucceeded = session.ReceiverConnected.TrySetResult(receiverConnection);
        return await Task.FromResult(setSucceeded);
    }

    public bool TryGetSession(string code, out PairingSession<TConnection>? session)
    {
        return _sessions.TryGetValue(code, out session);
    }

    public void RemoveSession(string code)
    {
        if (_sessions.TryRemove(code, out PairingSession<TConnection>? session))
        {
            session.Dispose();
        }
    }
}
