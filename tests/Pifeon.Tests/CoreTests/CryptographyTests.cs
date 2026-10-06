using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Pifeon.Core.Cryptography;

namespace Pifeon.Tests.CoreTests;

public class CryptographyTests
{
    [Fact]
    public void KeyExchange_ShouldDeriveSameSharedSecretOnBothPeers()
    {
        // Arrange
        using var alice = new KeyExchange();
        using var bob = new KeyExchange();

        byte[] alicePublicKey = alice.GetPublicKey();
        byte[] bobPublicKey = bob.GetPublicKey();

        // Act
        byte[] aliceSecret = alice.DeriveSharedSecret(bobPublicKey);
        byte[] bobSecret = bob.DeriveSharedSecret(alicePublicKey);

        // Assert
        Assert.Equal(32, aliceSecret.Length); // 256-bit
        Assert.Equal(aliceSecret, bobSecret);
    }

    [Fact]
    public void AesGcmEncryption_EncryptAndDecrypt_ShouldReturnOriginalData()
    {
        // Arrange
        byte[] key = RandomNumberGenerator.GetBytes(32); // 256-bit key
        byte[] originalData = "Messaggio segreto Pifeon P2P"u8.ToArray();

        // Act
        byte[] encryptedPackage = AesGcmEncryption.Encrypt(originalData, key);
        byte[] decryptedData = AesGcmEncryption.Decrypt(encryptedPackage, key);

        // Assert
        Assert.NotEqual(originalData, encryptedPackage);
        Assert.Equal(originalData, decryptedData);
    }
    [Fact]
    public void AesGcmEncryption_DecryptWithTamperedData_ShouldThrowCryptographicException()
    {
        // Arrange
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] originalData = "Dati integri"u8.ToArray();
        byte[] encryptedPackage = AesGcmEncryption.Encrypt(originalData, key);

        // Act: Corrompiamo un byte nel ciphertext per simulare una manomissione
        encryptedPackage[^1] ^= 0xFF;

        // Assert: Verifichiamo l'eccezione esatta lanciata da AesGcm
        Assert.Throws<AuthenticationTagMismatchException>(() => AesGcmEncryption.Decrypt(encryptedPackage, key));
    }

    [Fact]
    public void AesGcmEncryption_DecryptWithWrongKey_ShouldThrowCryptographicException()
    {
        // Arrange
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] wrongKey = RandomNumberGenerator.GetBytes(32);
        byte[] originalData = "Dati integri"u8.ToArray();
        byte[] encryptedPackage = AesGcmEncryption.Encrypt(originalData, key);
        // Assert: Verifichiamo l'eccezione esatta lanciata da AesGcm
        Assert.Throws<AuthenticationTagMismatchException>(() => AesGcmEncryption.Decrypt(encryptedPackage, wrongKey));
    }

    [Fact]
    public void Encrypt_ShouldThrowArgumentException_WhenKeyLengthIsNot32Bytes()
    {
        // Arrange
        byte[] invalidKey = new byte[16]; // 128-bit instead of 256-bit
        byte[] plainText = "Test Payload"u8.ToArray();

        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            AesGcmEncryption.Encrypt(plainText, invalidKey));

        Assert.Equal("key", exception.ParamName);
        Assert.Contains("La chiave AES deve essere di 256 bit", exception.Message);
    }

    [Fact]
    public void Decrypt_ShouldThrowArgumentException_WhenKeyLengthIsNot32Bytes()
    {
        // Arrange
        byte[] invalidKey = new byte[24]; // 192-bit instead of 256-bit
        byte[] dummyEncryptedPackage = new byte[30]; // Pacchetto valido per lunghezza

        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            AesGcmEncryption.Decrypt(dummyEncryptedPackage, invalidKey));

        Assert.Equal("key", exception.ParamName);
        Assert.Contains("La chiave AES deve essere di 256 bit", exception.Message);
    }

    [Fact]
    public void Decrypt_ShouldThrowArgumentException_WhenEncryptedPackageIsTooShort()
    {
        // Arrange
        byte[] validKey = new byte[32]; // Chiave corretta da 32 byte
        byte[] tooShortPackage = new byte[27]; // Minimo richiesto: Nonce (12) + Tag (16) = 28 byte

        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            AesGcmEncryption.Decrypt(tooShortPackage, validKey));

        Assert.Equal("encryptedPackage", exception.ParamName);
        Assert.Contains("Dati cifrati non validi o troppo corti.", exception.Message);
    }

}
