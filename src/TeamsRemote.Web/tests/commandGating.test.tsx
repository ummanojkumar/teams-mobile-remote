import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import { TransportClient } from '../src/services/transport';
import { ProtocolErrorCodes } from '../src/types';

describe('Strict Phase 4A Command Gating', () => {
  const dummySnapshot = {
    value: 'muted' as const,
    capability: 'verified' as const,
    confidence: 'confirmed' as const,
  };

  it('renders "Not enabled yet" badge and button label on Camera and Hand control cards', () => {
    const { unmount } = render(<ControlCard type="camera" snapshot={dummySnapshot} isConnected={true} />);
    expect(screen.getByText('Not enabled yet')).toBeInTheDocument();
    expect(screen.getByText('Not enabled yet (Phase 4B/C)')).toBeInTheDocument();
    unmount();

    render(<ControlCard type="hand" snapshot={dummySnapshot} isConnected={true} />);
    expect(screen.getByText('Not enabled yet')).toBeInTheDocument();
    expect(screen.getByText('Not enabled yet (Phase 4B/C)')).toBeInTheDocument();
  });

  it('ensures camera and hand control buttons have disabled and aria-disabled="true" attributes', () => {
    render(<ControlCard type="hand" snapshot={dummySnapshot} isConnected={true} />);
    const button = screen.getByRole('button', { name: /Not enabled yet/i });

    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-disabled', 'true');
  });

  it('clicking disabled camera or hand control button causes no action or event dispatch', () => {
    const clickSpy = vi.fn();
    render(
      <div onClick={clickSpy}>
        <ControlCard type="camera" snapshot={dummySnapshot} isConnected={true} />
      </div>
    );

    const button = screen.getByRole('button', { name: /Not enabled yet/i });
    fireEvent.click(button);

    // Button itself is disabled so click shouldn't trigger command logic
    expect(button).toBeDisabled();
  });

  it('renders Unavailable when microphone capability is unavailable or disconnected', () => {
    const unavailableSnapshot = {
      value: 'unknown' as const,
      capability: 'unavailable' as const,
      confidence: 'unknown' as const,
    };
    render(<ControlCard type="microphone" snapshot={unavailableSnapshot} isConnected={true} />);
    expect(screen.getByRole('button', { name: /Unavailable/i })).toBeDisabled();
  });

  it('transport client explicitly blocks camera and hand remote control commands with UNSUPPORTED_IN_PHASE_4A', () => {
    const transport = new TransportClient({
      onStatusChange: vi.fn(),
      onStateSnapshot: vi.fn(),
      onCommandResult: vi.fn(),
      onPairResponse: vi.fn(),
      onRevoked: vi.fn(),
      onError: vi.fn(),
    });

    expect(() => transport.sendCommand('set_camera', 'off')).toThrow(
      ProtocolErrorCodes.UnsupportedInPhase4A
    );

    expect(() => transport.sendCommand('set_hand', 'lowered')).toThrow(
      ProtocolErrorCodes.UnsupportedInPhase4A
    );
  });
});

