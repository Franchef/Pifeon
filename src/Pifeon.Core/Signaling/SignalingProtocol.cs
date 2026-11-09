using System.Text;
using System.Text.Json;
using Pifeon.Core.Exceptions;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Signaling;

internal static class SignalingProtocol
{
    public static string ParseCreatedCode(ServerNetworkMessage message)
    {
        string? type = ReadMessageType(message.Payload);
        if (!string.Equals(type, "CODE_CREATED", StringComparison.OrdinalIgnoreCase))
        {
            ThrowIfError(message, code: string.Empty);
            throw new InvalidOperationException("Unexpected signaling response while creating session.");
        }

        CodeCreatedResponse? response = JsonSerializer.Deserialize(
            message.Payload.Span,
            SignalingJsonContext.Default.CodeCreatedResponse);
        if (response is null || string.IsNullOrWhiteSpace(response.Code))
        {
            throw new InvalidOperationException("Signaling server returned an invalid session code payload.");
        }

        return response.Code;
    }

    public static void EnsureReceiverJoined(ServerNetworkMessage message, string code)
    {
        ThrowIfError(message, code);
        string? type = ReadMessageType(message.Payload);
        if (!string.Equals(type, "RECEIVER_JOINED", StringComparison.OrdinalIgnoreCase)
            && message.Type != ServerNetworkMessageType.IpExchanges)
        {
            throw new InvalidDataException($"Unexpected signaling message: {type}.");
        }
    }

    private static void ThrowIfError(ServerNetworkMessage message, string code)
    {
        string? type = ReadMessageType(message.Payload);
        if (message.Type != ServerNetworkMessageType.Abort
            && !string.Equals(type, "ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize(
                message.Payload.Span,
                SignalingJsonContext.Default.ErrorResponse);
        }
        catch (JsonException)
        {
        }

        string errorMessage = error?.Message ?? Encoding.UTF8.GetString(message.Payload.Span);
        if (ContainsAny(errorMessage, "rate", "too many", "429"))
        {
            throw new CreateSessionRateLimitedException(errorMessage);
        }

        if (ContainsAny(errorMessage, "not found", "invalid", "expired", "404"))
        {
            throw new SessionNotFoundException(code);
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorMessage)
            ? "Signaling server returned an error."
            : errorMessage);
    }

    private static string? ReadMessageType(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("type", out JsonElement typeElement)
                ? typeElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ContainsAny(string value, params ReadOnlySpan<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
