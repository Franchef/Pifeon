using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling.Models;

namespace Pifeon.Core.Services;

internal sealed class PairingManager<TConnection> : IPairingManager<TConnection>
{
    private const int PairingCodeLength = 10;
    private const string PairingCodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private readonly ConcurrentDictionary<string, PairingSession<TConnection>> _sessions = new();
    private readonly TimeSpan _defaultTimeout;

    public PairingManager(TimeSpan? timeout = null)
    {
        _defaultTimeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public Task<string> CreateSessionAsync(TConnection senderConnection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        while (true)
        {
            string code = CreatePairingCode();
            var cts = new CancellationTokenSource(_defaultTimeout);
            var session = new PairingSession<TConnection>(
                code,
                senderConnection,
                new TaskCompletionSource<TConnection>(TaskCreationOptions.RunContinuationsAsynchronously),
                cts);
            if (!_sessions.TryAdd(code, session))
            {
                session.Dispose();
                continue;
            }

            cts.Token.Register(() => RemoveSession(code));
            return Task.FromResult(code);
        }
    }

    private static string CreatePairingCode()
    {
        Span<char> code = stackalloc char[PairingCodeLength];
        for (int index = 0; index < code.Length; index++)
        {
            code[index] = PairingCodeAlphabet[RandomNumberGenerator.GetInt32(PairingCodeAlphabet.Length)];
        }

        return new string(code);
    }

    public Task<bool> TryJoinSessionAsync(string code, TConnection receiverConnection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryClaimSession(code, out PairingSession<TConnection>? session) || session is null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(session.ReceiverConnected.TrySetResult(receiverConnection));
    }

    public bool TryGetSession(string code, out PairingSession<TConnection>? session)
    {
        return _sessions.TryGetValue(code, out session);
    }

    public bool TryClaimSession(string code, out PairingSession<TConnection>? session)
    {
        if (!_sessions.TryGetValue(code, out session) || !session.TryClaimReceiver())
        {
            session = null;
            return false;
        }

        return true;
    }

    public void RemoveSession(string code)
    {
        if (_sessions.TryRemove(code, out PairingSession<TConnection>? session))
        {
            session.Dispose();
        }
    }
}
