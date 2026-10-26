import React from 'react';
import { ReactionTypes, type ReactionType } from '../types/protocol';

interface ReactionItem {
  id: ReactionType;
  label: string;
  emoji: string;
}

const REACTIONS: readonly ReactionItem[] = [
  { id: ReactionTypes.Like, label: 'Like', emoji: '👍' },
  { id: ReactionTypes.Love, label: 'Love', emoji: '❤️' },
  { id: ReactionTypes.Applause, label: 'Applause', emoji: '👏' },
  { id: ReactionTypes.Laugh, label: 'Laugh', emoji: '😂' },
  { id: ReactionTypes.Surprise, label: 'Surprise', emoji: '😮' },
];

export interface ReactionsBarProps {
  isConnected: boolean;
  isMeetingActive: boolean;
  cooldownRemainingMs: number;
  lastSentReaction: string | null;
  onSendReaction: (reaction: string) => void;
}

export const ReactionsBar: React.FC<ReactionsBarProps> = ({
  isConnected,
  isMeetingActive,
  cooldownRemainingMs,
  lastSentReaction,
  onSendReaction,
}) => {
  const isCooldownActive = cooldownRemainingMs > 0;
  const isDisabled = !isConnected || !isMeetingActive || isCooldownActive;

  return (
    <section className="reactions-card" aria-label="Meeting Reactions">
      <div className="reactions-header">
        <div className="reactions-title-group">
          <svg
            className="reactions-title-icon"
            viewBox="0 0 24 24"
            width="18"
            height="18"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <circle cx="12" cy="12" r="10" />
            <path d="M8 14s1.5 2 4 2 4-2 4-2" />
            <line x1="9" y1="9" x2="9.01" y2="9" />
            <line x1="15" y1="9" x2="15.01" y2="9" />
          </svg>
          <span className="reactions-title">Live Reactions</span>
        </div>

        <div className="reactions-status-group">
          {isCooldownActive && (
            <span className="reaction-cooldown-badge" aria-live="polite">
              {(cooldownRemainingMs / 1000).toFixed(1)}s
            </span>
          )}
          {!isCooldownActive && lastSentReaction && (
            <span className="reaction-sent-badge" aria-live="polite">
              Sent!
            </span>
          )}
        </div>
      </div>

      <div className="reactions-grid" role="group" aria-label="Reaction Options">
        {REACTIONS.map((item) => {
          const isJustSent = lastSentReaction === item.id;
          return (
            <button
              key={item.id}
              id={`reaction-btn-${item.id}`}
              type="button"
              className={`reaction-btn ${isJustSent ? 'is-active' : ''}`}
              disabled={isDisabled}
              onClick={() => onSendReaction(item.id)}
              aria-label={`Send ${item.label} reaction`}
            >
              <span className="reaction-emoji" aria-hidden="true">
                {item.emoji}
              </span>
              <span className="reaction-name">{item.label}</span>
            </button>
          );
        })}
      </div>

      <div className="reactions-footer-hint">
        {!isMeetingActive
          ? 'Reactions are available when a meeting is active.'
          : isCooldownActive
          ? 'Cooling down to prevent reaction spam.'
          : 'Tap to send instant one-shot reaction.'}
      </div>
    </section>
  );
};
