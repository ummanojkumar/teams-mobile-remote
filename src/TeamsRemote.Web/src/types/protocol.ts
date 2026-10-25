export const PROTOCOL_VERSION = 1;
export const MAX_ENVELOPE_SIZE_BYTES = 16384; // 16 KB cap

export type ProtocolMessageType =
  | 'command'
  | 'command_result'
  | 'state_snapshot'
  | 'pair_challenge'
  | 'pair_request'
  | 'pair_approve'
  | 'pair_response'
  | 'revoke_device'
  | 'ping'
  | 'pong';

export const ProtocolErrorCodes = {
  UnsupportedInPhase2: 'UNSUPPORTED_IN_PHASE_2',
  UnsupportedInPhase4A: 'UNSUPPORTED_IN_PHASE_4A',
  PairingRequired: 'PAIRING_REQUIRED',
  PairingExpired: 'PAIRING_EXPIRED',
  InvalidPairingCode: 'INVALID_PAIRING_CODE',
  PairingRejected: 'PAIRING_REJECTED',
  RateLimited: 'RATE_LIMITED',
  StaleCommand: 'STALE_COMMAND',
  InvalidStateRevision: 'INVALID_STATE_REVISION',
  PayloadTooLarge: 'PAYLOAD_TOO_LARGE',
  UnsupportedProtocolVersion: 'UNSUPPORTED_PROTOCOL_VERSION',
  InvalidEnvelope: 'INVALID_ENVELOPE',
  InvalidJson: 'INVALID_JSON',
} as const;

export type ProtocolErrorCode = typeof ProtocolErrorCodes[keyof typeof ProtocolErrorCodes];

export interface ProtocolEnvelope<T = unknown> {
  version: number;
  type: ProtocolMessageType;
  messageId: string;
  correlationId?: string;
  deviceId: string;
  sentAt: string;
  payload: T;
}

export interface CommandPayload {
  command: string;
  desired?: string;
  expectedStateRevision?: number;
  echoText?: string;
}

export interface CommandResultPayload {
  status: 'accepted' | 'confirmed' | 'rejected' | 'failed' | 'timed_out' | 'unsupported' | 'state_uncertain';
  stateRevision?: number;
  errorCode?: string;
  reason?: string;
  state?: Record<string, string>;
  observedAt?: string;
}

export type ControlValue = 'muted' | 'unmuted' | 'on' | 'off' | 'raised' | 'lowered' | 'unknown';
export type ControlCapability = 'verified' | 'observe-only' | 'fallback' | 'unavailable';
export type StateConfidence = 'confirmed' | 'pending' | 'stale' | 'unknown';

export interface ControlSnapshotItem {
  value: ControlValue;
  capability: ControlCapability;
  confidence: StateConfidence;
}

export interface StateSnapshotPayload {
  stateRevision: number;
  meeting: 'active' | 'none' | 'unknown';
  controls: {
    microphone?: ControlSnapshotItem;
    camera?: ControlSnapshotItem;
    hand?: ControlSnapshotItem;
    [key: string]: ControlSnapshotItem | undefined;
  };
  connection: string;
  observedAt: string;
}

export interface PairChallengePayload {
  challengeId?: string;
  pairingCode: string;
  shortCode?: string;
  relayUrl: string;
  expiresAt: string;
}

export interface PairRequestPayload {
  pairingCode: string;
  shortCode?: string;
  deviceLabel: string;
  challengeId?: string;
}

export interface PairApprovePayload {
  challengeId: string;
  approved: boolean;
  deviceId: string;
  sessionToken?: string;
  reason?: string;
}

export function generateUuid(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    bytes[6] = (bytes[6] & 0x0f) | 0x40; // Version 4
    bytes[8] = (bytes[8] & 0x3f) | 0x80; // Variant 10xx
    const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export function createEnvelope<T>(
  type: ProtocolMessageType,
  payload: T,
  deviceId = '00000000-0000-0000-0000-000000000000',
  correlationId?: string,
  messageId?: string
): ProtocolEnvelope<T> {
  return {
    version: PROTOCOL_VERSION,
    type,
    messageId: messageId || generateUuid(),
    correlationId: correlationId || generateUuid(),
    deviceId,
    sentAt: new Date().toISOString(),
    payload,
  };
}

export function serializeEnvelope<T>(envelope: ProtocolEnvelope<T>): string {
  const json = JSON.stringify(envelope);
  const size = new TextEncoder().encode(json).length;
  if (size > MAX_ENVELOPE_SIZE_BYTES) {
    throw new Error(`Envelope exceeds maximum permitted size of ${MAX_ENVELOPE_SIZE_BYTES} bytes (${size} bytes)`);
  }
  return json;
}

export function deserializeEnvelope<T = unknown>(rawJson: string): {
  success: boolean;
  envelope?: ProtocolEnvelope<T>;
  errorCode?: string;
  reason?: string;
} {
  const byteLength = new TextEncoder().encode(rawJson).length;
  if (byteLength > MAX_ENVELOPE_SIZE_BYTES) {
    return {
      success: false,
      errorCode: ProtocolErrorCodes.PayloadTooLarge,
      reason: `Message exceeds size limit of ${MAX_ENVELOPE_SIZE_BYTES} bytes (${byteLength} bytes)`,
    };
  }

  try {
    const parsed = JSON.parse(rawJson);
    if (!parsed || typeof parsed !== 'object') {
      return {
        success: false,
        errorCode: ProtocolErrorCodes.InvalidEnvelope,
        reason: 'Parsed payload is not an object',
      };
    }

    if (parsed.version !== PROTOCOL_VERSION) {
      return {
        success: false,
        errorCode: ProtocolErrorCodes.UnsupportedProtocolVersion,
        reason: `Unsupported protocol version ${parsed.version}. Expected ${PROTOCOL_VERSION}`,
      };
    }

    if (!parsed.type || !parsed.messageId || !parsed.deviceId) {
      return {
        success: false,
        errorCode: ProtocolErrorCodes.InvalidEnvelope,
        reason: 'Missing required envelope fields (type, messageId, or deviceId)',
      };
    }

    return {
      success: true,
      envelope: parsed as ProtocolEnvelope<T>,
    };
  } catch (ex) {
    return {
      success: false,
      errorCode: ProtocolErrorCodes.InvalidJson,
      reason: ex instanceof Error ? ex.message : 'Invalid JSON format',
    };
  }
}
