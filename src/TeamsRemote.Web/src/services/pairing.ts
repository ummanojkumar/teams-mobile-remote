import type { PairedCredentials, PairChallengePayload } from '../types';

const STORAGE_KEY = 'teams_remote_credentials';

/**
 * Manages paired device credentials in sessionStorage.
 * Credentials persist across transient WebSocket disconnects for automatic reconnection,
 * and are cleared only upon pairing expiry, revocation, explicit unpair, or session close.
 */
export class PairingStorage {
  static saveCredentials(creds: PairedCredentials): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(creds));
    } catch {
      // Ignore sessionStorage exceptions (e.g. private browsing quota)
    }
  }

  static loadCredentials(): PairedCredentials | null {
    try {
      const data = sessionStorage.getItem(STORAGE_KEY);
      if (!data) return null;
      const parsed = JSON.parse(data) as PairedCredentials;
      if (parsed.deviceId && parsed.sessionToken && parsed.relayUrl) {
        return parsed;
      }
      return null;
    } catch {
      return null;
    }
  }

  static hasCredentials(): boolean {
    return this.loadCredentials() !== null;
  }

  static clearCredentials(): void {
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // Ignore
    }
  }
}

export interface ParsedChallenge {
  challengeId: string;
  shortCode: string;
  relayUrl: string;
  expiresAt: Date;
}

/**
 * Validates and parses either raw QR JSON or manual form inputs.
 */
export function parsePairingPayload(input: string): {
  success: boolean;
  challenge?: ParsedChallenge;
  error?: string;
} {
  const trimmed = input.trim();
  if (!trimmed) {
    return { success: false, error: 'Input cannot be empty' };
  }

  // Attempt JSON parsing (from QR code scan or clipboard paste)
  if (trimmed.startsWith('{') && trimmed.endsWith('}')) {
    try {
      const data = JSON.parse(trimmed) as Partial<PairChallengePayload>;
      if (!data.challengeId || !data.shortCode || !data.relayUrl || !data.expiresAt) {
        return { success: false, error: 'QR payload is missing required fields (challengeId, shortCode, or relayUrl)' };
      }

      const expires = new Date(data.expiresAt);
      if (isNaN(expires.getTime())) {
        return { success: false, error: 'Invalid expiration timestamp in payload' };
      }

      if (Date.now() > expires.getTime()) {
        return { success: false, error: 'Pairing challenge has expired (60s limit). Please generate a new QR.' };
      }

      return {
        success: true,
        challenge: {
          challengeId: data.challengeId,
          shortCode: formatShortCode(data.shortCode),
          relayUrl: normalizeRelayUrl(data.relayUrl),
          expiresAt: expires,
        },
      };
    } catch (e) {
      return { success: false, error: 'Failed to parse JSON QR payload: ' + (e instanceof Error ? e.message : 'Invalid JSON') };
    }
  }

  return { success: false, error: 'Unrecognized pairing input. Please scan the QR code or paste the JSON payload.' };
}

export function isChallengeExpired(expiresAt: string | Date): boolean {
  const expires = typeof expiresAt === 'string' ? new Date(expiresAt) : expiresAt;
  return isNaN(expires.getTime()) || Date.now() > expires.getTime();
}

/**
 * Validates manual inputs (Challenge ID GUID + 6-digit confirmation code).
 */
export function parseManualInput(
  challengeIdInput: string,
  codeInput: string,
  relayUrlInput?: string
): { success: boolean; challenge?: ParsedChallenge; error?: string } {
  const cleanId = challengeIdInput.trim();
  const cleanCode = codeInput.trim();

  // Validate GUID format
  const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!guidRegex.test(cleanId)) {
    return { success: false, error: 'Invalid Challenge ID. Must be a valid UUID (e.g. 12345678-1234-1234-1234-1234567890ab).' };
  }

  // Validate 6-digit code format (e.g. 123-456 or 123456)
  const codeRegex = /^\d{3}-?\d{3}$/;
  if (!codeRegex.test(cleanCode)) {
    return { success: false, error: 'Invalid verification code. Expected 6 digits (e.g. 123-456).' };
  }

  const defaultRelay = typeof window !== 'undefined' && window.location.hostname === 'localhost'
    ? 'ws://127.0.0.1:5100/ws/client'
    : 'wss://' + (typeof window !== 'undefined' ? window.location.host : 'relay.example.com') + '/ws/client';

  const relayUrl = relayUrlInput ? normalizeRelayUrl(relayUrlInput.trim()) : defaultRelay;

  return {
    success: true,
    challenge: {
      challengeId: cleanId,
      shortCode: formatShortCode(cleanCode),
      relayUrl,
      // Default manual entry to 60s TTL from now if not specified in JSON
      expiresAt: new Date(Date.now() + 60000),
    },
  };
}

export function formatShortCode(code: string): string {
  const digits = code.replace(/\D/g, '');
  if (digits.length === 6) {
    return `${digits.slice(0, 3)}-${digits.slice(3, 6)}`;
  }
  return code;
}

export function normalizeRelayUrl(url: string): string {
  let normalized = (url || '').trim();
  if (!normalized) {
    return typeof window !== 'undefined' && window.location.hostname === 'localhost'
      ? 'ws://127.0.0.1:5100/ws/client'
      : 'wss://' + (typeof window !== 'undefined' ? window.location.host : 'relay.example.com') + '/ws/client';
  }

  // In production, enforce wss://
  if (typeof window !== 'undefined' && window.location.protocol === 'https:' && normalized.startsWith('ws://')) {
    normalized = normalized.replace('ws://', 'wss://');
  }

  // Ensure path ends with /ws/client
  if (normalized.endsWith('/ws/companion')) {
    normalized = normalized.replace('/ws/companion', '/ws/client');
  } else if (!normalized.endsWith('/ws/client')) {
    normalized = normalized.replace(/\/+$/, '') + '/ws/client';
  }

  return normalized;
}
