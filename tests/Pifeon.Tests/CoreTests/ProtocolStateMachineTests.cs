using Pifeon.Core.StateMachines;

namespace Pifeon.Tests.CoreTests;

public sealed class ProtocolStateMachineTests
{
    [Fact]
    public void ConnectionStateMachine_ShouldFollowDirectConnectionLifecycle()
    {
        var state = new ConnectionStateMachine();

        state.TransitionTo(ConnectionState.WaitingForPeer);
        state.TransitionTo(ConnectionState.Establishing);
        state.TransitionTo(ConnectionState.Connected);

        Assert.True(state.IsConnected);
        Assert.True(state.TryBeginClosing());
        Assert.False(state.TryBeginClosing());
        state.TransitionTo(ConnectionState.Closed);
        Assert.Equal(ConnectionState.Closed, state.State);
    }

    [Fact]
    public void ConnectionStateMachine_ShouldRejectInvalidTransition()
    {
        var state = new ConnectionStateMachine();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            state.TransitionTo(ConnectionState.Closed));

        Assert.Contains("Created to Closed", error.Message);
    }

    [Fact]
    public void TransferStateMachine_ShouldSupportSenderLifecycle()
    {
        var state = new TransferStateMachine();

        state.TransitionTo(TransferState.Preparing);
        state.TransitionTo(TransferState.WaitingForConnection);
        state.TransitionTo(TransferState.Transferring);
        state.TransitionTo(TransferState.Completed);

        Assert.Equal(TransferState.Completed, state.State);
    }

    [Fact]
    public void TransferStateMachine_ShouldSupportReceiverLifecycle()
    {
        var state = new TransferStateMachine();

        state.TransitionTo(TransferState.WaitingForManifest);
        state.TransitionTo(TransferState.Ready);
        state.TransitionTo(TransferState.Transferring);
        state.TransitionTo(TransferState.Completed);

        Assert.Equal(TransferState.Completed, state.State);
    }

    [Fact]
    public void TransferStateMachine_ShouldRejectRepeatedTransfer()
    {
        var state = new TransferStateMachine();
        state.TransitionTo(TransferState.Preparing);
        state.TransitionTo(TransferState.Transferring);
        state.TransitionTo(TransferState.Completed);

        Assert.Throws<InvalidOperationException>(() =>
            state.TransitionTo(TransferState.Preparing));
    }
}
