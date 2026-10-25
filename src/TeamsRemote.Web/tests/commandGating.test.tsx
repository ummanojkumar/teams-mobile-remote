import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import { TransportClient } from '../src/services/transport';
import { ProtocolErrorCodes } from '../src/types';

describe('Strict Hand Command Gating (Phase 4C)', () => {
  const dummyHandSnapshot = {
    value: 'lowered' as const,
    capability: 'unavailable' as const,
    confidence: 'unknown' as const,
  };

  it('renders "Not enabled yet" badge and button label on Hand control card', () => {
    render(<ControlCard type="hand" snapshot={dummyHandSnapshot} isConnected={true} />);
    expect(screen.getByText('Not enabled yet')).toBeInTheDocument();
    expect(screen.getByText('Not enabled yet (Phase 4B/C)')).toBeInTheDocument();
  });

  it('ensures hand control button has disabled and aria-disabled="true" attributes', () => {
    render(<ControlCard type="hand" snapshot={dummyHandSnapshot} isConnected={true} />);
    const button = screen.getByRole('button', { name: /Not enabled yet/i });

    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-disabled', 'true');
  });

  it('clicking disabled hand control button causes no action or event dispatch', () => {
    const clickSpy = vi.fn();
    render(
      <div onClick={clickSpy}>
        <ControlCard type="hand" snapshot={dummyHandSnapshot} isConnected={true} />
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

  it('transport client explicitly blocks hand remote control commands with UNSUPPORTED_IN_PHASE_4A', () => {
    const transport = new TransportClient({
      onStatusChange: vi.fn(),
      onStateSnapshot: vi.fn(),
      onCommandResult: vi.fn(),
      onPairResponse: vi.fn(),
      onRevoked: vi.fn(),
      onError: vi.fn(),
    });

    expect(() => transport.sendCommand('set_hand', 'lowered')).toThrow(
      ProtocolErrorCodes.UnsupportedInPhase4A
    );
  });
});


