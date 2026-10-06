using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthyStatus))]
[JsonSerializable(typeof(ServerNetworkMessage))]
[JsonSerializable(typeof(PeerNetworkMessage))]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(JoinSessionRequest))]
[JsonSerializable(typeof(CodeCreatedResponse))]
[JsonSerializable(typeof(ReceiverJoinedResponse))]
[JsonSerializable(typeof(SignalDataMessage))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(TransferManifest))]
[JsonSerializable(typeof(TransferItemInfo))]
[JsonSerializable(typeof(IpExchange))]
public partial class SignalingJsonContext : JsonSerializerContext;
