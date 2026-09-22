using System.Text.Json.Serialization;

namespace Pifeon.Core.Signaling.Messages;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(JoinSessionRequest))]
[JsonSerializable(typeof(CodeCreatedResponse))]
[JsonSerializable(typeof(ReceiverJoinedResponse))]
[JsonSerializable(typeof(SignalDataMessage))]
[JsonSerializable(typeof(ErrorResponse))]
public partial class SignalingJsonContext : JsonSerializerContext;
