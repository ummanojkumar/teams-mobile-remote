import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useRemoteTransport } from '../src/hooks/useRemoteTransport';
import type { ParsedChallenge } from '../src/services/pairing';

let mockTransportInstances: any[] = [];

vi.mock('../src/services/transport', () => {
  class MockTransportClient {
    callbacks: any;
    status = 'connecting';
    connect = vi.fn((_url: string) => {
      // stays connecting unless changed
    });
    disconnect = vi.fn();
    sendPairRequest = vi.fn();
    queryState = vi.fn();
    setCredentials = vi.fn();
    getRelayUrl = vi.fn(() => 'ws://192.168.1.100:5100/ws/client');
    getStatus = vi.fn(() => this.status);

    constructor(callbacks: any) {
      this.callbacks = callbacks;
      mockTransportInstances.push(this);
    }
  }
  return { TransportClient: MockTransportClient };
});

describe('Pairing Timeouts and Error Handling', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    mockTransportInstances = [];
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  const testChallenge: ParsedChallenge = {
    challengeId: '11111111-2222-3333-4444-555555555555',
    shortCode: '123-456',
    relayUrl: 'ws://192.168.1.100:5100/ws/client',
    expiresAt: new Date(Date.now() + 60000),
  };

  it('times out after 10 seconds if relay cannot be reached, setting clear error', async () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transport = mockTransportInstances[0];

    act(() => {
      result.current.startPairing(testChallenge);
    });

    expect(result.current.state.pairing).toBe('awaiting_approval');
    expect(result.current.state.matchingCode).toBe('123-456');

    // Fast-forward 10 seconds while transport stays connecting
    act(() => {
      vi.advanceTimersByTime(10000);
    });

    expect(transport.disconnect).toHaveBeenCalled();
    expect(result.current.state.pairing).toBe('unpaired');
    expect(result.current.state.errorMessage).toContain('Cannot reach relay at ws://192.168.1.100:5100/ws/client');
    expect(result.current.state.errorMessage).toContain('Ensure phone and laptop are on the same Wi-Fi network');
  });

  it('connects to relay, sends pair request, and times out after 60 seconds if approval is never given', async () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transport = mockTransportInstances[0];

    act(() => {
      result.current.startPairing(testChallenge);
    });

    // Simulate socket opening within 500ms
    act(() => {
      transport.status = 'connected';
      vi.advanceTimersByTime(500);
    });

    expect(transport.sendPairRequest).toHaveBeenCalledWith(
      testChallenge.challengeId,
      testChallenge.shortCode,
      expect.any(String)
    );
    expect(result.current.state.pairing).toBe('awaiting_approval');

    // Fast-forward 60 seconds without companion approval
    act(() => {
      vi.advanceTimersByTime(60000);
    });

    expect(transport.disconnect).toHaveBeenCalled();
    expect(result.current.state.pairing).toBe('expired');
    expect(result.current.state.errorMessage).toContain('Pairing request timed out');
  });

  it('cancels timeouts and transitions to paired when companion approves within window', async () => {
    const { result } = renderHook(() => useRemoteTransport());
    const transport = mockTransportInstances[0];

    act(() => {
      result.current.startPairing(testChallenge);
    });

    // Socket connects
    act(() => {
      transport.status = 'connected';
      vi.advanceTimersByTime(200);
    });

    expect(transport.sendPairRequest).toHaveBeenCalled();

    // Companion approves
    act(() => {
      transport.callbacks.onPairResponse({
        challengeId: testChallenge.challengeId,
        approved: true,
        deviceId: '099a80e4-9ee0-47bf-93f8-580796e62ee7',
        sessionToken: 'test-token',
      });
    });

    expect(result.current.state.pairing).toBe('paired');
    expect(result.current.state.errorMessage).toBeNull();

    // Advancing past 60s should NOT expire since it's already paired
    act(() => {
      vi.advanceTimersByTime(70000);
    });

    expect(result.current.state.pairing).toBe('paired');
  });
});
