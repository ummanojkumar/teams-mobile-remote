import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import type { ControlSnapshotItem } from '../src/types';

describe('Phase 4C Remote Camera Control: ControlCard', () => {
  const cameraOffActionEligible: ControlSnapshotItem = {
    value: 'off',
    capability: 'observe-only',
    confidence: 'confirmed',
  };

  const cameraOnActionEligible: ControlSnapshotItem = {
    value: 'on',
    capability: 'observe-only',
    confidence: 'confirmed',
  };

  const cameraOnVerified: ControlSnapshotItem = {
    value: 'on',
    capability: 'verified',
    confidence: 'confirmed',
  };

  it('renders "Camera Off" with confirmed confidence badge, observe-only capability badge, and action-eligible subtext', () => {
    render(<ControlCard type="camera" snapshot={cameraOffActionEligible} isConnected={true} />);

    expect(screen.getByText('Camera')).toBeInTheDocument();
    expect(screen.getByText('Camera Off')).toBeInTheDocument();
    expect(screen.getByText('CONFIRMED')).toBeInTheDocument();
    expect(screen.getByText('Observe-Only')).toBeInTheDocument();
    expect(
      screen.getByText('Camera action-eligible — tap to turn on and verify control')
    ).toBeInTheDocument();
  });

  it('passively flips to "Camera On" with theme-on styling when snapshot value is on', () => {
    const { rerender } = render(
      <ControlCard type="camera" snapshot={cameraOffActionEligible} isConnected={true} />
    );
    expect(screen.getByText('Camera Off')).toBeInTheDocument();

    rerender(<ControlCard type="camera" snapshot={cameraOnActionEligible} isConnected={true} />);
    expect(screen.getByText('Camera On')).toBeInTheDocument();

    const card = screen.getByTestId('control-card-camera');
    expect(card.className).toContain('theme-on');
  });

  it('Camera card button is actionable when observe-only and confirmed (action-eligible)', () => {
    const actionSpy = vi.fn();
    render(
      <ControlCard
        type="camera"
        snapshot={cameraOffActionEligible}
        isConnected={true}
        onCameraAction={actionSpy}
      />
    );

    const button = screen.getByTestId('camera-action-on');
    expect(button).not.toBeDisabled();
    expect(button).toHaveTextContent('Turn Camera On');

    fireEvent.click(button);
    expect(actionSpy).toHaveBeenCalledWith('on');
  });

  it('When camera is On, button displays "Turn Camera Off" and invokes onCameraAction("off")', () => {
    const actionSpy = vi.fn();
    render(
      <ControlCard
        type="camera"
        snapshot={cameraOnActionEligible}
        isConnected={true}
        onCameraAction={actionSpy}
      />
    );

    const button = screen.getByTestId('camera-action-off');
    expect(button).not.toBeDisabled();
    expect(button).toHaveTextContent('Turn Camera Off');

    fireEvent.click(button);
    expect(actionSpy).toHaveBeenCalledWith('off');
  });

  it('promotes capability badge to "Remote Active" when capability is verified', () => {
    render(<ControlCard type="camera" snapshot={cameraOnVerified} isConnected={true} />);

    expect(screen.getByText('Remote Active')).toBeInTheDocument();
    expect(screen.queryByText('Observe-Only')).not.toBeInTheDocument();
    expect(screen.getByText('Tap below to turn camera off')).toBeInTheDocument();
  });

  it('displays pending spinner and aria-busy="true" while camera action is pending', () => {
    render(
      <ControlCard
        type="camera"
        snapshot={cameraOffActionEligible}
        isConnected={true}
        isActionPending={true}
      />
    );

    const button = screen.getByTestId('camera-action-pending');
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button).toHaveTextContent('Turning On...');
  });

  it('renders State Unknown when camera state is unavailable or unknown', () => {
    const cameraUnavailable: ControlSnapshotItem = {
      value: 'unknown',
      capability: 'unavailable',
      confidence: 'unknown',
    };

    render(<ControlCard type="camera" snapshot={cameraUnavailable} isConnected={true} />);

    expect(screen.getByText('State Unknown')).toBeInTheDocument();
    expect(screen.getByText('UNKNOWN')).toBeInTheDocument();
    expect(screen.getByText('Control unavailable in current Teams window')).toBeInTheDocument();
  });
});

