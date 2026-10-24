import { describe, it, expect } from 'vitest';
import {
  parseManualInput,
  parsePairingPayload,
  isChallengeExpired,
} from '../src/services/pairing';

describe('Manual Code Validation & Parsing', () => {
  const validGuid = 'a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d';
  const futureExpiry = new Date(Date.now() + 50000).toISOString();
  const pastExpiry = new Date(Date.now() - 5000).toISOString();

  it('successfully parses valid JSON QR payload', () => {
    const rawQr = JSON.stringify({
      challengeId: validGuid,
      relayUrl: 'wss://relay.example.com',
      shortCode: '849201',
      expiresAt: futureExpiry,
    });

    const result = parsePairingPayload(rawQr);
    expect(result.success).toBe(true);
    expect(result.challenge).toBeDefined();
    expect(result.challenge?.challengeId).toBe(validGuid);
    expect(result.challenge?.shortCode).toBe('849-201');
    expect(result.challenge?.relayUrl).toBe('wss://relay.example.com/ws/client');
  });

  it('rejects expired QR payload with clear error', () => {
    const rawQr = JSON.stringify({
      challengeId: validGuid,
      relayUrl: 'wss://relay.example.com',
      shortCode: '849201',
      expiresAt: pastExpiry,
    });

    const result = parsePairingPayload(rawQr);
    expect(result.success).toBe(false);
    expect(result.error).toMatch(/challenge has expired/i);
  });

  it('correctly calculates expiration status', () => {
    expect(isChallengeExpired(futureExpiry)).toBe(false);
    expect(isChallengeExpired(pastExpiry)).toBe(true);
  });

  it('parses manual challenge entry with relay URL, challenge ID, and short code', () => {
    const result = parseManualInput(
      validGuid,
      '123456',
      'wss://relay.example.com'
    );

    expect(result.success).toBe(true);
    expect(result.challenge?.challengeId).toBe(validGuid);
    expect(result.challenge?.shortCode).toBe('123-456');
    expect(result.challenge?.relayUrl).toBe('wss://relay.example.com/ws/client');
  });

  it('rejects manual challenge entry with invalid GUID', () => {
    const result = parseManualInput(
      'not-a-valid-guid',
      '123456',
      'wss://relay.example.com'
    );

    expect(result.success).toBe(false);
    expect(result.error).toMatch(/valid UUID/i);
  });

  it('rejects manual challenge entry with invalid short code length', () => {
    const result = parseManualInput(
      validGuid,
      '12', // too short
      'wss://relay.example.com'
    );

    expect(result.success).toBe(false);
    expect(result.error).toMatch(/6 digits/i);
  });

  it('formats 6-digit confirmation codes consistently with hyphen (123456 -> 123-456)', () => {
    const result = parseManualInput(
      validGuid,
      '789012',
      'wss://relay.example.com'
    );

    expect(result.success).toBe(true);
    expect(result.challenge?.shortCode).toBe('789-012');
  });
});
