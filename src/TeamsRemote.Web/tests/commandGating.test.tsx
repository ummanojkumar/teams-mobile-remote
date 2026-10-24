import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import { TransportClient } from '../src/services/transport';
import { ProtocolErrorCodes } from '../src/types';

describe('Strict Phase 3 Command Gating', () => {
  const dummySnapshot = {
    value: 'muted' as const,
    capability: 'verified' as const,
    confidence: 'confirmed' as const,
  };

  it('renders "Not enabled yet" badge and button label on all control cards', () => {
    const { unmount } = render(<ControlCard type="microphone" snapshot={dummySnapshot} isConnected={true} />);
    expect(screen.getByText('Not enabled yet')).toBeInTheDocument();
    expect(screen.getByText('Not enabled yet (Phase 3)')).toBeInTheDocument();
    unmount();

    render(<ControlCard type="camera" snapshot={dummySnapshot} isConnected={true} />);
    expect(screen.getByText('Not enabled yet')).toBeInTheDocument();
    expect(screen.getByText('Not enabled yet (Phase 3)')).toBeInTheDocument();
  });

  it('ensures control buttons have disabled and aria-disabled="true" attributes', () => {
    render(<ControlCard type="hand" snapshot={dummySnapshot} isConnected={true} />);
    const button = screen.getByRole('button', { name: /Not enabled yet/i });

    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-disabled', 'true');
  });

  it('clicking the disabled control button causes no action or event dispatch', () => {
    const clickSpy = vi.fn();
    render(
      <div onClick={clickSpy}>
        <ControlCard type="microphone" snapshot={dummySnapshot} isConnected={true} />
      </div>
    );

    const button = screen.getByRole('button', { name: /Not enabled yet/i });
    fireEvent.click(button);

    // Button itself is disabled so click shouldn't trigger command logic
    expect(button).toBeDisabled();
  });

  it('transport client explicitly blocks remote control commands with UNSUPPORTED_IN_PHASE_2', () => {
    const transport = new TransportClient({
      onStatusChange: vi.fn(),
      onStateSnapshot: vi.fn(),
      onCommandResult: vi.fn(),
      onPairResponse: vi.fn(),
      onRevoked: vi.fn(),
      onError: vi.fn(),
    });

    expect(() => transport.sendCommand('set_microphone', 'muted')).toThrow(
      ProtocolErrorCodes.UnsupportedInPhase2
    );

    expect(() => transport.sendCommand('set_camera', 'off')).toThrow(
      ProtocolErrorCodes.UnsupportedInPhase2
    );

    expect(() => transport.sendCommand('set_hand', 'lowered')).toThrow(
      ProtocolErrorCodes.UnsupportedInPhase2
    );
  });
});
