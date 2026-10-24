import { describe, it, expect } from 'vitest';
import {
  createEnvelope,
  deserializeEnvelope,
  serializeEnvelope,
  MAX_ENVELOPE_SIZE_BYTES,
  PROTOCOL_VERSION,
  ProtocolErrorCodes,
} from '../src/types/protocol';

describe('Protocol Transport Serialization & Validation', () => {
  it('creates valid envelope with UUIDs and timestamp', () => {
    const envelope = createEnvelope('ping', { test: true }, 'device-123', 'corr-456');

    expect(envelope.version).toBe(PROTOCOL_VERSION);
    expect(envelope.type).toBe('ping');
    expect(envelope.deviceId).toBe('device-123');
    expect(envelope.correlationId).toBe('corr-456');
    expect(envelope.messageId).toBeDefined();
    expect(new Date(envelope.sentAt).getTime()).toBeGreaterThan(0);
    expect(envelope.payload).toEqual({ test: true });
  });

  it('serializes and deserializes envelopes symmetrically', () => {
    const original = createEnvelope('state_snapshot', {
      stateRevision: 42,
      meeting: 'active',
      controls: {
        microphone: { value: 'muted', capability: 'verified', confidence: 'confirmed' },
      },
      connection: 'connected',
      observedAt: '2026-10-24T20:00:00Z',
    });

    const serialized = serializeEnvelope(original);
    expect(typeof serialized).toBe('string');

    const result = deserializeEnvelope<typeof original.payload>(serialized);
    expect(result.success).toBe(true);
    expect(result.envelope).toBeDefined();
    expect(result.envelope?.version).toBe(1);
    expect(result.envelope?.type).toBe('state_snapshot');
    expect(result.envelope?.payload.stateRevision).toBe(42);
    expect(result.envelope?.payload.meeting).toBe('active');
  });

  it('enforces 16 KB maximum message limit on serialization', () => {
    const hugePayload = { data: 'X'.repeat(MAX_ENVELOPE_SIZE_BYTES + 100) };
    const envelope = createEnvelope('command', hugePayload);

    expect(() => serializeEnvelope(envelope)).toThrow(/exceeds maximum permitted size/);
  });

  it('rejects inbound payloads exceeding 16 KB limit before full processing', () => {
    const hugeRaw = JSON.stringify({
      version: 1,
      type: 'command',
      messageId: '1',
      deviceId: '1',
      sentAt: 'now',
      payload: 'X'.repeat(MAX_ENVELOPE_SIZE_BYTES + 200),
    });

    const result = deserializeEnvelope(hugeRaw);
    expect(result.success).toBe(false);
    expect(result.errorCode).toBe(ProtocolErrorCodes.PayloadTooLarge);
  });

  it('rejects invalid JSON string syntax', () => {
    const invalidJson = '{ version: 1, type: "ping" ... not valid';
    const result = deserializeEnvelope(invalidJson);

    expect(result.success).toBe(false);
    expect(result.errorCode).toBe(ProtocolErrorCodes.InvalidJson);
  });

  it('rejects unsupported protocol versions', () => {
    const badVersionEnvelope = JSON.stringify({
      version: 99,
      type: 'ping',
      messageId: 'msg-1',
      deviceId: 'dev-1',
      sentAt: new Date().toISOString(),
      payload: {},
    });

    const result = deserializeEnvelope(badVersionEnvelope);
    expect(result.success).toBe(false);
    expect(result.errorCode).toBe(ProtocolErrorCodes.UnsupportedProtocolVersion);
  });

  it('rejects envelope missing required fields', () => {
    const missingFields = JSON.stringify({
      version: 1,
      payload: {},
      // missing type, messageId, deviceId
    });

    const result = deserializeEnvelope(missingFields);
    expect(result.success).toBe(false);
    expect(result.errorCode).toBe(ProtocolErrorCodes.InvalidEnvelope);
  });
});
