using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace Pifeon.Server;

public class PairingManager<TConnection>
{
    private readonly ConcurrentDictionary<string, PairingSession<TConnection>> _sessions = new();
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Crea una nuova sessione garantendo l'univocità del codice a 6 cifre.
    /// </summary>
    public string CreateSession(TConnection senderConnection)
    {
        string code;
        var cts = new CancellationTokenSource(_defaultTimeout);

        // Ciclo di sicurezza: continua a generare finché non ne trova uno libero
        do
        {
            code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }
        while (_sessions.ContainsKey(code));

        var session = new PairingSession<TConnection>(code, senderConnection, new TaskCompletionSource<TConnection>(), cts);

        // Se il tentativo di inserimento fallisce (race condition estremamente rara), ritenta
        while (!_sessions.TryAdd(code, session))
        {
            code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            session = new PairingSession<TConnection>(code, senderConnection, new TaskCompletionSource<TConnection>(), cts);
        }

        // Timer di auto-pulizia
        cts.Token.Register(() => RemoveSession(code));

        return code;
    }

    public bool TryGetSession(string code, out PairingSession<TConnection>? session)
    {
        return _sessions.TryGetValue(code, out session);
    }

    public void RemoveSession(string code)
    {
        if (_sessions.TryRemove(code, out PairingSession<TConnection>? session))
        {
            session.TimeoutCts.Dispose();
        }
    }
}
