import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useRemoteTransport } from '../src/hooks/useRemoteTransport';
import type { StateSnapshotPayload } from '../src/types';

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
    sendCommand = vi.fn();
    setCredentials = vi.fn();

    constructor(callbacks: any) {
      this.callbacks = callbacks;
      mockTransportInstances.push(this);
    }
  }
  return { TransportClient: MockTransportClient };
});

describe('Phase 5B Remote Raise Hand Control Integration Flow', () => {
  beforeEach(() => {
    mockTransportInstances = [];
    vi.clearAllMocks();
  });

  const baseHandSnapshot: StateSnapshotPayload = {
    stateRevision: 10,
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
        capability: 'observe-only',
        confidence: 'confirmed',
      },
    },
  };

  it('sendSetHand dispatches explicit "raised" command with exact stateRevision', async () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    // Establish connection and state snapshot
    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot(baseHandSnapshot);
    });

    expect(result.current.state.controls.hand.value).toBe('lowered');
    expect(result.current.state.controls.hand.confidence).toBe('confirmed');

    // Trigger Raise Hand
    act(() => {
      result.current.setHandState('raised');
    });

    expect(transportClient.sendSetHand).toHaveBeenCalledWith(
      'raised',
      10,
      expect.any(String),
      expect.any(String)
    );
    expect(result.current.isHandActionPending).toBe(true);
    expect(result.current.state.controls.hand.confidence).toBe('pending');
  });

  it('sendSetHand dispatches explicit "lowered" command when hand is raised', async () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    const handRaisedSnapshot: StateSnapshotPayload = {
      ...baseHandSnapshot,
      stateRevision: 11,
      controls: {
        ...baseHandSnapshot.controls,
        hand: {
          value: 'raised',
          capability: 'verified',
          confidence: 'confirmed',
        },
      },
    };

    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot(handRaisedSnapshot);
    });

    // Trigger Lower Hand
    act(() => {
      result.current.setHandState('lowered');
    });

    expect(transportClient.sendSetHand).toHaveBeenCalledWith(
      'lowered',
      11,
      expect.any(String),
      expect.any(String)
    );
    expect(result.current.isHandActionPending).toBe(true);
    expect(result.current.state.controls.hand.confidence).toBe('pending');
  });

  it('Anti-double-tap: ignores duplicate setHandState calls while an action is already in flight', async () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot(baseHandSnapshot);
    });

    // First call
    act(() => {
      result.current.setHandState('raised');
    });
    expect(transportClient.sendSetHand).toHaveBeenCalledTimes(1);

    // Rapid double-tap
    act(() => {
      result.current.setHandState('raised');
    });
    expect(transportClient.sendSetHand).toHaveBeenCalledTimes(1); // Dropped!
  });

  it('clears isHandActionPending when fresh snapshot arrives', async () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transportClient = mockTransportInstances[0];

    act(() => {
      transportClient.callbacks.onStatusChange('connected');
      transportClient.callbacks.onStateSnapshot(baseHandSnapshot);
    });

    act(() => {
      result.current.setHandState('raised');
    });
    expect(result.current.isHandActionPending).toBe(true);

    // Companion pushes confirmed state snapshot
    const updatedSnapshot: StateSnapshotPayload = {
      ...baseHandSnapshot,
      stateRevision: 11,
      controls: {
        ...baseHandSnapshot.controls,
        hand: {
          value: 'raised',
          capability: 'verified',
          confidence: 'confirmed',
        },
      },
    };

    act(() => {
      transportClient.callbacks.onStateSnapshot(updatedSnapshot);
    });

    expect(result.current.isHandActionPending).toBe(false);
    expect(result.current.state.controls.hand.value).toBe('raised');
    expect(result.current.state.controls.hand.capability).toBe('verified');
    expect(result.current.state.controls.hand.confidence).toBe('confirmed');
  });
});
