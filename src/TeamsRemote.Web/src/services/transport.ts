import {
  createEnvelope,
  deserializeEnvelope,
  MAX_ENVELOPE_SIZE_BYTES,
  type ProtocolEnvelope,
  ProtocolErrorCodes,
  serializeEnvelope,
  type StateSnapshotPayload,
  type CommandResultPayload,
  type PairApprovePayload,
  type ConnectionStatus,
  type PairedCredentials,
  generateUuid,
} from '../types';
import { normalizeRelayUrl } from './pairing';

export interface TransportEvents {
  onStatusChange: (status: ConnectionStatus) => void;
  onStateSnapshot: (snapshot: StateSnapshotPayload) => void;
  onCommandResult: (result: CommandResultPayload, correlationId?: string) => void;
  onPairResponse: (response: PairApprovePayload) => void;
  onRevoked: (reason?: string) => void;
  onError: (error: string) => void;
}

export class TransportClient {
  private socket: WebSocket | null = null;
  private status: ConnectionStatus = 'disconnected';
  private reconnectAttempt = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private heartbeatInterval: ReturnType<typeof setInterval> | null = null;
  private isDisposed = false;
  private credentials: PairedCredentials | null = null;
  private events: Partial<TransportEvents> = {};

  constructor(events: Partial<TransportEvents>) {
    this.events = events;
  }

  getStatus(): ConnectionStatus {
    return this.status;
  }

  setCredentials(creds: PairedCredentials | null): void {
    this.credentials = creds;
  }

  getCredentials(): PairedCredentials | null {
    return this.credentials;
  }

  getRelayUrl(): string {
    return this.credentials?.relayUrl || '';
  }

  hasValidCredentials(): boolean {
    return !!(
      this.credentials &&
      this.credentials.deviceId &&
      this.credentials.deviceId !== '00000000-0000-0000-0000-000000000000' &&
      this.credentials.sessionToken
    );
  }

  connect(relayUrl: string): void {
    this.isDisposed = false;
    this.cleanupSocket();

    // Production safety enforcement: ensure wss:// if page is https:
    let finalUrl = normalizeRelayUrl(relayUrl);
    if (typeof window !== 'undefined' && window.location.protocol === 'https:' && finalUrl.startsWith('ws://')) {
      finalUrl = finalUrl.replace('ws://', 'wss://');
    }

    // If client holds valid pairing credentials, pass deviceId and token for immediate session resumption
    if (this.hasValidCredentials() && this.credentials) {
      const sep = finalUrl.includes('?') ? '&' : '?';
      finalUrl += `${sep}deviceId=${encodeURIComponent(this.credentials.deviceId)}&token=${encodeURIComponent(this.credentials.sessionToken)}`;
    }

    this.setStatus(this.reconnectAttempt > 0 ? 'reconnecting' : 'connecting');

    try {
      this.socket = new WebSocket(finalUrl);
    } catch (ex) {
      this.setStatus('disconnected');
      this.events.onError?.(ex instanceof Error ? ex.message : 'Failed to create WebSocket');
      this.scheduleReconnect(finalUrl);
      return;
    }

    this.socket.onopen = () => {
      this.reconnectAttempt = 0;
      this.setStatus('connected');
      this.startHeartbeat();

      // Immediately query current state snapshot upon connect or reconnect ONLY if already paired
      if (this.hasValidCredentials()) {
        this.queryState();
      }
    };

    this.socket.onmessage = (event: MessageEvent) => {
      this.handleIncomingMessage(typeof event.data === 'string' ? event.data : '');
    };

    this.socket.onerror = () => {
      // Handled in onclose
    };

    this.socket.onclose = (event: CloseEvent) => {
      this.stopHeartbeat();
      if (this.isDisposed) return;

      this.setStatus('disconnected');

      // Normal closure vs unexpected disconnect
      if (event.code === 1000) {
        // Closed intentionally
        return;
      }

      this.scheduleReconnect(finalUrl);
    };
  }

