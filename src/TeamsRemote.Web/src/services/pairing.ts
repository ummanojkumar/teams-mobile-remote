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
  pairingCode: string;
  challengeId?: string;
  shortCode?: string;
  relayUrl: string;
  expiresAt: Date;
}

export const BASE32_PAIRING_ALPHABET = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
export const BASE32_PAIRING_REGEX = /^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$/;

export function validatePairingCode(input: string): {
  valid: boolean;
  normalized: string;
  error?: string;
} {
  const normalized = (input || '').trim().toUpperCase();
  if (!normalized) {
    return { valid: false, normalized: '', error: 'Pairing code cannot be empty' };
  }

  // Check forbidden visually confusing characters: 0, 1, I, O
  if (/[01IO]/.test(normalized)) {
    return {
      valid: false,
      normalized,
      error: 'Invalid characters: 0, 1, I, and O are excluded to prevent confusion.',
    };
  }

  if (normalized.length !== 6) {
    return {
      valid: false,
      normalized,
      error: `Expected 6 characters, got ${normalized.length}.`,
    };
  }

  if (!BASE32_PAIRING_REGEX.test(normalized)) {
    return {
      valid: false,
      normalized,
      error: 'Invalid pairing code format. Use capital letters A-Z and digits 2-9.',
    };
  }

  return { valid: true, normalized };
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
      const rawCode = (data.pairingCode || data.shortCode || '').trim().toUpperCase();
      if (!rawCode || !data.relayUrl) {
        return { success: false, error: 'QR payload is missing required fields (pairingCode or relayUrl)' };
      }

      const { valid, normalized, error } = validatePairingCode(rawCode);
      if (!valid) {
        return { success: false, error: 'QR payload contains invalid pairing code: ' + error };
      }

      const expires = data.expiresAt ? new Date(data.expiresAt) : new Date(Date.now() + 60000);
      if (isNaN(expires.getTime())) {
        return { success: false, error: 'Invalid expiration timestamp in payload' };
      }

      if (Date.now() > expires.getTime()) {
        return { success: false, error: 'Pairing challenge has expired (60s limit). Please generate a new QR.' };
      }

      return {
        success: true,
        challenge: {
          pairingCode: normalized,
          shortCode: normalized,
          challengeId: data.challengeId,
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
 * Resolves the appropriate default relay WebSocket URL based on the current client environment:
 * - Localhost / Loopback: ws://127.0.0.1:5100/ws/client
 * - LAN Development (Vite dev port 5173 or private network IPv4 e.g. 192.168.x.x, 10.x.x.x):
 *   ws://<hostname>:5100/ws/client (maps Vite host to Relay port 5100 using ws://)
 * - Production / Public Domain (HTTPS): wss://<host>/ws/client
 */
export function getDefaultRelayUrl(): string {
  if (typeof window === 'undefined' || !window.location) {
    return 'ws://127.0.0.1:5100/ws/client';
  }

  const hostname = window.location.hostname || '127.0.0.1';
  const isHttps = window.location.protocol === 'https:';
  const proto = isHttps ? 'wss:' : 'ws:';

  // 1. Loopback addresses always connect to local relay port 5100
  if (hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1') {
    return 'ws://127.0.0.1:5100/ws/client';
  }

  // 2. LAN development: Vite dev port (5173) or private network IPv4 / mDNS (.local)
  const isPrivateIp =
    /^192\.168\./.test(hostname) ||
    /^10\./.test(hostname) ||
    /^172\.(1[6-9]|2[0-9]|3[0-1])\./.test(hostname) ||
    hostname.endsWith('.local');

  if (window.location.port === '5173' || (!isHttps && isPrivateIp)) {
    return `${proto}//${hostname}:5100/ws/client`;
  }

  // 3. Production / reverse proxy
  return `${proto}//${window.location.host}/ws/client`;
}

/**
 * Validates single 6-character Base32 pairing code without requiring Challenge UUID.
 */
export function parseManualCode(
  codeInput: string,
  relayUrlInput?: string
): { success: boolean; challenge?: ParsedChallenge; error?: string } {
  const { valid, normalized, error } = validatePairingCode(codeInput);
  if (!valid) {
    return { success: false, error };
  }

  const relayUrl = relayUrlInput && relayUrlInput.trim()
    ? normalizeRelayUrl(relayUrlInput.trim())
    : getDefaultRelayUrl();

  return {
    success: true,
    challenge: {
      pairingCode: normalized,
      shortCode: normalized,
      relayUrl,
      // Default manual entry to 60s TTL
      expiresAt: new Date(Date.now() + 60000),
    },
  };
}

/**
 * Legacy manual input parser retained for backward test compatibility.
 */
export function parseManualInput(
  _challengeIdInput: string,
  codeInput: string,
  relayUrlInput?: string
): { success: boolean; challenge?: ParsedChallenge; error?: string } {
  return parseManualCode(codeInput, relayUrlInput);
}

export function formatShortCode(code: string): string {
  return (code || '').trim().toUpperCase();
}

export function normalizeRelayUrl(url: string): string {
  let normalized = (url || '').trim();
  if (!normalized) {
    return getDefaultRelayUrl();
  }

  // Auto-prepend ws:// or wss:// if user entered IP/host without scheme (e.g. 192.168.1.5:5100)
  if (!/^[a-zA-Z]+:\/\//.test(normalized)) {
    const isHttps = typeof window !== 'undefined' && window.location.protocol === 'https:';
    normalized = (isHttps ? 'wss://' : 'ws://') + normalized;
  } else if (normalized.startsWith('http://')) {
    normalized = normalized.replace('http://', 'ws://');
  } else if (normalized.startsWith('https://')) {
    normalized = normalized.replace('https://', 'wss://');
  }

  // In production (https), enforce wss://
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
