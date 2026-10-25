import { describe, it, expect, vi, afterEach } from 'vitest';
import {
  parseManualCode,
  parsePairingPayload,
  isChallengeExpired,
  validatePairingCode,
  getDefaultRelayUrl,
  normalizeRelayUrl,
  BASE32_PAIRING_ALPHABET,
} from '../src/services/pairing';

describe('6-Character Base32 Manual Code Validation & Parsing', () => {
  const futureExpiry = new Date(Date.now() + 50000).toISOString();
  const pastExpiry = new Date(Date.now() - 5000).toISOString();

  it('validates 32-character Crockford Base32 alphabet excludes O, 0, I, and 1', () => {
    expect(BASE32_PAIRING_ALPHABET).toHaveLength(32);
    expect(BASE32_PAIRING_ALPHABET).not.toContain('0');
    expect(BASE32_PAIRING_ALPHABET).not.toContain('O');
    expect(BASE32_PAIRING_ALPHABET).not.toContain('1');
    expect(BASE32_PAIRING_ALPHABET).not.toContain('I');
  });

  it('accepts valid 6-character Base32 pairing codes (e.g. K7M4PX)', () => {
    const validCodes = ['K7M4PX', '234567', 'ZYXWVT', 'A2B3C4', 'R9T8P6'];
    for (const code of validCodes) {
      const { valid, normalized, error } = validatePairingCode(code);
      expect(valid).toBe(true);
      expect(normalized).toBe(code);
      expect(error).toBeUndefined();
    }
  });

  it('normalizes lowercase valid characters to uppercase', () => {
    const { valid, normalized } = validatePairingCode('k7m4px');
    expect(valid).toBe(true);
    expect(normalized).toBe('K7M4PX');
  });

  it('rejects pairing codes containing forbidden confusing characters: 0, 1, I, O', () => {
    const invalidCodes = ['K7M4P0', 'O7M4PX', 'K1M4PX', 'K7MI4P', '01IO23'];
    for (const code of invalidCodes) {
      const { valid, error } = validatePairingCode(code);
      expect(valid).toBe(false);
      expect(error).toMatch(/excluded/i);
    }
  });

  it('rejects codes that are too short or too long', () => {
    expect(validatePairingCode('K7M4P').valid).toBe(false);
    expect(validatePairingCode('K7M4PXX').valid).toBe(false);
    expect(validatePairingCode('').valid).toBe(false);
  });

  it('parses single manual 6-character code without requiring Challenge UUID', () => {
    const result = parseManualCode('K7M4PX', 'wss://relay.example.com');

    expect(result.success).toBe(true);
    expect(result.challenge).toBeDefined();
    expect(result.challenge?.pairingCode).toBe('K7M4PX');
    expect(result.challenge?.relayUrl).toBe('wss://relay.example.com/ws/client');
    // Challenge ID is NOT required or set on manual entry
    expect(result.challenge?.challengeId).toBeUndefined();
  });

  it('successfully parses valid JSON QR payload with pairingCode', () => {
    const rawQr = JSON.stringify({
      version: 1,
      pairingCode: 'K7M4PX',
      relayUrl: 'wss://relay.example.com',
      expiresAt: futureExpiry,
    });

    const result = parsePairingPayload(rawQr);
    expect(result.success).toBe(true);
    expect(result.challenge).toBeDefined();
    expect(result.challenge?.pairingCode).toBe('K7M4PX');
    expect(result.challenge?.relayUrl).toBe('wss://relay.example.com/ws/client');
  });

  it('rejects expired QR payload with clear error', () => {
    const rawQr = JSON.stringify({
      version: 1,
      pairingCode: 'K7M4PX',
      relayUrl: 'wss://relay.example.com',
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

  describe('Dynamic Relay Resolution for Mobile & LAN', () => {
    const originalLocation = window.location;

    afterEach(() => {
      Object.defineProperty(window, 'location', {
        value: originalLocation,
        writable: true,
      });
    });

    it('resolves loopback relay url when on localhost', () => {
      Object.defineProperty(window, 'location', {
        value: {
          hostname: 'localhost',
          host: 'localhost:5173',
          port: '5173',
          protocol: 'http:',
        },
        writable: true,
      });

      expect(getDefaultRelayUrl()).toBe('ws://127.0.0.1:5100/ws/client');
    });

    it('resolves laptop LAN IP on port 5100 when accessed from mobile phone on port 5173', () => {
      Object.defineProperty(window, 'location', {
        value: {
          hostname: '192.168.1.50',
          host: '192.168.1.50:5173',
          port: '5173',
          protocol: 'http:',
        },
        writable: true,
      });

      // Crucial: Must use laptop IP on port 5100 with ws://, NOT wss://192.168.1.50:5173
      expect(getDefaultRelayUrl()).toBe('ws://192.168.1.50:5100/ws/client');

      // parseManualCode without custom relay URL must use this default
      const res = parseManualCode('K7M4PX');
      expect(res.success).toBe(true);
      expect(res.challenge?.relayUrl).toBe('ws://192.168.1.50:5100/ws/client');
    });

    it('resolves 10.x.x.x private LAN IP correctly', () => {
      Object.defineProperty(window, 'location', {
        value: {
          hostname: '10.0.0.15',
          host: '10.0.0.15:5173',
          port: '5173',
          protocol: 'http:',
        },
        writable: true,
      });

      expect(getDefaultRelayUrl()).toBe('ws://10.0.0.15:5100/ws/client');
    });

    it('resolves production HTTPS host to wss://', () => {
      Object.defineProperty(window, 'location', {
        value: {
          hostname: 'remote.example.com',
          host: 'remote.example.com',
          port: '',
          protocol: 'https:',
        },
        writable: true,
      });

      expect(getDefaultRelayUrl()).toBe('wss://remote.example.com/ws/client');
    });

    it('normalizes custom relay URLs without scheme or trailing path', () => {
      expect(normalizeRelayUrl('192.168.1.50:5100')).toBe('ws://192.168.1.50:5100/ws/client');
      expect(normalizeRelayUrl('http://192.168.1.50:5100')).toBe('ws://192.168.1.50:5100/ws/client');
      expect(normalizeRelayUrl('ws://192.168.1.50:5100/ws/companion')).toBe('ws://192.168.1.50:5100/ws/client');
    });
  });
});