  private handleIncomingMessage(rawText: string): void {
    if (!rawText) return;

    if (new TextEncoder().encode(rawText).length > MAX_ENVELOPE_SIZE_BYTES) {
      this.events.onError?.('Received oversized message from relay');
      return;
    }

    const { success, envelope, errorCode, reason } = deserializeEnvelope(rawText);
    if (!success || !envelope) {
      this.events.onError?.(`Protocol error: ${errorCode} - ${reason}`);
      return;
    }

    switch (envelope.type) {
      case 'ping':
        this.sendEnvelope(createEnvelope('pong', {}, this.getDeviceId(), envelope.messageId));
        break;

      case 'pong':
        // Heartbeat confirmed
        break;

      case 'state_snapshot':
        this.events.onStateSnapshot?.(envelope.payload as StateSnapshotPayload);
        break;

      case 'command_result': {
        const result = envelope.payload as CommandResultPayload;
        if (result.errorCode === ProtocolErrorCodes.PairingRequired) {
          // Only treat as revocation if this client previously held valid paired credentials
          if (this.hasValidCredentials()) {
            this.events.onRevoked?.(result.reason || 'Device pairing has been revoked by the companion.');
            this.disconnect();
          }
          return;
        }
        this.events.onCommandResult?.(result, envelope.correlationId);
        break;
      }

      case 'pair_response': {
        const pairResp = envelope.payload as PairApprovePayload;
        this.events.onPairResponse?.(pairResp);
        break;
      }
    }
  }

  sendEnvelope<T>(envelope: ProtocolEnvelope<T>): boolean {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      return false;
    }

