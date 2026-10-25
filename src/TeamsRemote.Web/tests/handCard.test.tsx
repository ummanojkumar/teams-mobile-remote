import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import type { ControlSnapshotItem } from '../src/types';

describe('Phase 5B Remote Raise Hand Control: ControlCard', () => {
  const handLoweredActionEligible: ControlSnapshotItem = {
    value: 'lowered',
    capability: 'observe-only',
    confidence: 'confirmed',
  };

  const handRaisedActionEligible: ControlSnapshotItem = {
    value: 'raised',
    capability: 'observe-only',
    confidence: 'confirmed',
  };

  const handRaisedVerified: ControlSnapshotItem = {
    value: 'raised',
    capability: 'verified',
    confidence: 'confirmed',
  };

  const handUnavailable: ControlSnapshotItem = {
    value: 'unknown',
    capability: 'unavailable',
    confidence: 'unknown',
  };

  it('renders "Hand Lowered" with confirmed confidence badge, observe-only capability badge, and action-eligible subtext', () => {
    render(<ControlCard type="hand" snapshot={handLoweredActionEligible} isConnected={true} />);

    expect(screen.getByTestId('control-card-hand')).toBeInTheDocument();
    expect(screen.getByTestId('hand-action-raise')).toBeInTheDocument();
    expect(screen.getByText('Hand Lowered')).toBeInTheDocument();
    expect(screen.getByText('CONFIRMED')).toBeInTheDocument();
    expect(screen.getByText('Observe-Only')).toBeInTheDocument();
    expect(
      screen.getByText('Raise hand action-eligible — tap to raise and verify control')
    ).toBeInTheDocument();

    const card = screen.getByTestId('control-card-hand');
    expect(card.className).toContain('theme-lowered');
  });

  it('passively flips to "Hand Raised" with theme-raised styling when snapshot value becomes raised', () => {
    const { rerender } = render(
      <ControlCard type="hand" snapshot={handLoweredActionEligible} isConnected={true} />
    );
    expect(screen.getByText('Hand Lowered')).toBeInTheDocument();

    rerender(<ControlCard type="hand" snapshot={handRaisedActionEligible} isConnected={true} />);
    expect(screen.getByText('Hand Raised')).toBeInTheDocument();

    const card = screen.getByTestId('control-card-hand');
    expect(card.className).toContain('theme-raised');
  });

  it('Raise Hand button is actionable when observe-only and confirmed (action-eligible)', () => {
    const actionSpy = vi.fn();
    render(
      <ControlCard
        type="hand"
        snapshot={handLoweredActionEligible}
        isConnected={true}
        onHandAction={actionSpy}
      />
    );

    const button = screen.getByTestId('hand-action-raise');
    expect(button).not.toBeDisabled();
    expect(button).toHaveTextContent('Raise Hand');

    fireEvent.click(button);
    expect(actionSpy).toHaveBeenCalledWith('raised');
  });

  it('When hand is raised, button displays "Lower Hand" and invokes onHandAction("lowered")', () => {
    const actionSpy = vi.fn();
    render(
      <ControlCard
        type="hand"
        snapshot={handRaisedActionEligible}
        isConnected={true}
        onHandAction={actionSpy}
      />
    );

    const button = screen.getByTestId('hand-action-lower');
    expect(button).not.toBeDisabled();
    expect(button).toHaveTextContent('Lower Hand');

    fireEvent.click(button);
    expect(actionSpy).toHaveBeenCalledWith('lowered');
  });

  it('promotes capability badge to "Remote Active" when capability is verified', () => {
    render(<ControlCard type="hand" snapshot={handRaisedVerified} isConnected={true} />);

    expect(screen.getByText('Remote Active')).toBeInTheDocument();
    expect(screen.queryByText('Observe-Only')).not.toBeInTheDocument();
    expect(screen.getByText('Tap below to lower hand')).toBeInTheDocument();
  });

  it('displays pending spinner and aria-busy="true" while hand action is pending', () => {
    render(
      <ControlCard
        type="hand"
        snapshot={handLoweredActionEligible}
        isConnected={true}
        isActionPending={true}
      />
    );

    const pendingBtn = screen.getByTestId('hand-action-pending');
    expect(pendingBtn).toBeDisabled();
    expect(pendingBtn).toHaveAttribute('aria-busy', 'true');
    expect(pendingBtn).toHaveTextContent('Raising Hand...');
  });

  it('renders disabled lock button when state is unknown or unavailable', () => {
    render(<ControlCard type="hand" snapshot={handUnavailable} isConnected={true} />);

    expect(screen.getByText('State Unknown')).toBeInTheDocument();
    expect(screen.getByText('Control unavailable in current Teams window')).toBeInTheDocument();

    const button = screen.getByTestId('hand-action-disabled');
    expect(button).toBeDisabled();
    expect(button).toHaveTextContent('Unavailable');
  });

  it('renders "Disconnected from relay" and disables button when isConnected is false', () => {
    render(<ControlCard type="hand" snapshot={handLoweredActionEligible} isConnected={false} />);

    expect(screen.getByText('Disconnected from relay')).toBeInTheDocument();

    const button = screen.getByTestId('hand-action-disabled');
    expect(button).toBeDisabled();
    expect(button).toHaveTextContent('Disconnected');
  });
});
