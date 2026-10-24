import React from 'react';
import { AlertCircle, Clock, Hash, Users, RefreshCw } from 'lucide-react';
import type { RemoteState } from '../types';

interface StatusBannerProps {
  state: RemoteState;
  onRefresh: () => void;
}

export const StatusBanner: React.FC<StatusBannerProps> = ({ state, onRefresh }) => {
  const { meetingStatus, stateRevision, observedAt, connection, errorMessage, pairing } = state;

  const formattedTime = observedAt
    ? new Date(observedAt).toLocaleTimeString()
    : 'Not observed';

  return (
    <div className="status-banner-card">
      {/* Critical Alert if any */}
      {errorMessage && (
        <div className="status-error-alert" role="alert">
          <AlertCircle size={16} className="error-icon" />
          <span className="error-text">{errorMessage}</span>
        </div>
      )}

      {pairing === 'revoked' && (
        <div className="status-error-alert alert-revoked" role="alert">
          <AlertCircle size={16} className="error-icon" />
          <span className="error-text">
            Device was revoked by the companion. Please scan a new QR code to pair again.
          </span>
        </div>
      )}

      {connection === 'reconnecting' && (
        <div className="status-warning-alert" role="status">
          <RefreshCw size={14} className="spin-icon" />
          <span>Connection interrupted. Reconnecting with exponential backoff...</span>
        </div>
      )}

      {/* Real-time Meeting & Sync Row */}
      <div className="status-metrics-grid">
        <div className="metric-item">
          <span className="metric-label">
            <Users size={13} /> Meeting
          </span>
          <span className={`metric-value meeting-${meetingStatus}`}>
            {meetingStatus === 'active'
              ? 'Active Meeting'
              : meetingStatus === 'none'
              ? 'No Meeting'
              : 'Unknown'}
          </span>
        </div>

        <div className="metric-item">
          <span className="metric-label">
            <Hash size={13} /> State Rev
          </span>
          <span className="metric-value font-mono">
            #{stateRevision}
          </span>
        </div>

        <div className="metric-item">
          <span className="metric-label">
            <Clock size={13} /> Synced At
          </span>
          <span className="metric-value font-mono">
            {formattedTime}
          </span>
        </div>

        <div className="metric-item refresh-metric">
          <button
            onClick={onRefresh}
            disabled={connection !== 'connected'}
            className="mini-refresh-btn touch-target"
            title="Refresh Teams State (query_state)"
            aria-label="Refresh State"
          >
            <RefreshCw size={14} className={connection === 'connecting' ? 'spin-icon' : ''} />
          </button>
        </div>
      </div>
    </div>
  );
};
