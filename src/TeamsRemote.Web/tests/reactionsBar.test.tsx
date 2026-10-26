import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ReactionsBar } from '../src/components/ReactionsBar';
import { ReactionTypes } from '../src/types/protocol';

describe('Phase 6B Live Reactions: ReactionsBar Component', () => {
  it('renders all five reaction buttons with correct labels and emojis', () => {
    const handleSendReaction = vi.fn();

    render(
      <ReactionsBar
        isConnected={true}
        isMeetingActive={true}
        cooldownRemainingMs={0}
        lastSentReaction={null}
        onSendReaction={handleSendReaction}
      />
    );

    expect(screen.getByRole('region', { name: 'Meeting Reactions' })).toBeInTheDocument();
    expect(screen.getByText('Live Reactions')).toBeInTheDocument();

    const expectedReactions = [
      { id: ReactionTypes.Like, label: 'Like', emoji: '👍' },
      { id: ReactionTypes.Love, label: 'Love', emoji: '❤️' },
      { id: ReactionTypes.Applause, label: 'Applause', emoji: '👏' },
      { id: ReactionTypes.Laugh, label: 'Laugh', emoji: '😂' },
      { id: ReactionTypes.Surprise, label: 'Surprise', emoji: '😮' },
    ];

    expectedReactions.forEach(({ id, label, emoji }) => {
      const btn = screen.getByRole('button', { name: `Send ${label} reaction` });
      expect(btn).toBeInTheDocument();
      expect(btn).not.toBeDisabled();
      expect(btn.querySelector('.reaction-emoji')?.textContent).toBe(emoji);
      expect(btn.querySelector('.reaction-name')?.textContent).toBe(label);
    });
  });

  it('clicking a reaction button invokes onSendReaction callback', () => {
    const handleSendReaction = vi.fn();

    render(
      <ReactionsBar
        isConnected={true}
        isMeetingActive={true}
        cooldownRemainingMs={0}
        lastSentReaction={null}
        onSendReaction={handleSendReaction}
      />
    );

    const loveBtn = screen.getByRole('button', { name: 'Send Love reaction' });
    fireEvent.click(loveBtn);

    expect(handleSendReaction).toHaveBeenCalledTimes(1);
    expect(handleSendReaction).toHaveBeenCalledWith(ReactionTypes.Love);
  });

  it('disables all reaction buttons when not connected or no meeting is active', () => {
    const handleSendReaction = vi.fn();

    const { rerender } = render(
      <ReactionsBar
        isConnected={false}
        isMeetingActive={true}
        cooldownRemainingMs={0}
        lastSentReaction={null}
        onSendReaction={handleSendReaction}
      />
    );

    expect(screen.getByRole('button', { name: 'Send Like reaction' })).toBeDisabled();

    rerender(
      <ReactionsBar
        isConnected={true}
        isMeetingActive={false}
        cooldownRemainingMs={0}
        lastSentReaction={null}
        onSendReaction={handleSendReaction}
      />
    );

    expect(screen.getByRole('button', { name: 'Send Like reaction' })).toBeDisabled();
    expect(
      screen.getByText('Reactions are available when a meeting is active.')
    ).toBeInTheDocument();
  });

  it('displays countdown badge and disables buttons during active cooldown', () => {
    const handleSendReaction = vi.fn();

    render(
      <ReactionsBar
        isConnected={true}
        isMeetingActive={true}
        cooldownRemainingMs={1600}
        lastSentReaction={ReactionTypes.Like}
        onSendReaction={handleSendReaction}
      />
    );

    expect(screen.getByText('1.6s')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send Like reaction' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Send Applause reaction' })).toBeDisabled();
    expect(
      screen.getByText('Cooling down to prevent reaction spam.')
    ).toBeInTheDocument();
  });

  it('displays "Sent!" badge and highlights button for lastSentReaction when not in cooldown', () => {
    const handleSendReaction = vi.fn();

    render(
      <ReactionsBar
        isConnected={true}
        isMeetingActive={true}
        cooldownRemainingMs={0}
        lastSentReaction={ReactionTypes.Laugh}
        onSendReaction={handleSendReaction}
      />
    );

    expect(screen.getByText('Sent!')).toBeInTheDocument();
    const laughBtn = screen.getByRole('button', { name: 'Send Laugh reaction' });
    expect(laughBtn.className).toContain('is-active');
  });
});
