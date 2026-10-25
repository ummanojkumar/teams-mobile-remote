import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import type { ControlSnapshotItem } from '../src/types';

describe('Phase 4B Camera Feasibility Spike: ControlCard', () => {
  const cameraOffObserved: ControlSnapshotItem = {
    value: 'off',
    capability: 'observe-only',
    confidence: 'confirmed',
  };

  const cameraOnObserved: ControlSnapshotItem = {
    value: 'on',
    capability: 'observe-only',
    confidence: 'confirmed',
  };

  it('renders "Camera Off" with confirmed confidence badge and observe-only capability badge', () => {
    render(<ControlCard type="camera" snapshot={cameraOffObserved} isConnected={true} />);

    expect(screen.getByText('Camera')).toBeInTheDocument();
    expect(screen.getByText('Camera Off')).toBeInTheDocument();
    expect(screen.getByText('CONFIRMED')).toBeInTheDocument();
    expect(screen.getByText('Observe-Only')).toBeInTheDocument();
    expect(
      screen.getByText('Camera observation active (Phase 4B spike) — control disabled')
    ).toBeInTheDocument();
  });

  it('passively flips to "Camera On" with theme-on styling when snapshot value is on', () => {
    const { rerender } = render(
      <ControlCard type="camera" snapshot={cameraOffObserved} isConnected={true} />
    );
    expect(screen.getByText('Camera Off')).toBeInTheDocument();

    rerender(<ControlCard type="camera" snapshot={cameraOnObserved} isConnected={true} />);
    expect(screen.getByText('Camera On')).toBeInTheDocument();

    const card = screen.getByTestId('control-card-camera');
    expect(card.className).toContain('theme-on');
  });

  it('Camera card button is permanently disabled regardless of confirmed detection success', () => {
    render(<ControlCard type="camera" snapshot={cameraOnObserved} isConnected={true} />);

    const button = screen.getByTestId('camera-action-locked');
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).toHaveTextContent('Not enabled yet (Phase 4B/C)');
  });

  it('clicking Camera button triggers zero actions and dispatches zero commands', () => {
    const actionSpy = vi.fn();
    render(
      <ControlCard
        type="camera"
        snapshot={cameraOnObserved}
        isConnected={true}
        onAction={actionSpy}
      />
    );

    const button = screen.getByTestId('camera-action-locked');
    fireEvent.click(button);

    expect(actionSpy).not.toHaveBeenCalled();
    expect(button).toBeDisabled();
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
