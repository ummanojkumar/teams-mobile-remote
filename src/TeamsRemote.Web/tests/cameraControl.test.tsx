import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, renderHook, act } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import { CameraSafetyModal } from '../src/components/CameraSafetyModal';
import { useRemoteTransport } from '../src/hooks/useRemoteTransport';
import { TransportClient } from '../src/services/transport';
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
    sendCommand = vi.fn();
    setCredentials = vi.fn();

    constructor(callbacks: any) {
      this.callbacks = callbacks;
      mockTransportInstances.push(this);
    }
  }
  return { TransportClient: MockTransportClient };
});

describe('Phase 4C Remote Camera Control Component & Safety Flow', () => {
  beforeEach(() => {
    sessionStorage.clear();
    mockTransportInstances = [];
    vi.clearAllMocks();
  });

  describe('CameraSafetyModal Component', () => {
    it('renders nothing when isOpen is false', () => {
      const { container } = render(
        <CameraSafetyModal isOpen={false} onConfirm={vi.fn()} onCancel={vi.fn()} />
      );
      expect(container.firstChild).toBeNull();
    });

    it('renders confirmation modal with clear warnings when isOpen is true', () => {
      render(
        <CameraSafetyModal isOpen={true} onConfirm={vi.fn()} onCancel={vi.fn()} />
      );

      expect(screen.getByText('Enable Remote Camera?')).toBeInTheDocument();
      expect(screen.getByText(/Your laptop camera will be turned on remotely/i)).toBeInTheDocument();
      expect(screen.getByText(/Make sure your camera is uncovered and positioned appropriately/i)).toBeInTheDocument();
      expect(screen.getByTestId('camera-safety-cancel')).toBeInTheDocument();
      expect(screen.getByTestId('camera-safety-confirm')).toBeInTheDocument();
    });

    it('invokes onCancel when Cancel button is clicked', () => {
      const onCancel = vi.fn();
      render(
        <CameraSafetyModal isOpen={true} onConfirm={vi.fn()} onCancel={onCancel} />
      );

      fireEvent.click(screen.getByTestId('camera-safety-cancel'));
      expect(onCancel).toHaveBeenCalledTimes(1);
    });

    it('invokes onConfirm when Turn Camera On button is clicked', () => {
      const onConfirm = vi.fn();
      render(
        <CameraSafetyModal isOpen={true} onConfirm={onConfirm} onCancel={vi.fn()} />
      );

      fireEvent.click(screen.getByTestId('camera-safety-confirm'));
      expect(onConfirm).toHaveBeenCalledTimes(1);
    });
  });

  describe('ControlCard Camera Rendering & Gating', () => {
    it('renders explicit "Turn Camera On" button when camera is off and action-eligible', () => {
      const onCameraAction = vi.fn();
      render(
        <ControlCard
          type="camera"
          snapshot={{
            value: 'off',
            capability: 'observe-only',
            confidence: 'confirmed',
          }}
          isConnected={true}
          onCameraAction={onCameraAction}
          isActionPending={false}
        />
      );

      const button = screen.getByRole('button', { name: /Turn Camera On/i });
      expect(button).toBeInTheDocument();
      expect(button).not.toBeDisabled();
      expect(button).toHaveAttribute('data-testid', 'camera-action-on');

      fireEvent.click(button);
      expect(onCameraAction).toHaveBeenCalledWith('on');
    });

    it('renders explicit "Turn Camera Off" button when camera is on and verified', () => {
      const onCameraAction = vi.fn();
      render(
        <ControlCard
          type="camera"
          snapshot={{
            value: 'on',
            capability: 'verified',
            confidence: 'confirmed',
          }}
          isConnected={true}
          onCameraAction={onCameraAction}
          isActionPending={false}
        />
      );

      const button = screen.getByRole('button', { name: /Turn Camera Off/i });
      expect(button).toBeInTheDocument();
      expect(button).not.toBeDisabled();
      expect(button).toHaveAttribute('data-testid', 'camera-action-off');

      fireEvent.click(button);
      expect(onCameraAction).toHaveBeenCalledWith('off');
    });

    it('enforces anti-double-tap: disables button with aria-busy="true" during pending camera action', () => {
      const onCameraAction = vi.fn();
      render(
        <ControlCard
          type="camera"
          snapshot={{
            value: 'off',
            capability: 'observe-only',
            confidence: 'confirmed',
          }}
          isConnected={true}
          onCameraAction={onCameraAction}
          isActionPending={true}
        />
      );

      const button = screen.getByRole('button', { name: /Turning On\.\.\./i });
      expect(button).toBeDisabled();
      expect(button).toHaveAttribute('aria-busy', 'true');

      fireEvent.click(button);
      expect(onCameraAction).not.toHaveBeenCalled();
    });

    it('disables camera button when disconnected', () => {
      render(
        <ControlCard
          type="camera"
          snapshot={{
            value: 'off',
            capability: 'verified',
            confidence: 'confirmed',
          }}
          isConnected={false}
          isActionPending={false}
        />
      );

      const button = screen.getByRole('button', { name: /Disconnected/i });
      expect(button).toBeDisabled();
    });
  });

  describe('useRemoteTransport Camera Workflow', () => {
    it('prompts first camera-on action with safety modal and dispatches command upon confirmation', () => {
      const { result } = renderHook(() => useRemoteTransport());
      const transportClient = mockTransportInstances[mockTransportInstances.length - 1];

      // Set connected and paired state with active meeting snapshot
      const activeSnapshot: StateSnapshotPayload = {
        stateRevision: 10,
        meeting: 'in_meeting',
        observedAt: new Date().toISOString(),
        controls: {
          microphone: { value: 'muted', capability: 'verified', confidence: 'confirmed' },
          camera: { value: 'off', capability: 'observe-only', confidence: 'confirmed' },
          hand: { value: 'lowered', capability: 'unavailable', confidence: 'unknown' },
        },
      };

      act(() => {
        transportClient.callbacks.onStatusChange('connected');
        transportClient.callbacks.onStateSnapshot(activeSnapshot);
      });

      // Calling setCameraState('on') for the first time should trigger safety modal
      act(() => {
        result.current.setCameraState('on');
      });

      expect(result.current.showCameraSafetyModal).toBe(true);
      expect(transportClient.sendSetCamera).not.toHaveBeenCalled();

      // Confirm modal
      act(() => {
        result.current.confirmCameraSafety();
      });

      expect(result.current.showCameraSafetyModal).toBe(false);
      expect(transportClient.sendSetCamera).toHaveBeenCalledWith(
        'on',
        10,
        expect.any(String),
        expect.any(String)
      );
      expect(result.current.isCameraActionPending).toBe(true);
      expect(sessionStorage.getItem('teams_remote_camera_safety_confirmed')).toBe('true');
    });

    it('cancelling safety modal sends zero commands and resets modal state', () => {
      const { result } = renderHook(() => useRemoteTransport());
      const transportClient = mockTransportInstances[mockTransportInstances.length - 1];

      act(() => {
        result.current.setCameraState('on');
      });

      expect(result.current.showCameraSafetyModal).toBe(true);

      act(() => {
        result.current.cancelCameraSafety();
      });

      expect(result.current.showCameraSafetyModal).toBe(false);
      expect(transportClient.sendSetCamera).not.toHaveBeenCalled();
      expect(result.current.isCameraActionPending).toBe(false);
      expect(sessionStorage.getItem('teams_remote_camera_safety_confirmed')).toBeNull();
    });

    it('subsequent camera-on action in same session bypasses modal and dispatches directly', () => {
      sessionStorage.setItem('teams_remote_camera_safety_confirmed', 'true');
      const { result } = renderHook(() => useRemoteTransport());
      const transportClient = mockTransportInstances[mockTransportInstances.length - 1];

      const activeSnapshot: StateSnapshotPayload = {
        stateRevision: 15,
        meeting: 'in_meeting',
        observedAt: new Date().toISOString(),
        controls: {
          microphone: { value: 'muted', capability: 'verified', confidence: 'confirmed' },
          camera: { value: 'off', capability: 'observe-only', confidence: 'confirmed' },
          hand: { value: 'lowered', capability: 'unavailable', confidence: 'unknown' },
        },
      };

      act(() => {
        transportClient.callbacks.onStatusChange('connected');
        transportClient.callbacks.onStateSnapshot(activeSnapshot);
      });

      act(() => {
        result.current.setCameraState('on');
      });

      expect(result.current.showCameraSafetyModal).toBe(false);
      expect(transportClient.sendSetCamera).toHaveBeenCalledWith(
        'on',
        15,
        expect.any(String),
        expect.any(String)
      );
      expect(result.current.isCameraActionPending).toBe(true);
    });

    it('"Turn Camera Off" action dispatches immediately without showing safety modal', () => {
      const { result } = renderHook(() => useRemoteTransport());
      const transportClient = mockTransportInstances[mockTransportInstances.length - 1];

      const activeSnapshot: StateSnapshotPayload = {
        stateRevision: 20,
        meeting: 'in_meeting',
        observedAt: new Date().toISOString(),
        controls: {
          microphone: { value: 'muted', capability: 'verified', confidence: 'confirmed' },
          camera: { value: 'on', capability: 'verified', confidence: 'confirmed' },
          hand: { value: 'lowered', capability: 'unavailable', confidence: 'unknown' },
        },
      };

      act(() => {
        transportClient.callbacks.onStatusChange('connected');
        transportClient.callbacks.onStateSnapshot(activeSnapshot);
      });

      act(() => {
        result.current.setCameraState('off');
      });

      expect(result.current.showCameraSafetyModal).toBe(false);
      expect(transportClient.sendSetCamera).toHaveBeenCalledWith(
        'off',
        20,
        expect.any(String),
        expect.any(String)
      );
      expect(result.current.isCameraActionPending).toBe(true);
    });

    it('unpair resets camera safety confirmation from sessionStorage', () => {
      sessionStorage.setItem('teams_remote_camera_safety_confirmed', 'true');
      const { result } = renderHook(() => useRemoteTransport());

      act(() => {
        result.current.unpair();
      });

      expect(sessionStorage.getItem('teams_remote_camera_safety_confirmed')).toBeNull();
    });
  });
});
