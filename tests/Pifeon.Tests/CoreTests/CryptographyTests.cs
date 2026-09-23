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
}
