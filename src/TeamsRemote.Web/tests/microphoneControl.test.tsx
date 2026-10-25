import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';

describe('Phase 4A Remote Microphone Control Component', () => {
  it('renders explicit "Unmute" button when microphone is muted and verified', () => {
    const onAction = vi.fn();
    render(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'muted',
          capability: 'verified',
          confidence: 'confirmed',
        }}
        isConnected={true}
        onAction={onAction}
        isActionPending={false}
      />
    );

    const button = screen.getByRole('button', { name: /Unmute/i });
    expect(button).toBeInTheDocument();
    expect(button).not.toBeDisabled();
    expect(button).toHaveAttribute('data-testid', 'microphone-action-unmute');

    fireEvent.click(button);
    expect(onAction).toHaveBeenCalledTimes(1);
    expect(onAction).toHaveBeenCalledWith('unmuted');
  });

  it('renders explicit "Mute" button when microphone is unmuted and verified', () => {
    const onAction = vi.fn();
    render(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'unmuted',
          capability: 'verified',
          confidence: 'confirmed',
        }}
        isConnected={true}
        onAction={onAction}
        isActionPending={false}
      />
    );

    const button = screen.getByRole('button', { name: /Mute/i });
    expect(button).toBeInTheDocument();
    expect(button).not.toBeDisabled();
    expect(button).toHaveAttribute('data-testid', 'microphone-action-mute');

    fireEvent.click(button);
    expect(onAction).toHaveBeenCalledTimes(1);
    expect(onAction).toHaveBeenCalledWith('muted');
  });

  it('enforces anti-double-tap: disables button and shows pending spinner when isActionPending is true', () => {
    const onAction = vi.fn();
    render(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'muted',
          capability: 'verified',
          confidence: 'confirmed',
        }}
        isConnected={true}
        onAction={onAction}
        isActionPending={true}
      />
    );

    const button = screen.getByRole('button', { name: /Unmuting\.\.\./i });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');

    fireEvent.click(button);
    expect(onAction).not.toHaveBeenCalled();
  });

  it('disables control button when capability is degraded or unsupported', () => {
    const onAction = vi.fn();
    render(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'unknown',
          capability: 'unavailable',
          confidence: 'unknown',
        }}
        isConnected={true}
        onAction={onAction}
        isActionPending={false}
      />
    );

    const button = screen.getByRole('button', { name: /Unavailable/i });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-disabled', 'true');
  });

  it('disables control button when disconnected even if snapshot was previously verified', () => {
    const onAction = vi.fn();
    render(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'muted',
          capability: 'verified',
          confidence: 'confirmed',
        }}
        isConnected={false}
        onAction={onAction}
        isActionPending={false}
      />
    );

    const button = screen.getByRole('button', { name: /Disconnected/i });
    expect(button).toBeDisabled();
  });

  it('passively flips button from Unmute to Mute when snapshot changes from muted to unmuted', () => {
    const { rerender } = render(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'muted',
          capability: 'verified',
          confidence: 'confirmed',
        }}
        isConnected={true}
        isActionPending={false}
      />
    );

    expect(screen.getByRole('button', { name: /Unmute/i })).toBeInTheDocument();

    // Passive snapshot arrives reflecting user toggling Teams desktop directly
    rerender(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'unmuted',
          capability: 'verified',
          confidence: 'confirmed',
        }}
        isConnected={true}
        isActionPending={false}
      />
    );

    expect(screen.getByRole('button', { name: /Mute/i })).toBeInTheDocument();
  });

  it('passively disables button and shows Unavailable when Teams closes or meeting disappears', () => {
    const { rerender } = render(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'muted',
          capability: 'verified',
          confidence: 'confirmed',
        }}
        isConnected={true}
        isActionPending={false}
      />
    );

    expect(screen.getByRole('button', { name: /Unmute/i })).toBeInTheDocument();

    // Passive snapshot arrives when Teams closes or meeting surface ends
    rerender(
      <ControlCard
        type="microphone"
        snapshot={{
          value: 'unknown',
          capability: 'unavailable',
          confidence: 'unknown',
        }}
        isConnected={true}
        isActionPending={false}
      />
    );

    const button = screen.getByRole('button', { name: /Unavailable/i });
    expect(button).toBeInTheDocument();
    expect(button).toBeDisabled();
  });
});
