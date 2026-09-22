using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Pifeon.Core.Cryptography;

public static class AesGcmEncryption
{
    public const int NonceSize = 12; // 96 bits per AES-GCM
    public const int TagSize = 16;   // 128 bits authentication tag

    /// <summary>
    /// Cifra un buffer di byte utilizzando AES-256-GCM.
    /// Output formato: [Nonce (12B)][Tag (16B)][Ciphertext]
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plainText, ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("La chiave AES deve essere di 256 bit (32 byte).", nameof(key));
        }

        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        byte[] tag = new byte[TagSize];
        byte[] cipherText = new byte[plainText.Length];

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Encrypt(nonce, plainText, cipherText, tag);

        // Prepara il buffer combinato: Nonce + Tag + Ciphertext
        byte[] result = new byte[NonceSize + TagSize + plainText.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipherText, 0, result, NonceSize + TagSize, cipherText.Length);

        return result;
    }

    /// <summary>
    /// Decifra un pacchetto formato da [Nonce (12B)][Tag (16B)][Ciphertext] con AES-256-GCM.
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> encryptedPackage, ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("La chiave AES deve essere di 256 bit (32 byte).", nameof(key));
        }

        if (encryptedPackage.Length < NonceSize + TagSize)
        {
            throw new ArgumentException("Dati cifrati non validi o troppo corti.", nameof(encryptedPackage));
        }

        ReadOnlySpan<byte> nonce = encryptedPackage[..NonceSize];
        ReadOnlySpan<byte> tag = encryptedPackage.Slice(NonceSize, TagSize);
        ReadOnlySpan<byte> cipherText = encryptedPackage[(NonceSize + TagSize)..];

        byte[] plainText = new byte[cipherText.Length];

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Decrypt(nonce, cipherText, tag, plainText);

        return plainText;
    }
}
