import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useRemoteTransport } from '../src/hooks/useRemoteTransport';
import type { StateSnapshotPayload } from '../src/types';
import { ReactionTypes } from '../src/types/protocol';

let mockTransportInstances: any[] = [];

vi.mock('../src/services/transport', () => {
  class MockTransportClient {
    callbacks: any;
    connect = vi.fn();
    disconnect = vi.fn();
    sendPairChallenge = vi.fn();
    sendPairApprove = vi.fn();
    sendPairRequest = vi.fn();
    unpair = vi.fn();
    sendEcho = vi.fn();
    sendRefreshState = vi.fn();
    queryState = vi.fn();
    sendSetMicrophone = vi.fn();
    sendSetCamera = vi.fn();
    sendSetHand = vi.fn();
    sendReaction = vi.fn();
    sendCommand = vi.fn();
    setCredentials = vi.fn();

    constructor(callbacks: any) {
      this.callbacks = callbacks;
      mockTransportInstances.push(this);
    }
  }
  return { TransportClient: MockTransportClient };
});

describe('Phase 6B Live Reactions Integration Flow', () => {
  beforeEach(() => {
    mockTransportInstances = [];
    vi.clearAllMocks();
  });

  const baseActiveSnapshot: StateSnapshotPayload = {
    stateRevision: 15,
    meeting: 'active',
    observedAt: '2026-10-25T10:00:00.000Z',
    controls: {
      microphone: {
        value: 'muted',
        capability: 'verified',
        confidence: 'confirmed',
      },
      camera: {
        value: 'off',
        capability: 'verified',
        confidence: 'confirmed',
      },
      hand: {
        value: 'lowered',
        capability: 'verified',
        confidence: 'confirmed',
      },
    },
    connection: 'connected',
  };

  it('sendReaction dispatches command with valid reaction, messageId, and correlationId', () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot(baseActiveSnapshot);
    });

    act(() => {
      result.current.sendReaction(ReactionTypes.Like);
    });

    expect(transportClient.sendReaction).toHaveBeenCalledTimes(1);
    expect(transportClient.sendReaction).toHaveBeenCalledWith(
      ReactionTypes.Like,
      expect.any(String),
      expect.any(String)
    );

    // Cooldown is initiated and last sent reaction recorded
    expect(result.current.reactionCooldownRemainingMs).toBeGreaterThan(0);
    expect(result.current.lastSentReaction).toBe(ReactionTypes.Like);

    // Revision and core control states remain untouched
    expect(result.current.state.stateRevision).toBe(15);
    expect(result.current.state.controls.microphone.value).toBe('muted');
    expect(result.current.state.controls.camera.value).toBe('off');
    expect(result.current.state.controls.hand.value).toBe('lowered');
  });

  it('Anti-spam: drops duplicate or rapid reaction clicks during 2000ms cooldown window', () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot(baseActiveSnapshot);
    });

    // 1st reaction
    act(() => {
      result.current.sendReaction(ReactionTypes.Love);
    });
    expect(transportClient.sendReaction).toHaveBeenCalledTimes(1);
    expect(result.current.lastSentReaction).toBe(ReactionTypes.Love);

    // Immediate 2nd reaction tap should be dropped
    act(() => {
      result.current.sendReaction(ReactionTypes.Applause);
    });
    expect(transportClient.sendReaction).toHaveBeenCalledTimes(1);
  });

  it('drops sendReaction when not connected or no active meeting', () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    // Case 1: not connected
    act(() => {
      transportClient.callbacks.onStatusChange('disconnected');
    });
    act(() => {
      result.current.sendReaction(ReactionTypes.Laugh);
    });
    expect(transportClient.sendReaction).not.toHaveBeenCalled();

    // Case 2: connected, but meeting: 'none'
    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot({
        ...baseActiveSnapshot,
        meeting: 'none',
      });
    });
    act(() => {
      result.current.sendReaction(ReactionTypes.Surprise);
    });
    expect(transportClient.sendReaction).not.toHaveBeenCalled();
  });

  it('updates errorMessage when host rejects reaction with POLICY_RESTRICTED', () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot(baseActiveSnapshot);
    });

    act(() => {
      result.current.sendReaction(ReactionTypes.Applause);
    });

    // Simulate host rejection with POLICY_RESTRICTED
    act(() => {
      transportClient.callbacks.onCommandResult({
        status: 'rejected',
        errorCode: 'POLICY_RESTRICTED',
        reason: 'Live reactions are disabled by meeting policy.',
      });
    });

    expect(result.current.state.errorMessage).toBe('Live reactions are disabled by meeting policy.');
  });

  it('updates errorMessage when host rejects reaction with RATE_LIMITED', () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot(baseActiveSnapshot);
    });

    act(() => {
      result.current.sendReaction(ReactionTypes.Laugh);
    });

    // Simulate host rejection with RATE_LIMITED
    act(() => {
      transportClient.callbacks.onCommandResult({
        status: 'rejected',
        errorCode: 'RATE_LIMITED',
        reason: 'Device reaction cooldown active. Please wait 1200ms.',
      });
    });

    expect(result.current.state.errorMessage).toBe('Device reaction cooldown active. Please wait 1200ms.');
  });
});
