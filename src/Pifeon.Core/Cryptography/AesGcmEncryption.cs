using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Pifeon.Core.Cryptography;

internal static class AesGcmEncryption
{
    public const int NonceSize = 12; // 96 bits per AES-GCM
    public const int TagSize = 16;   // 128 bits authentication tag

    public static int GetEncryptedSize(int plainTextLength) =>
        checked(NonceSize + TagSize + plainTextLength);

    public static int GetPlainTextSize(int encryptedPackageLength)
    {
        if (encryptedPackageLength < NonceSize + TagSize)
        {
            throw new ArgumentException("Dati cifrati non validi o troppo corti.", nameof(encryptedPackageLength));
        }

        return encryptedPackageLength - NonceSize - TagSize;
    }

    /// <summary>
    /// Cifra un buffer di byte utilizzando AES-256-GCM.
    /// Output formato: [Nonce (12B)][Tag (16B)][Ciphertext]
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plainText, ReadOnlySpan<byte> key)
    {
        byte[] result = new byte[GetEncryptedSize(plainText.Length)];
        Encrypt(plainText, key, result);
        return result;
    }

    public static void Encrypt(
        ReadOnlySpan<byte> plainText,
        ReadOnlySpan<byte> key,
        Span<byte> destination)
    {
        ValidateKey(key);
        if (destination.Length < GetEncryptedSize(plainText.Length))
        {
            throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        }

        Span<byte> nonce = destination[..NonceSize];
        Span<byte> tag = destination.Slice(NonceSize, TagSize);
        Span<byte> cipherText = destination.Slice(NonceSize + TagSize, plainText.Length);
        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Encrypt(nonce, plainText, cipherText, tag);
    }

    /// <summary>
    /// Decifra un pacchetto formato da [Nonce (12B)][Tag (16B)][Ciphertext] con AES-256-GCM.
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> encryptedPackage, ReadOnlySpan<byte> key)
    {
        if (encryptedPackage.Length < NonceSize + TagSize)
        {
            throw new ArgumentException("Dati cifrati non validi o troppo corti.", nameof(encryptedPackage));
        }

        byte[] plainText = new byte[GetPlainTextSize(encryptedPackage.Length)];
        Decrypt(encryptedPackage, key, plainText);
        return plainText;
    }

    public static int Decrypt(
        ReadOnlySpan<byte> encryptedPackage,
        ReadOnlySpan<byte> key,
        Span<byte> destination)
    {
        ValidateKey(key);
        int plainTextLength = GetPlainTextSize(encryptedPackage.Length);
        if (destination.Length < plainTextLength)
        {
            throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        }

        ReadOnlySpan<byte> nonce = encryptedPackage[..NonceSize];
        ReadOnlySpan<byte> tag = encryptedPackage.Slice(NonceSize, TagSize);
        ReadOnlySpan<byte> cipherText = encryptedPackage[(NonceSize + TagSize)..];

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Decrypt(nonce, cipherText, tag, destination[..plainTextLength]);
        return plainTextLength;
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("La chiave AES deve essere di 256 bit (32 byte).", nameof(key));
        }
    }
}
