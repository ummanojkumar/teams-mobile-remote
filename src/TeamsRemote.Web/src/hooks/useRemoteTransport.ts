import { useCallback, useEffect, useRef, useState } from 'react';
import { PairingStorage, type ParsedChallenge } from '../services/pairing';
import { TransportClient } from '../services/transport';
import {
  type CommandResultPayload,
  type ControlSnapshotItem,
  INITIAL_REMOTE_STATE,
  type PairedCredentials,
  type PairApprovePayload,
  type RemoteState,
  type StateSnapshotPayload,
} from '../types';

export function useRemoteTransport() {
  const [state, setState] = useState<RemoteState>(INITIAL_REMOTE_STATE);
  const [echoLog, setEchoLog] = useState<{ text: string; time: string; status: string }[]>([]);
  const transportRef = useRef<TransportClient | null>(null);

  const [isMicActionPending, setIsMicActionPending] = useState(false);
  const micActionTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleStatusChange = useCallback((status: RemoteState['connection']) => {
    setState((prev: RemoteState) => ({ ...prev, connection: status }));
  }, []);

  const handleStateSnapshot = useCallback((snapshot: StateSnapshotPayload) => {
    if (micActionTimerRef.current) {
      clearTimeout(micActionTimerRef.current);
      micActionTimerRef.current = null;
    }
    setIsMicActionPending(false);

    setState((prev: RemoteState) => ({
      ...prev,
      stateRevision: snapshot.stateRevision,
      meetingStatus: snapshot.meeting,
      observedAt: snapshot.observedAt,
      controls: {
        microphone: snapshot.controls?.microphone || {
          value: 'unknown',
          capability: 'unavailable',
          confidence: 'unknown',
        },
        camera: snapshot.controls?.camera || {
          value: 'unknown',
          capability: 'unavailable',
          confidence: 'unknown',
        },
        hand: snapshot.controls?.hand || {
          value: 'unknown',
          capability: 'unavailable',
          confidence: 'unknown',
        },
      },
    }));
  }, []);

  const handleCommandResult = useCallback((result: CommandResultPayload) => {
    if (micActionTimerRef.current) {
      clearTimeout(micActionTimerRef.current);
      micActionTimerRef.current = null;
    }
    setIsMicActionPending(false);

    if (result.state?.echo) {
      setEchoLog((prev) => [
        {
          text: result.state?.echo || '',
          time: new Date().toLocaleTimeString(),
          status: result.status,
        },
        ...prev.slice(0, 9),
      ]);
    }

    if (result.status === 'state_uncertain') {
      setState((prev: RemoteState) => ({
        ...prev,
        errorMessage: result.reason || 'Microphone state uncertain after action.',
        controls: {
          ...prev.controls,
          microphone: {
            ...prev.controls.microphone,
            confidence: 'stale',
          },
        },
      }));
      transportRef.current?.queryState();
      return;
    }

    if (result.status === 'rejected') {
      setState((prev: RemoteState) => ({
        ...prev,
        errorMessage: result.reason || 'Command rejected.',
      }));
      if (result.errorCode === 'STALE_COMMAND' || result.errorCode === 'INVALID_STATE_REVISION') {
        transportRef.current?.queryState();
      }
      return;
    }

    if (result.state && (result.state.microphone || result.state.meeting || result.state.confidence)) {
      const micVal = result.state.microphone as ControlSnapshotItem['value'] | undefined;
      const confVal = result.state.confidence as ControlSnapshotItem['confidence'] | undefined;
      const meetingVal = result.state.meeting;
      const normalizedMeeting: RemoteState['meetingStatus'] =
        meetingVal === 'active' || meetingVal === 'none' ? meetingVal : 'unknown';

      setState((prev: RemoteState) => ({
        ...prev,
        stateRevision: result.stateRevision ?? prev.stateRevision,
        observedAt: result.observedAt || prev.observedAt || new Date().toISOString(),
        meetingStatus: meetingVal !== undefined ? normalizedMeeting : prev.meetingStatus,
        errorMessage: null,
        controls: {
          ...prev.controls,
          microphone: micVal
            ? {
                value: micVal,
                confidence: confVal || 'confirmed',
                capability: 'verified',
              }
            : prev.controls.microphone,
        },
      }));
    } else if (result.stateRevision !== undefined) {
      setState((prev: RemoteState) => ({
        ...prev,
        stateRevision: result.stateRevision ?? prev.stateRevision,
        observedAt: result.observedAt || prev.observedAt,
      }));
    }
  }, []);

  const handlePairResponse = useCallback((response: PairApprovePayload) => {
    if (response.approved && response.deviceId) {
      const creds: PairedCredentials = {
        deviceId: response.deviceId,
        sessionToken: response.sessionToken || '',
        relayUrl: transportRef.current?.getRelayUrl() || 'ws://127.0.0.1:5100/ws/client',
        pairedAt: new Date().toISOString(),
      };

      PairingStorage.saveCredentials(creds);
      transportRef.current?.setCredentials(creds);

      setState((prev: RemoteState) => ({
        ...prev,
        pairing: 'paired',
        matchingCode: null,
        errorMessage: null,
      }));

      // Immediately synchronize initial snapshot
      transportRef.current?.queryState();
    } else {
      PairingStorage.clearCredentials();
      const isExpired = response.reason?.toLowerCase().includes('expired');
      setState((prev: RemoteState) => ({
        ...prev,
        pairing: isExpired ? 'expired' : 'rejected',
        errorMessage: response.reason || 'Pairing was declined on the companion.',
      }));
    }
  }, []);

  const handleRevoked = useCallback((reason?: string) => {
    PairingStorage.clearCredentials();
    transportRef.current?.setCredentials(null);
    setState((prev: RemoteState) => ({
      ...prev,
      pairing: 'revoked',
      errorMessage: reason || 'Device pairing was revoked by the companion.',
    }));
  }, []);

  const handleError = useCallback((error: string) => {
    setState((prev: RemoteState) => ({ ...prev, errorMessage: error }));
  }, []);

  // Initialize transport client once
  useEffect(() => {
    const transport = new TransportClient({
      onStatusChange: handleStatusChange,
      onStateSnapshot: handleStateSnapshot,
      onCommandResult: handleCommandResult,
      onPairResponse: handlePairResponse,
      onRevoked: handleRevoked,
      onError: handleError,
    });

    transportRef.current = transport;

    // Load persisted credentials from sessionStorage if available
    const existingCreds = PairingStorage.loadCredentials();
    if (existingCreds) {
      transport.setCredentials(existingCreds);
      setState((prev: RemoteState) => ({ ...prev, pairing: 'paired' }));
      transport.connect(existingCreds.relayUrl);
    }

    return () => {
      transport.disconnect();
    };
  }, [
    handleStatusChange,
    handleStateSnapshot,
    handleCommandResult,
    handlePairResponse,
    handleRevoked,
    handleError,
  ]);

  const startPairing = useCallback((challenge: ParsedChallenge) => {
    if (!transportRef.current) return;

    setState((prev: RemoteState) => ({
      ...prev,
      pairing: 'awaiting_approval',
      matchingCode: challenge.shortCode,
      errorMessage: null,
    }));

    // Pre-save relayUrl to credentials context
    const tempCreds: PairedCredentials = {
      deviceId: '00000000-0000-0000-0000-000000000000',
      sessionToken: '',
      relayUrl: challenge.relayUrl,
      pairedAt: new Date().toISOString(),
    };
    transportRef.current.setCredentials(tempCreds);

    // Connect to relay
    transportRef.current.connect(challenge.relayUrl);

    // Send pair request
    const deviceLabel = `Mobile PWA (${typeof navigator !== 'undefined' ? navigator.platform || 'Device' : 'Browser'})`;
    
    // Poll briefly until socket opens, then send request
    const checkOpenAndSend = () => {
      if (transportRef.current?.getStatus() === 'connected') {
        transportRef.current.sendPairRequest(challenge.challengeId, challenge.shortCode, deviceLabel);
      } else {
        setTimeout(checkOpenAndSend, 100);
      }
    };
    checkOpenAndSend();
  }, []);

  const unpair = useCallback(() => {
    PairingStorage.clearCredentials();
    transportRef.current?.disconnect();
    transportRef.current?.setCredentials(null);
    setState(INITIAL_REMOTE_STATE);
  }, []);

  const testEcho = useCallback((text = 'Hello from Mobile PWA!') => {
    transportRef.current?.testEcho(text);
  }, []);

  const refreshState = useCallback(() => {
    transportRef.current?.queryState();
  }, []);

  const setMicrophoneState = useCallback(
    (desired: 'muted' | 'unmuted') => {
      if (!transportRef.current) return;

      // 1. Anti-double-tap: drop duplicate action if already in flight
      if (isMicActionPending) {
        return;
      }

      // 2. Pre-action capability and confidence gate
      if (
        state.connection !== 'connected' ||
        state.controls.microphone.capability !== 'verified' ||
        state.controls.microphone.confidence !== 'confirmed'
      ) {
        return;
      }

      setIsMicActionPending(true);

      // Optimistically mark confidence as pending
      setState((prev: RemoteState) => ({
        ...prev,
        controls: {
          ...prev.controls,
          microphone: {
            ...prev.controls.microphone,
            confidence: 'pending',
          },
        },
      }));

      // Bounded safety timeout (5000ms)
      if (micActionTimerRef.current) {
        clearTimeout(micActionTimerRef.current);
      }
      micActionTimerRef.current = setTimeout(() => {
        setIsMicActionPending(false);
        setState((prev: RemoteState) => ({
          ...prev,
          errorMessage: 'Microphone action timed out. Re-syncing state...',
          controls: {
            ...prev.controls,
            microphone: {
              ...prev.controls.microphone,
              confidence: 'stale',
            },
          },
        }));
        transportRef.current?.queryState();
      }, 5000);

      const messageId =
        typeof crypto !== 'undefined' && crypto.randomUUID
          ? crypto.randomUUID()
          : 'msg_' + Date.now();
      transportRef.current.sendSetMicrophone(
        desired,
        state.stateRevision,
        messageId,
        messageId
      );
    },
    [
      isMicActionPending,
      state.connection,
      state.controls.microphone,
      state.stateRevision,
    ]
  );

  useEffect(() => {
    return () => {
      if (micActionTimerRef.current) {
        clearTimeout(micActionTimerRef.current);
      }
    };
  }, []);

  const resetPairingState = useCallback(() => {
    PairingStorage.clearCredentials();
    transportRef.current?.setCredentials(null);
    transportRef.current?.disconnect();
    setState((prev: RemoteState) => ({
      ...prev,
      pairing: 'unpaired',
      matchingCode: null,
      errorMessage: null,
    }));
  }, []);

  return {
    state,
    echoLog,
    isMicActionPending,
    setMicrophoneState,
    startPairing,
    unpair,
    testEcho,
    refreshState,
    resetPairingState,
  };
}
