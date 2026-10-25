import React from 'react';
import {
  Mic,
  MicOff,
  Video,
  VideoOff,
  Hand,
  HelpCircle,
  AlertTriangle,
  Lock,
  Loader2,
  CheckCircle,
} from 'lucide-react';
import type { ControlSnapshotItem } from '../types';

export type ControlType = 'microphone' | 'camera' | 'hand';

interface ControlCardProps {
  type: ControlType;
  snapshot: ControlSnapshotItem;
  isConnected: boolean;
  onAction?: (desired: 'muted' | 'unmuted') => void;
  onCameraAction?: (desired: 'on' | 'off') => void;
  isActionPending?: boolean;
}

export const ControlCard: React.FC<ControlCardProps> = ({
  type,
  snapshot,
  isConnected,
  onAction,
  onCameraAction,
  isActionPending = false,
}) => {
  const { value, confidence, capability } = snapshot;

  const isMic = type === 'microphone';
  const isCamera = type === 'camera';

  const isMicActionable =
    isMic &&
    isConnected &&
    capability === 'verified' &&
    confidence === 'confirmed' &&
    (value === 'muted' || value === 'unmuted');

  const isCameraActionable =
    isCamera &&
    isConnected &&
    (capability === 'observe-only' || capability === 'verified') &&
    confidence === 'confirmed' &&
    (value === 'on' || value === 'off');

  const isPending = (isMic || isCamera) && isActionPending;

  // Determine state labels and icons
  let title = '';
  let valueLabel = '';
  let IconComponent = HelpCircle;
  let stateThemeClass = 'theme-unknown';

  if (isMic) {
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
      valueLabel = confidence === 'stale' ? 'State Stale' : confidence === 'pending' ? 'Command In Progress...' : 'State Unknown';
      IconComponent = confidence === 'stale' ? AlertTriangle : confidence === 'pending' ? Loader2 : HelpCircle;
      stateThemeClass = confidence === 'stale' ? 'theme-stale' : 'theme-unknown';
    }
  } else if (isCamera) {
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
      valueLabel = confidence === 'stale' ? 'State Stale' : confidence === 'pending' ? 'Command In Progress...' : 'State Unknown';
      IconComponent = confidence === 'stale' ? AlertTriangle : confidence === 'pending' ? Loader2 : HelpCircle;
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

  // Subtext guidance
  let subtext = '';
  if (capability === 'unavailable') {
    subtext = 'Control unavailable in current Teams window';
  } else if (!isConnected) {
    subtext = 'Disconnected from relay';
  } else if (isPending) {
    if (isMic) {
      subtext = value === 'muted' ? 'Sending unmute command to Teams...' : 'Sending mute command to Teams...';
    } else {
      subtext = value === 'off' ? 'Sending camera-on command to Teams...' : 'Sending camera-off command to Teams...';
    }
  } else if (confidence === 'stale') {
    subtext = 'State observation is stale — please refresh';
  } else if (isMicActionable) {
    subtext = value === 'muted' ? 'Tap below to unmute microphone' : 'Tap below to mute microphone';
  } else if (isCameraActionable) {
    if (capability === 'verified') {
      subtext = value === 'off' ? 'Tap below to turn camera on' : 'Tap below to turn camera off';
    } else {
      subtext = value === 'off'
        ? 'Camera action-eligible — tap to turn on and verify control'
        : 'Camera action-eligible — tap to turn off and verify control';
    }
  } else if (type === 'hand') {
    if (confidence === 'confirmed') {
      subtext = 'Raise hand observation active (Phase 5A spike) — control disabled';
    } else {
      subtext = 'Raise hand observation waiting for Teams meeting state';
    }
  } else if (isMic) {
    subtext = 'Microphone control waiting for verified Teams meeting state';
  } else if (isCamera) {
    subtext = 'Camera control waiting for confirmed Teams meeting state';
  } else {
    subtext = 'Remote control disabled';
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

        {/* Phase / Capability Badge */}
        {((isMic && capability === 'verified') || (isCamera && capability === 'verified')) ? (
          <div className="active-verified-badge" title="Remote control active & verified">
            <CheckCircle size={12} />
            <span>Remote Active</span>
          </div>
        ) : ((isCamera || type === 'hand') && capability === 'observe-only') ? (
          <div className="phase-observe-badge" title={`${title} capability: observe-only`}>
            <Lock size={12} />
            <span>Observe-Only</span>
          </div>
        ) : (
          <div className="phase3-locked-badge" title={isMic ? 'Microphone locked' : 'Remote control locked'}>
            <Lock size={12} />
            <span>Not enabled yet</span>
          </div>
        )}
      </div>

      <div className="card-body">
        <div className="icon-container">
          <IconComponent size={44} className={`control-icon ${isPending ? 'spin-icon' : ''}`} />
        </div>

        <div className="state-details">
          <div className="state-headline">{valueLabel}</div>
          <div className="state-subtext">{subtext}</div>
        </div>
      </div>

      {/* Action Button: Interactive for Microphone in Phase 4A & Camera in Phase 4C */}
      {isMic ? (
        isPending ? (
          <button
            type="button"
            disabled={true}
            aria-disabled="true"
            aria-busy="true"
            className="card-action-btn pending-btn touch-target"
            data-testid="microphone-action-pending"
          >
            <Loader2 size={18} className="spin-icon" />
            <span>{value === 'muted' ? 'Unmuting...' : 'Muting...'}</span>
          </button>
        ) : isMicActionable ? (
          value === 'muted' ? (
            <button
              type="button"
              className="card-action-btn action-unmute touch-target"
              data-testid="microphone-action-unmute"
              onClick={() => onAction?.('unmuted')}
            >
              <Mic size={18} />
              <span>Unmute</span>
            </button>
          ) : (
            <button
              type="button"
              className="card-action-btn action-mute touch-target"
              data-testid="microphone-action-mute"
              onClick={() => onAction?.('muted')}
            >
              <MicOff size={18} />
              <span>Mute</span>
            </button>
          )
        ) : (
          <button
            type="button"
            disabled={true}
            aria-disabled="true"
            className="card-action-btn disabled-btn touch-target"
            data-testid="microphone-action-disabled"
          >
            <Lock size={14} />
            <span>
              {!isConnected
                ? 'Disconnected'
                : capability === 'unavailable'
                ? 'Unavailable'
                : confidence === 'stale'
                ? 'State Stale (Refresh)'
                : 'Control Unavailable'}
            </span>
          </button>
        )
      ) : isCamera ? (
        isPending ? (
          <button
            type="button"
            disabled={true}
            aria-disabled="true"
            aria-busy="true"
            className="card-action-btn pending-btn touch-target"
            data-testid="camera-action-pending"
          >
            <Loader2 size={18} className="spin-icon" />
            <span>{value === 'off' ? 'Turning On...' : 'Turning Off...'}</span>
          </button>
        ) : isCameraActionable ? (
          value === 'off' ? (
            <button
              type="button"
              className="card-action-btn action-unmute touch-target"
              data-testid="camera-action-on"
              onClick={() => onCameraAction?.('on')}
            >
              <Video size={18} />
              <span>Turn Camera On</span>
            </button>
          ) : (
            <button
              type="button"
              className="card-action-btn action-mute touch-target"
              data-testid="camera-action-off"
              onClick={() => onCameraAction?.('off')}
            >
              <VideoOff size={18} />
              <span>Turn Camera Off</span>
            </button>
          )
        ) : (
          <button
            type="button"
            disabled={true}
            aria-disabled="true"
            className="card-action-btn disabled-btn touch-target"
            data-testid="camera-action-disabled"
          >
            <Lock size={14} />
            <span>
              {!isConnected
                ? 'Disconnected'
                : capability === 'unavailable'
                ? 'Unavailable'
                : confidence === 'stale'
                ? 'State Stale (Refresh)'
                : 'Control Unavailable'}
            </span>
          </button>
        )
      ) : (
        <button
          type="button"
          disabled={true}
          aria-disabled="true"
          className="card-action-btn disabled-btn touch-target"
          data-testid={`${type}-action-locked`}
          onClick={(e) => {
            e.preventDefault();
          }}
        >
          <Lock size={14} />
          <span>Not enabled yet</span>
        </button>
      )}
    </div>
  );
};
