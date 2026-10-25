using System.Text.Json;
using TeamsRemote.Core.Protocol;
using Xunit;

namespace TeamsRemote.Tests;

public class ProtocolEnvelopeTests
{
    [Fact]
    public void Create_ProducesValidEnvelopeWithCurrentVersion()
    {
        var deviceId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var payload = new { test = "value" };

        var envelope = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            payload,
            deviceId,
            correlationId);

        Assert.Equal(ProtocolEnvelope.CurrentVersion, envelope.Version);
        Assert.Equal(ProtocolMessageType.Command, envelope.Type);
        Assert.Equal(deviceId, envelope.DeviceId);
        Assert.Equal(correlationId, envelope.CorrelationId);
        Assert.NotEqual(Guid.Empty, envelope.MessageId);
    }

    [Fact]
    public void TryDeserialize_WhenValidJson_ReturnsSuccess()
    {
        var original = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new { command = "test_echo", echoText = "hello" },
            Guid.NewGuid());

        string json = original.Serialize();
        var (success, envelope, errorCode, reason) = ProtocolEnvelope.TryDeserialize(json);

        Assert.True(success);
        Assert.NotNull(envelope);
        Assert.Null(errorCode);
        Assert.Equal(original.MessageId, envelope.MessageId);
        Assert.Equal(original.Type, envelope.Type);
    }

    [Fact]
    public void TryDeserialize_WhenVersionIsNotOne_ReturnsUnsupportedVersion()
    {
        string invalidVersionJson = """
        {
            "version": 2,
            "type": "command",
            "messageId": "d0f6549b-962d-4147-ba4b-897a25459b8f",
            "correlationId": "d0f6549b-962d-4147-ba4b-897a25459b8f",
            "deviceId": "d0f6549b-962d-4147-ba4b-897a25459b8f",
            "sentAt": "2026-10-24T12:00:00Z",
            "payload": {}
        }
        """;

        var (success, envelope, errorCode, reason) = ProtocolEnvelope.TryDeserialize(invalidVersionJson);

        Assert.False(success);
        Assert.Equal(ProtocolErrorCodes.UnsupportedProtocolVersion, errorCode);
        Assert.Contains("unsupported", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryDeserialize_WhenPayloadExceeds16KB_RejectsPayloadTooLarge()
    {
        // Construct string larger than 16 KB (16,384 bytes)
        string largePadding = new string('A', 17000);
        string largeJson = $$"""
        {
            "version": 1,
            "type": "command",
            "messageId": "d0f6549b-962d-4147-ba4b-897a25459b8f",
            "correlationId": "d0f6549b-962d-4147-ba4b-897a25459b8f",
            "deviceId": "d0f6549b-962d-4147-ba4b-897a25459b8f",
            "sentAt": "2026-10-24T12:00:00Z",
            "payload": {
                "padding": "{{largePadding}}"
            }
        }
        """;

        var (success, envelope, errorCode, reason) = ProtocolEnvelope.TryDeserialize(largeJson);

        Assert.False(success);
        Assert.Equal(ProtocolErrorCodes.PayloadTooLarge, errorCode);
        Assert.Contains("exceeds", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_WhenEnvelopeExceeds16KB_ThrowsException()
    {
        string largePadding = new string('X', 17000);
        var envelope = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new { bigData = largePadding },
            Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => envelope.Serialize());
    }

    [Fact]
    public void TryDeserialize_WhenMalformedJson_ReturnsMalformedJsonError()
    {
        var (success, envelope, errorCode, reason) = ProtocolEnvelope.TryDeserialize("{ not valid json");

        Assert.False(success);
        Assert.Equal("MALFORMED_JSON", errorCode);
    }

    [Fact]
    public void TryDeserialize_WhenNonGuidStringId_DoesNotThrowAndDeserializesDeterministicGuid()
    {
        string json = """
        {
            "version": 1,
            "type": "command",
            "messageId": "msg_1729838402948",
            "correlationId": "corr_1729838402948",
            "deviceId": "00000000-0000-0000-0000-000000000000",
            "sentAt": "2026-10-25T12:00:00Z",
            "payload": { "command": "set_microphone", "desired": "unmuted" }
        }
        """;

        var (success, envelope, errorCode, reason) = ProtocolEnvelope.TryDeserialize(json);

        Assert.True(success, reason);
        Assert.NotNull(envelope);
        Assert.NotEqual(Guid.Empty, envelope.MessageId);
        Assert.NotEqual(Guid.Empty, envelope.CorrelationId);
    }

    [Fact]
    public void TryDeserialize_WhenStandardGuidString_DeserializesExactGuid()
    {
        var expectedMsgId = Guid.NewGuid();
        string json = $$"""
        {
            "version": 1,
            "type": "command",
            "messageId": "{{expectedMsgId}}",
            "correlationId": "{{expectedMsgId}}",
            "deviceId": "00000000-0000-0000-0000-000000000000",
            "sentAt": "2026-10-25T12:00:00Z",
            "payload": {}
        }
        """;

        var (success, envelope, errorCode, reason) = ProtocolEnvelope.TryDeserialize(json);

        Assert.True(success);
        Assert.NotNull(envelope);
        Assert.Equal(expectedMsgId, envelope.MessageId);
        Assert.Equal(expectedMsgId, envelope.CorrelationId);
    }
}