    try {
      const json = serializeEnvelope(envelope);
      this.socket.send(json);
      return true;
    } catch (ex) {
      this.events.onError?.(ex instanceof Error ? ex.message : 'Send error');
      return false;
    }
  }

  sendPairRequest(challengeId: string, pairingCode: string, deviceLabel: string): boolean {
    const code = (pairingCode || '').trim().toUpperCase();
    const envelope = createEnvelope(
      'pair_request',
      {
        pairingCode: code,
        shortCode: code,
        deviceLabel: deviceLabel || 'Mobile Device',
        challengeId: challengeId || '00000000-0000-0000-0000-000000000000',
      },
      '00000000-0000-0000-0000-000000000000'
    );
    return this.sendEnvelope(envelope);
  }

  testEcho(echoText: string): boolean {
    const envelope = createEnvelope(
      'command',
      { command: 'test_echo', echoText },
      this.getDeviceId()
    );
    return this.sendEnvelope(envelope);
  }

  queryState(): boolean {
    const envelope = createEnvelope(
      'command',
      { command: 'query_state' },
      this.getDeviceId()
    );
    return this.sendEnvelope(envelope);
  }

  sendSetMicrophone(
    desired: 'muted' | 'unmuted',
    expectedStateRevision: number,
    messageId?: string,
    correlationId?: string
  ): boolean {
    if (!this.hasValidCredentials()) return false;
    const msgId = messageId || generateUuid();
    const corrId = correlationId || msgId;

    const envelope = createEnvelope(
      'command',
      {
        command: 'set_microphone',
        desired,
        expectedStateRevision,
      },
      this.getDeviceId(),
      corrId,
      msgId
    );
    return this.sendEnvelope(envelope);
  }

  /**
   * Phase 4C: Remote Desired-State Camera Control
   * Dispatches explicit 'on' or 'off' desired-state command with expectedStateRevision.
   */
  sendSetCamera(
    desired: 'on' | 'off',
    expectedStateRevision: number,
    messageId?: string,
    correlationId?: string
  ): boolean {
    if (!this.hasValidCredentials()) return false;
    const msgId = messageId || generateUuid();
    const corrId = correlationId || msgId;

    const envelope = createEnvelope(
      'command',
      {
        command: 'set_camera',
        desired,
        expectedStateRevision,
      },
      this.getDeviceId(),
      corrId,
      msgId
    );
    return this.sendEnvelope(envelope);
  }

  /**
   * Phase 5B: Remote Desired-State Raise Hand Control
   * Dispatches explicit 'raised' or 'lowered' desired-state command with expectedStateRevision.
   */
  sendSetHand(
    desired: 'raised' | 'lowered',
    expectedStateRevision: number,
    messageId?: string,
    correlationId?: string
  ): boolean {
    if (!this.hasValidCredentials()) return false;
    const msgId = messageId || generateUuid();
    const corrId = correlationId || msgId;

    const envelope = createEnvelope(
      'command',
      {
        command: 'set_hand',
        desired,
        expectedStateRevision,
      },
      this.getDeviceId(),
      corrId,
      msgId
    );
    return this.sendEnvelope(envelope);
  }

  /**
   * Phase 6B: Remote Teams Live Reaction Control
   * Dispatches one-shot transient reaction command: 'like' | 'love' | 'applause' | 'laugh' | 'surprise'.
   */
  sendReaction(
    reaction: string,
    messageId?: string,
    correlationId?: string
  ): boolean {
    if (!this.hasValidCredentials()) return false;
    const msgId = messageId || generateUuid();
    const corrId = correlationId || msgId;

    const envelope = createEnvelope(
      'command',
      {
        command: 'send_reaction',
        reaction,
      },
      this.getDeviceId(),
      corrId,
      msgId
    );
    return this.sendEnvelope(envelope);
  }

  /**
   * Unknown/unsupported command guard.
   */
  sendRemoteControlCommand(command: string): never {
    throw new Error(
      `Remote Teams control '${command}' is not supported.`
    );
  }

  sendCommand(command: string, desired?: string, expectedStateRevision = 0): boolean {
    if (command === 'set_microphone' && (desired === 'muted' || desired === 'unmuted')) {
      return this.sendSetMicrophone(desired, expectedStateRevision);
    }
    if (command === 'set_camera' && (desired === 'on' || desired === 'off')) {
      return this.sendSetCamera(desired, expectedStateRevision);
    }
    if (command === 'set_hand' && (desired === 'raised' || desired === 'lowered')) {
      return this.sendSetHand(desired, expectedStateRevision);
    }
    if (command === 'send_reaction' && desired) {
      return this.sendReaction(desired);
    }
    return this.sendRemoteControlCommand(command);
  }

  private scheduleReconnect(url: string): void {
    if (this.isDisposed) return;
    this.reconnectAttempt++;

    // Exponential backoff with jitter: Math.min(1000 * 2^attempt, 30000) +/- 200ms
    const baseDelay = Math.min(1000 * Math.pow(2, Math.min(this.reconnectAttempt, 5)), 30000);
    const jitter = Math.floor(Math.random() * 400) - 200;
    const delay = Math.max(500, baseDelay + jitter);

    this.setStatus('reconnecting');
    this.reconnectTimer = setTimeout(() => {
      if (!this.isDisposed) {
        this.connect(url);
      }
    }, delay);
  }

  private startHeartbeat(): void {
    this.stopHeartbeat();
    this.heartbeatInterval = setInterval(() => {
      if (this.socket && this.socket.readyState === WebSocket.OPEN && this.hasValidCredentials()) {
        this.sendEnvelope(createEnvelope('ping', {}, this.getDeviceId()));
      }
    }, 15000);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval);
      this.heartbeatInterval = null;
    }
  }

  private setStatus(status: ConnectionStatus): void {
    if (this.status !== status) {
      this.status = status;
      this.events.onStatusChange?.(status);
    }
  }

  private getDeviceId(): string {
    return this.credentials?.deviceId || '00000000-0000-0000-0000-000000000000';
  }

  disconnect(): void {
    this.isDisposed = true;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    this.stopHeartbeat();
    this.cleanupSocket();
    this.setStatus('disconnected');
  }

  private cleanupSocket(): void {
    if (this.socket) {
      try {
        this.socket.onopen = null;
        this.socket.onmessage = null;
        this.socket.onerror = null;
        this.socket.onclose = null;
        if (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING) {
          this.socket.close(1000, 'Client disconnecting');
        }
      } catch {
        // Ignore
      }
      this.socket = null;
    }
  }
}
