import { useCallback, useEffect, useRef, useState } from 'react';
import { PairingStorage, getDefaultRelayUrl, type ParsedChallenge } from '../services/pairing';
import { TransportClient } from '../services/transport';
import {
  type CommandResultPayload,
  type ControlSnapshotItem,
  generateUuid,
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

  const [isCameraActionPending, setIsCameraActionPending] = useState(false);
  const cameraActionTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [isHandActionPending, setIsHandActionPending] = useState(false);
  const handActionTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [reactionCooldownRemainingMs, setReactionCooldownRemainingMs] = useState(0);
  const [lastSentReaction, setLastSentReaction] = useState<string | null>(null);
  const reactionCooldownEndTimeRef = useRef<number>(0);
  const reactionCooldownIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const reactionSentTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const pairingConnectTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pairingApprovalTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pairingPollTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [hasConfirmedCameraSafetyThisSession, setHasConfirmedCameraSafetyThisSession] = useState(() => {
    try {
      return typeof sessionStorage !== 'undefined' && sessionStorage.getItem('teams_remote_camera_safety_confirmed') === 'true';
    } catch {
      return false;
    }
  });
  const [showCameraSafetyModal, setShowCameraSafetyModal] = useState(false);

  const handleStatusChange = useCallback((status: RemoteState['connection']) => {
    setState((prev: RemoteState) => ({ ...prev, connection: status }));
  }, []);

  const handleStateSnapshot = useCallback((snapshot: StateSnapshotPayload) => {
    if (micActionTimerRef.current) {
      clearTimeout(micActionTimerRef.current);
      micActionTimerRef.current = null;
    }
    setIsMicActionPending(false);

    if (cameraActionTimerRef.current) {
      clearTimeout(cameraActionTimerRef.current);
      cameraActionTimerRef.current = null;
    }
    setIsCameraActionPending(false);

    if (handActionTimerRef.current) {
      clearTimeout(handActionTimerRef.current);
      handActionTimerRef.current = null;
    }
    setIsHandActionPending(false);

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

    if (cameraActionTimerRef.current) {
      clearTimeout(cameraActionTimerRef.current);
      cameraActionTimerRef.current = null;
    }
    setIsCameraActionPending(false);

    if (handActionTimerRef.current) {
      clearTimeout(handActionTimerRef.current);
      handActionTimerRef.current = null;
    }
    setIsHandActionPending(false);

    if (result.state?.reaction) {
      setLastSentReaction(result.state.reaction);
    }

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
        errorMessage: result.reason || 'Control state uncertain after action.',
        controls: {
          ...prev.controls,
          microphone: {
            ...prev.controls.microphone,
            confidence: 'stale',
          },
          camera: {
            ...prev.controls.camera,
            confidence: 'stale',
          },
          hand: {
            ...prev.controls.hand,
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

    if (
      result.state &&
      (result.state.microphone ||
        result.state.camera ||
        result.state.hand ||
        result.state.meeting ||
        result.state.confidence ||
        result.state.capability)
    ) {
      const micVal = result.state.microphone as ControlSnapshotItem['value'] | undefined;
      const camVal = result.state.camera as ControlSnapshotItem['value'] | undefined;
      const handVal = result.state.hand as ControlSnapshotItem['value'] | undefined;
      const camCap = (result.state.camera_capability || (result.state.camera ? result.state.capability : undefined)) as ControlSnapshotItem['capability'] | undefined;
      const handCap = (result.state.hand_capability || (result.state.hand ? result.state.capability : undefined)) as ControlSnapshotItem['capability'] | undefined;
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
          camera: camVal
            ? {
                value: camVal,
                confidence: confVal || 'confirmed',
                capability: camCap || prev.controls.camera.capability,
              }
            : prev.controls.camera,
          hand: handVal
            ? {
                value: handVal,
                confidence: confVal || 'confirmed',
                capability: handCap || prev.controls.hand.capability,
              }
            : prev.controls.hand,
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
    if (pairingConnectTimerRef.current) {
      clearTimeout(pairingConnectTimerRef.current);
      pairingConnectTimerRef.current = null;
    }
    if (pairingApprovalTimerRef.current) {
      clearTimeout(pairingApprovalTimerRef.current);
      pairingApprovalTimerRef.current = null;
    }
    if (pairingPollTimerRef.current) {
      clearTimeout(pairingPollTimerRef.current);
      pairingPollTimerRef.current = null;
    }

    if (response.approved && response.deviceId) {
      const creds: PairedCredentials = {
        deviceId: response.deviceId,
        sessionToken: response.sessionToken || '',
        relayUrl: transportRef.current?.getRelayUrl() || getDefaultRelayUrl(),
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
    setState((prev: RemoteState) => {
      if (prev.pairing === 'awaiting_approval') {
        if (pairingConnectTimerRef.current) {
          clearTimeout(pairingConnectTimerRef.current);
          pairingConnectTimerRef.current = null;
        }
        if (pairingApprovalTimerRef.current) {
          clearTimeout(pairingApprovalTimerRef.current);
          pairingApprovalTimerRef.current = null;
        }
        if (pairingPollTimerRef.current) {
          clearTimeout(pairingPollTimerRef.current);
          pairingPollTimerRef.current = null;
        }
        return {
          ...prev,
          pairing: 'unpaired',
          errorMessage: `Cannot reach relay server: ${error}. Ensure phone and laptop are on the same Wi-Fi network and firewall allows port access.`,
        };
      }
      return { ...prev, errorMessage: error };
    });
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

    if (pairingConnectTimerRef.current) {
      clearTimeout(pairingConnectTimerRef.current);
      pairingConnectTimerRef.current = null;
    }
    if (pairingApprovalTimerRef.current) {
      clearTimeout(pairingApprovalTimerRef.current);
      pairingApprovalTimerRef.current = null;
    }
    if (pairingPollTimerRef.current) {
      clearTimeout(pairingPollTimerRef.current);
      pairingPollTimerRef.current = null;
    }

    const pairingCode = challenge.pairingCode || challenge.shortCode || '';
    setState((prev: RemoteState) => ({
      ...prev,
      pairing: 'awaiting_approval',
      matchingCode: pairingCode,
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

    // 10-second connection timeout: if socket cannot connect to relay within 10s, fail with clear error
    const connectionStartTime = Date.now();
    pairingConnectTimerRef.current = setTimeout(() => {
      if (pairingPollTimerRef.current) {
        clearTimeout(pairingPollTimerRef.current);
        pairingPollTimerRef.current = null;
      }
      transportRef.current?.disconnect();
      setState((prev: RemoteState) => ({
        ...prev,
        pairing: 'unpaired',
        matchingCode: null,
        errorMessage: `Cannot reach relay at ${challenge.relayUrl}. Ensure phone and laptop are on the same Wi-Fi network and firewall allows access.`,
      }));
    }, 10000);

    // Poll until socket opens, then send pair_request and transition to 60-second approval timeout
    const checkOpenAndSend = () => {
      if (transportRef.current?.getStatus() === 'connected') {
        if (pairingConnectTimerRef.current) {
          clearTimeout(pairingConnectTimerRef.current);
          pairingConnectTimerRef.current = null;
        }

        transportRef.current.sendPairRequest(challenge.challengeId || '00000000-0000-0000-0000-000000000000', pairingCode, deviceLabel);

        // 60-second approval timeout matching challenge TTL
        if (pairingApprovalTimerRef.current) {
          clearTimeout(pairingApprovalTimerRef.current);
        }
        pairingApprovalTimerRef.current = setTimeout(() => {
          transportRef.current?.disconnect();
          setState((prev: RemoteState) => ({
            ...prev,
            pairing: 'expired',
            matchingCode: null,
            errorMessage: 'Pairing request timed out. Please generate a new QR code on the companion.',
          }));
        }, 60000);
      } else {
        if (Date.now() - connectionStartTime < 10000) {
          pairingPollTimerRef.current = setTimeout(checkOpenAndSend, 100);
        }
      }
    };
    checkOpenAndSend();
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

      const messageId = generateUuid();
      const correlationId = generateUuid();
      transportRef.current.sendSetMicrophone(
        desired,
        state.stateRevision,
        messageId,
        correlationId
      );
    },
    [
      isMicActionPending,
      state.connection,
      state.controls.microphone,
      state.stateRevision,
    ]
  );

  const executeCameraAction = useCallback(
    (desired: 'on' | 'off') => {
      if (
        !transportRef.current ||
        isCameraActionPending ||
        state.connection !== 'connected' ||
        state.controls.camera.confidence !== 'confirmed' ||
        (state.controls.camera.capability !== 'observe-only' && state.controls.camera.capability !== 'verified')
      ) {
        return;
      }

      setIsCameraActionPending(true);

      // Optimistically mark confidence as pending
      setState((prev: RemoteState) => ({
        ...prev,
        controls: {
          ...prev.controls,
          camera: {
            ...prev.controls.camera,
            confidence: 'pending',
          },
        },
      }));

      // Bounded safety timeout (5000ms)
      if (cameraActionTimerRef.current) {
        clearTimeout(cameraActionTimerRef.current);
      }
      cameraActionTimerRef.current = setTimeout(() => {
        setIsCameraActionPending(false);
        setState((prev: RemoteState) => ({
          ...prev,
          errorMessage: 'Camera action timed out. Re-syncing state...',
          controls: {
            ...prev.controls,
            camera: {
              ...prev.controls.camera,
              confidence: 'stale',
            },
          },
        }));
        transportRef.current?.queryState();
      }, 5000);

      const messageId = generateUuid();
      const correlationId = generateUuid();
      transportRef.current.sendSetCamera(
        desired,
        state.stateRevision,
        messageId,
        correlationId
      );
    },
    [
      isCameraActionPending,
      state.connection,
      state.controls.camera,
      state.stateRevision,
    ]
  );

  const setCameraState = useCallback(
    (desired: 'on' | 'off') => {
      if (desired === 'on' && !hasConfirmedCameraSafetyThisSession) {
        setShowCameraSafetyModal(true);
        return;
      }
      executeCameraAction(desired);
    },
    [hasConfirmedCameraSafetyThisSession, executeCameraAction]
  );

  const confirmCameraSafety = useCallback(() => {
    setHasConfirmedCameraSafetyThisSession(true);
    try {
      if (typeof sessionStorage !== 'undefined') {
        sessionStorage.setItem('teams_remote_camera_safety_confirmed', 'true');
      }
    } catch {
      // Ignore sessionStorage errors
    }
    setShowCameraSafetyModal(false);
    executeCameraAction('on');
  }, [executeCameraAction]);

  const cancelCameraSafety = useCallback(() => {
    setShowCameraSafetyModal(false);
  }, []);

  const setHandState = useCallback(
    (desired: 'raised' | 'lowered') => {
      if (
        !transportRef.current ||
        isHandActionPending ||
        state.connection !== 'connected' ||
        state.controls.hand.confidence !== 'confirmed' ||
        (state.controls.hand.capability !== 'observe-only' && state.controls.hand.capability !== 'verified')
      ) {
        return;
      }

      setIsHandActionPending(true);

      // Optimistically mark confidence as pending
      setState((prev: RemoteState) => ({
        ...prev,
        controls: {
          ...prev.controls,
          hand: {
            ...prev.controls.hand,
            confidence: 'pending',
          },
        },
      }));

      // Bounded safety timeout (5000ms)
      if (handActionTimerRef.current) {
        clearTimeout(handActionTimerRef.current);
      }
      handActionTimerRef.current = setTimeout(() => {
        setIsHandActionPending(false);
        setState((prev: RemoteState) => ({
          ...prev,
          errorMessage: 'Raise hand action timed out. Re-syncing state...',
          controls: {
            ...prev.controls,
            hand: {
              ...prev.controls.hand,
              confidence: 'stale',
            },
          },
        }));
        transportRef.current?.queryState();
      }, 5000);

      const messageId = generateUuid();
      const correlationId = generateUuid();
      transportRef.current.sendSetHand(
        desired,
        state.stateRevision,
        messageId,
        correlationId
      );
    },
    [
      isHandActionPending,
      state.connection,
      state.controls.hand,
      state.stateRevision,
    ]
  );

  const sendReaction = useCallback(
    (reaction: string) => {
      const now = Date.now();
      if (now < reactionCooldownEndTimeRef.current) {
        // Drop duplicate or rapid tap during cooldown
        return;
      }
      if (
        !transportRef.current ||
        state.connection !== 'connected' ||
        state.meetingStatus !== 'active'
      ) {
        return;
      }

      // Start 500ms (0.5 sec) cooldown immediately
      const cooldownEnd = now + 500;
      reactionCooldownEndTimeRef.current = cooldownEnd;
      setReactionCooldownRemainingMs(500);
      setLastSentReaction(reaction);

      if (reactionCooldownIntervalRef.current) {
        clearInterval(reactionCooldownIntervalRef.current);
      }
      reactionCooldownIntervalRef.current = setInterval(() => {
        const remaining = Math.max(0, reactionCooldownEndTimeRef.current - Date.now());
        setReactionCooldownRemainingMs(remaining);
        if (remaining <= 0) {
          if (reactionCooldownIntervalRef.current) {
            clearInterval(reactionCooldownIntervalRef.current);
            reactionCooldownIntervalRef.current = null;
          }
        }
      }, 50);

      if (reactionSentTimerRef.current) {
        clearTimeout(reactionSentTimerRef.current);
      }
      reactionSentTimerRef.current = setTimeout(() => {
        setLastSentReaction(null);
      }, 1000);

      const messageId = generateUuid();
      const correlationId = generateUuid();
      transportRef.current.sendReaction(reaction, messageId, correlationId);
    },
    [state.connection, state.meetingStatus]
  );

  useEffect(() => {
    return () => {
      if (micActionTimerRef.current) {
        clearTimeout(micActionTimerRef.current);
      }
      if (cameraActionTimerRef.current) {
        clearTimeout(cameraActionTimerRef.current);
      }
      if (handActionTimerRef.current) {
        clearTimeout(handActionTimerRef.current);
      }
      if (pairingConnectTimerRef.current) {
        clearTimeout(pairingConnectTimerRef.current);
      }
      if (pairingApprovalTimerRef.current) {
        clearTimeout(pairingApprovalTimerRef.current);
      }
      if (pairingPollTimerRef.current) {
        clearTimeout(pairingPollTimerRef.current);
      }
      if (reactionCooldownIntervalRef.current) {
        clearInterval(reactionCooldownIntervalRef.current);
      }
      if (reactionSentTimerRef.current) {
        clearTimeout(reactionSentTimerRef.current);
      }
    };
  }, []);

  const resetPairingState = useCallback(() => {
    try {
      if (typeof sessionStorage !== 'undefined') {
        sessionStorage.removeItem('teams_remote_camera_safety_confirmed');
      }
    } catch {
      // Ignore
    }
    setHasConfirmedCameraSafetyThisSession(false);
    setShowCameraSafetyModal(false);
    if (pairingConnectTimerRef.current) {
      clearTimeout(pairingConnectTimerRef.current);
      pairingConnectTimerRef.current = null;
    }
    if (pairingApprovalTimerRef.current) {
      clearTimeout(pairingApprovalTimerRef.current);
      pairingApprovalTimerRef.current = null;
    }
    if (pairingPollTimerRef.current) {
      clearTimeout(pairingPollTimerRef.current);
      pairingPollTimerRef.current = null;
    }
    if (reactionCooldownIntervalRef.current) {
      clearInterval(reactionCooldownIntervalRef.current);
      reactionCooldownIntervalRef.current = null;
    }
    if (reactionSentTimerRef.current) {
      clearTimeout(reactionSentTimerRef.current);
      reactionSentTimerRef.current = null;
    }
    reactionCooldownEndTimeRef.current = 0;
    setReactionCooldownRemainingMs(0);
    setLastSentReaction(null);
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

  const unpair = useCallback(() => {
    try {
      if (typeof sessionStorage !== 'undefined') {
        sessionStorage.removeItem('teams_remote_camera_safety_confirmed');
      }
    } catch {
      // Ignore
    }
    setHasConfirmedCameraSafetyThisSession(false);
    setShowCameraSafetyModal(false);
    resetPairingState();
  }, [resetPairingState]);

  return {
    state,
    echoLog,
    isMicActionPending,
    isCameraActionPending,
    isHandActionPending,
    reactionCooldownRemainingMs,
    lastSentReaction,
    showCameraSafetyModal,
    setMicrophoneState,
    setCameraState,
    setHandState,
    sendReaction,
    confirmCameraSafety,
    cancelCameraSafety,
    startPairing,
    unpair,
    testEcho,
    refreshState,
    resetPairingState,
  };
}
