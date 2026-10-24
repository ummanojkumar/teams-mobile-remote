import React from 'react';
import { Mic, MicOff, Video, VideoOff, Hand, HelpCircle, AlertTriangle, Lock } from 'lucide-react';
import type { ControlSnapshotItem } from '../types';

export type ControlType = 'microphone' | 'camera' | 'hand';

interface ControlCardProps {
  type: ControlType;
  snapshot: ControlSnapshotItem;
  isConnected: boolean;
}

export const ControlCard: React.FC<ControlCardProps> = ({
  type,
  snapshot,
  isConnected,
}) => {
  const { value, confidence, capability } = snapshot;

  // Determine state labels and icons
  let title = '';
  let valueLabel = '';
  let IconComponent = HelpCircle;
  let stateThemeClass = 'theme-unknown';

  if (type === 'microphone') {
    title = 'Microphone';
    if (confidence === 'confirmed') {
      if (value === 'muted') {
        valueLabel = 'Muted';
        IconComponent = MicOff;
        stateThemeClass = 'theme-muted';
      } else if (value === 'unmuted') {
        valueLabel = 'Unmuted (Live)';
        IconComponent = Mic;
        stateThemeClass = 'theme-unmuted';
      } else {
        valueLabel = 'State Unknown';
        IconComponent = HelpCircle;
        stateThemeClass = 'theme-unknown';
      }
    } else {
      valueLabel = confidence === 'stale' ? 'State Stale' : 'State Unknown';
      IconComponent = confidence === 'stale' ? AlertTriangle : HelpCircle;
      stateThemeClass = confidence === 'stale' ? 'theme-stale' : 'theme-unknown';
    }
  } else if (type === 'camera') {
    title = 'Camera';
    if (confidence === 'confirmed') {
      if (value === 'on') {
        valueLabel = 'Camera On';
        IconComponent = Video;
        stateThemeClass = 'theme-on';
      } else if (value === 'off') {
        valueLabel = 'Camera Off';
        IconComponent = VideoOff;
        stateThemeClass = 'theme-off';
      } else {
        valueLabel = 'State Unknown';
        IconComponent = HelpCircle;
        stateThemeClass = 'theme-unknown';
      }
    } else {
      valueLabel = confidence === 'stale' ? 'State Stale' : 'State Unknown';
      IconComponent = confidence === 'stale' ? AlertTriangle : HelpCircle;
      stateThemeClass = confidence === 'stale' ? 'theme-stale' : 'theme-unknown';
    }
  } else if (type === 'hand') {
    title = 'Raise Hand';
    if (confidence === 'confirmed') {
      if (value === 'raised') {
        valueLabel = 'Hand Raised';
        IconComponent = Hand;
        stateThemeClass = 'theme-raised';
      } else if (value === 'lowered') {
        valueLabel = 'Hand Lowered';
        IconComponent = Hand;
        stateThemeClass = 'theme-lowered';
      } else {
        valueLabel = 'State Unknown';
        IconComponent = HelpCircle;
        stateThemeClass = 'theme-unknown';
      }
    } else {
      valueLabel = confidence === 'stale' ? 'State Stale' : 'State Unknown';
      IconComponent = confidence === 'stale' ? AlertTriangle : HelpCircle;
      stateThemeClass = confidence === 'stale' ? 'theme-stale' : 'theme-unknown';
    }
  }

  if (capability === 'unavailable' || !isConnected) {
    stateThemeClass += ' theme-disabled';
  }

  return (
    <div
      className={`control-card ${stateThemeClass}`}
      role="region"
      aria-label={`${title} control: ${valueLabel}`}
      data-testid={`control-card-${type}`}
    >
      <div className="card-top-row">
        <div className="card-title-group">
          <span className="card-title">{title}</span>
          <span className={`confidence-badge badge-${confidence}`}>
            {confidence.toUpperCase()}
          </span>
        </div>

        {/* Phase 3 Notice Badge */}
        <div className="phase3-locked-badge" title="Remote control locked in Phase 3">
          <Lock size={12} />
          <span>Not enabled yet</span>
        </div>
      </div>

      <div className="card-body">
        <div className="icon-container">
          <IconComponent size={44} className="control-icon" />
        </div>

        <div className="state-details">
          <div className="state-headline">{valueLabel}</div>
          <div className="state-subtext">
            {capability === 'unavailable'
              ? 'Control unavailable in current Teams window'
              : !isConnected
              ? 'Disconnected from relay'
              : 'Remote control disabled in Phase 3 proof of transport'}
          </div>
        </div>
      </div>

      {/* Button is strictly disabled and triggers no actions */}
      <button
        type="button"
        disabled={true}
        aria-disabled="true"
        className="card-action-btn disabled-btn touch-target"
        onClick={(e) => {
          e.preventDefault();
          // Intentional no-op: Remote controls are locked in Phase 3
        }}
      >
        <Lock size={14} />
        <span>Not enabled yet (Phase 3)</span>
      </button>
    </div>
  );
};
