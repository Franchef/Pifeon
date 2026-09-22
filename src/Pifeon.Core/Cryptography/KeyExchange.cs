using System.Security.Cryptography;

namespace Pifeon.Core.Cryptography;

public sealed class KeyExchange : IDisposable
{
    private readonly ECDiffieHellman _ecdh;

    public KeyExchange()
    {
        // Istanziazione cross-platform con la curva NIST P-256 (compatibile con Windows, Linux, macOS)
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    }

    /// <summary>
    /// Esporta la chiave pubblica locale da inviare al peer remoto via server di segnalazione.
    /// </summary>
    public byte[] GetPublicKey()
    {
        return _ecdh.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// Calcola la chiave simmetrica condivisa a 256-bit tramite SHA-256 combinando la propria 
    /// chiave privata con la chiave pubblica del peer.
    /// </summary>
    public byte[] DeriveSharedSecret(byte[] peerPublicKey)
    {
        using var otherKey = ECDiffieHellman.Create();
        otherKey.ImportSubjectPublicKeyInfo(peerPublicKey, out _);

        return _ecdh.DeriveKeyFromHash(otherKey.PublicKey, HashAlgorithmName.SHA256);
    }

    public void Dispose()
    {
        _ecdh.Dispose();
    }
}
