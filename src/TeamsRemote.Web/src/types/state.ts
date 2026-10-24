import type { ControlSnapshotItem } from './protocol';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export type PairingStatus =
  | 'unpaired'
  | 'scanning'
  | 'manual_entry'
  | 'awaiting_approval'
  | 'paired'
  | 'expired'
  | 'rejected'
  | 'revoked';

export interface PairedCredentials {
  deviceId: string;
  sessionToken: string;
  relayUrl: string;
  pairedAt: string;
}

export interface RemoteState {
  connection: ConnectionStatus;
  pairing: PairingStatus;
  stateRevision: number;
  meetingStatus: 'active' | 'none' | 'unknown';
  observedAt: string | null;
  controls: {
    microphone: ControlSnapshotItem;
    camera: ControlSnapshotItem;
    hand: ControlSnapshotItem;
  };
  matchingCode: string | null;
  errorMessage: string | null;
}

export const INITIAL_REMOTE_STATE: RemoteState = {
  connection: 'disconnected',
  pairing: 'unpaired',
  stateRevision: 0,
  meetingStatus: 'unknown',
  observedAt: null,
  controls: {
    microphone: {
      value: 'unknown',
      capability: 'unavailable',
      confidence: 'unknown',
    },
    camera: {
      value: 'unknown',
      capability: 'unavailable',
      confidence: 'unknown',
    },
    hand: {
      value: 'unknown',
      capability: 'unavailable',
      confidence: 'unknown',
    },
  },
  matchingCode: null,
  errorMessage: null,
};
