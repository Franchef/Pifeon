using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonSenderTests : IDisposable
{
    private readonly Mock<ISignalingService> _signalingServiceMock;
    private readonly Mock<ITransportChannel> _dataChannelMock;
    private readonly PifeonSender _sut; // System Under Test
    private readonly string _testDirectory;

    public PifeonSenderTests()
    {
        _signalingServiceMock = new Mock<ISignalingService>();
        _dataChannelMock = new Mock<ITransportChannel>();

        _sut = new PifeonSender(_signalingServiceMock.Object, _dataChannelMock.Object);

        // Cartella temporanea per i test di scansione filesystem
        _testDirectory = Path.Combine(Path.GetTempPath(), "Pifeon_SenderTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    #region 1. Validation & Constructor Tests

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenSignalingServiceIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new PifeonSender(null!, _dataChannelMock.Object));

        Assert.Equal("signalingService", ex.ParamName);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenDataChannelIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new PifeonSender(_signalingServiceMock.Object, null!));

        Assert.Equal("dataChannel", ex.ParamName);
    }

    [Fact]
    public void Constructor_ShouldForwardOnReceiverJoinedEvent()
    {
        // Arrange
        bool eventRaised = false;
        _sut.OnReceiverJoined += () => eventRaised = true;

        // Act: Scateniamo l'evento sul mock del servizio di segnalazione
        _signalingServiceMock.Raise(s => s.OnReceiverJoined += null);

        // Assert
        Assert.True(eventRaised);
    }

    #endregion

    #region 2. Session Initialization & Send Tests

    [Fact]
    public async Task InitializeSessionAsync_ShouldSetCodeAndReturnIt()
    {
        // Arrange
        string expectedCode = "654321";
        using var cts = new CancellationTokenSource();

        _signalingServiceMock
            .Setup(s => s.CreateSessionAsync(cts.Token))
            .ReturnsAsync(expectedCode);

        // Act
        string result = await _sut.InitializeSessionAsync(cts.Token);

        // Assert
        Assert.Equal(expectedCode, result);
        Assert.Equal(expectedCode, _sut.Code);
    }

    [Fact]
    public async Task SendAsync_ShouldThrowInvalidOperationException_WhenCodeIsEmpty()
    {
        await Task.Yield(); // Ensure the method is truly asynchronous
        // Arrange: Non invochiamo InitializeSessionAsync, quindi Code è stringa vuota
        string sampleFilePath = Path.Combine(_testDirectory, "test.txt");
        await File.WriteAllTextAsync(sampleFilePath, "Dummy content", TestContext.Current.CancellationToken);

        // Act & Assert
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SendAsync(sampleFilePath, TestContext.Current.CancellationToken));

        Assert.Contains("Invocare prima InitializeSessionAsync()", ex.Message);
    }

    [Fact]
    public async Task SendAsync_ShouldScanPathAndWaitForReceiver_WhenCodeIsSet()
    {
        // Arrange
        string generatedCode = "123456";
        using var cts = new CancellationTokenSource();

        _signalingServiceMock
            .Setup(s => s.CreateSessionAsync(cts.Token))
            .ReturnsAsync(generatedCode);

        _signalingServiceMock
            .Setup(s => s.WaitForReceiverAsync(cts.Token))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Prepariamo un file reale per la scansione tramite FolderScanner
        string sampleFilePath = Path.Combine(_testDirectory, "data.bin");
        await File.WriteAllBytesAsync(sampleFilePath, [0x01, 0x02, 0x03, 0x04], TestContext.Current.CancellationToken);

        // Act
        await _sut.InitializeSessionAsync(cts.Token);
        await _sut.SendAsync(sampleFilePath, cts.Token);

        // Assert
        _signalingServiceMock.Verify(s => s.WaitForReceiverAsync(cts.Token), Times.Once);
    }

    #endregion

    #region 3. Lifecycle & Disposal Tests

    [Fact]
    public async Task DisposeAsync_ShouldDisposeDataChannelAndSignalingService()
    {
        // Arrange
        _dataChannelMock
            .Setup(c => c.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        _signalingServiceMock
            .Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        // Act
        await _sut.DisposeAsync();

        // Assert
        _dataChannelMock.Verify(c => c.DisposeAsync(), Times.Once);
        _signalingServiceMock.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange
        _dataChannelMock.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _signalingServiceMock.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // Act
        await _sut.DisposeAsync();
        await _sut.DisposeAsync(); // Seconda chiamata

        // Assert: Devono essere stati invocati esattamente una sola volta
        _dataChannelMock.Verify(c => c.DisposeAsync(), Times.Once);
        _signalingServiceMock.Verify(s => s.DisposeAsync(), Times.Once);
    }

    #endregion
}
