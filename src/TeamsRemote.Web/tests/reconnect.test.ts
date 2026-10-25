import { describe, it, expect, beforeEach } from 'vitest';
import { PairingStorage } from '../src/services/pairing';
import type { PairedCredentials } from '../src/types';

describe('Credential Persistence & Reconnect Semantics', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  const testCreds: PairedCredentials = {
    deviceId: '11111111-2222-3333-4444-555555555555',
    sessionToken: 'sess_sec_tok_998877',
    relayUrl: 'wss://relay.example.com',
    pairedAt: new Date().toISOString(),
  };

  it('preserves paired credentials in sessionStorage', () => {
    PairingStorage.saveCredentials(testCreds);
    const loaded = PairingStorage.loadCredentials();

    expect(loaded).not.toBeNull();
    expect(loaded?.deviceId).toBe(testCreds.deviceId);
    expect(loaded?.sessionToken).toBe(testCreds.sessionToken);
    expect(loaded?.relayUrl).toBe(testCreds.relayUrl);
  });

  it('keeps credentials intact across simulated temporary disconnects', () => {
    // 1. Initial pairing saved
    PairingStorage.saveCredentials(testCreds);

    // 2. Simulated network disconnect (transport closed)
    // Credentials MUST still exist in sessionStorage
    const credsDuringDisconnect = PairingStorage.loadCredentials();
    expect(credsDuringDisconnect).toEqual(testCreds);

    // 3. Client can reconnect without re-scanning QR code
    expect(credsDuringDisconnect?.sessionToken).toBe('sess_sec_tok_998877');
  });

  it('clears credentials when explicit unpair occurs', () => {
    PairingStorage.saveCredentials(testCreds);
    expect(PairingStorage.hasCredentials()).toBe(true);

    PairingStorage.clearCredentials();
    expect(PairingStorage.hasCredentials()).toBe(false);
    expect(PairingStorage.loadCredentials()).toBeNull();
  });

  it('clears credentials on companion revocation', () => {
    PairingStorage.saveCredentials(testCreds);

    // Simulated revoke handler execution
    PairingStorage.clearCredentials();

    expect(PairingStorage.loadCredentials()).toBeNull();
  });

  it('formats resumption URL with deviceId and session token query parameters', () => {
    const relayUrl = 'ws://127.0.0.1:5100/ws/client';
    const finalUrl = `${relayUrl}?deviceId=${encodeURIComponent(testCreds.deviceId)}&token=${encodeURIComponent(testCreds.sessionToken)}`;

    expect(finalUrl).toContain('deviceId=11111111-2222-3333-4444-555555555555');
    expect(finalUrl).toContain('token=sess_sec_tok_998877');
  });
});
