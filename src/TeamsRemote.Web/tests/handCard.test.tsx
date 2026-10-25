import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import type { ControlSnapshotItem } from '../src/types';

describe('Phase 5A Raise Hand Feasibility Spike: ControlCard', () => {
  const handLoweredObserveOnly: ControlSnapshotItem = {
    value: 'lowered',
    capability: 'observe-only',
    confidence: 'confirmed',
  };

  const handRaisedObserveOnly: ControlSnapshotItem = {
    value: 'raised',
    capability: 'observe-only',
    confidence: 'confirmed',
  };

  const handUnavailable: ControlSnapshotItem = {
    value: 'unknown',
    capability: 'unavailable',
    confidence: 'unknown',
  };

  it('renders "Hand Lowered" with confirmed confidence badge, observe-only badge, and Phase 5A observation subtext', () => {
    render(<ControlCard type="hand" snapshot={handLoweredObserveOnly} isConnected={true} />);

    expect(screen.getByText('Raise Hand')).toBeInTheDocument();
    expect(screen.getByText('Hand Lowered')).toBeInTheDocument();
    expect(screen.getByText('CONFIRMED')).toBeInTheDocument();
    expect(screen.getByText('Observe-Only')).toBeInTheDocument();
    expect(
      screen.getByText('Raise hand observation active (Phase 5A spike) — control disabled')
    ).toBeInTheDocument();

    const card = screen.getByTestId('control-card-hand');
    expect(card.className).toContain('theme-lowered');
  });

  it('passively flips to "Hand Raised" with theme-raised styling when snapshot value becomes raised', () => {
    const { rerender } = render(
      <ControlCard type="hand" snapshot={handLoweredObserveOnly} isConnected={true} />
    );
    expect(screen.getByText('Hand Lowered')).toBeInTheDocument();

    rerender(<ControlCard type="hand" snapshot={handRaisedObserveOnly} isConnected={true} />);
    expect(screen.getByText('Hand Raised')).toBeInTheDocument();

    const card = screen.getByTestId('control-card-hand');
    expect(card.className).toContain('theme-raised');
  });

  it('CRITICAL: Raise Hand button remains permanently disabled in all Phase 5A cases', () => {
    const actionSpy = vi.fn();
    render(
      <ControlCard
        type="hand"
        snapshot={handLoweredObserveOnly}
        isConnected={true}
        onAction={actionSpy}
      />
    );

    const button = screen.getByTestId('hand-action-locked');
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).toHaveTextContent('Not enabled yet');

    fireEvent.click(button);
    expect(actionSpy).not.toHaveBeenCalled();
  });

  it('CRITICAL: Raise Hand button also remains permanently disabled when hand is raised', () => {
    const actionSpy = vi.fn();
    render(
      <ControlCard
        type="hand"
        snapshot={handRaisedObserveOnly}
        isConnected={true}
        onAction={actionSpy}
      />
    );

    const button = screen.getByTestId('hand-action-locked');
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).toHaveTextContent('Not enabled yet');

    fireEvent.click(button);
    expect(actionSpy).not.toHaveBeenCalled();
  });

  it('renders "State Unknown" and unavailable subtext when Hand is unavailable', () => {
    render(<ControlCard type="hand" snapshot={handUnavailable} isConnected={true} />);

    expect(screen.getByText('State Unknown')).toBeInTheDocument();
    expect(screen.getByText('Control unavailable in current Teams window')).toBeInTheDocument();

    const card = screen.getByTestId('control-card-hand');
    expect(card.className).toContain('theme-disabled');
  });

  it('renders "Disconnected from relay" when isConnected is false', () => {
    render(<ControlCard type="hand" snapshot={handLoweredObserveOnly} isConnected={false} />);

    expect(screen.getByText('Disconnected from relay')).toBeInTheDocument();

    const card = screen.getByTestId('control-card-hand');
    expect(card.className).toContain('theme-disabled');
  });
});
