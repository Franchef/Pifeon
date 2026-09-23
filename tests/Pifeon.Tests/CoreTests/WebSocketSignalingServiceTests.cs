using System.Text.Json;
using Pifeon.Core.Signaling;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public class WebSocketSignalingServiceTests
{
    [Fact]
    public void Constructor_WithValidUrl_ShouldInitializeService()
    {
        // Arrange & Act
        using var service = new WebSocketSignalingService("ws://localhost:5000/ws/pairing");

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task SendSignalDataAsync_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var service = new WebSocketSignalingService("ws://localhost:5000/ws/pairing");

        // Act & Assert: Invio prima di connettersi o creare una sessione
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendSignalDataAsync("sample_sdp_payload"));
    }

    [Fact]
    public async Task WaitForReceiverAsync_WhenCancelled_ShouldThrowTaskCanceledException()
    {
        // Arrange
        using var service = new WebSocketSignalingService("ws://localhost:5000/ws/pairing");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Annulliamo subito il token

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            service.WaitForReceiverAsync(cts.Token));
    }

    [Fact]
    public void SignalingJsonContext_ShouldSerializeAndDeserializeRequestsCorrectly()
    {
        // Arrange
        JoinSessionRequest request = new JoinSessionRequest("123456", "JOIN");

        // Act
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(
            request,
            SignalingJsonContext.Default.JoinSessionRequest);

        JoinSessionRequest? deserialized = JsonSerializer.Deserialize(
            jsonBytes,
            SignalingJsonContext.Default.JoinSessionRequest);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("123456", deserialized.Code);
        Assert.Equal("JOIN", deserialized.Action);
    }
}
