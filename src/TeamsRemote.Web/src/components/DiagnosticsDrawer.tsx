import React, { useState } from 'react';
import { X, Send, RefreshCw, Terminal, ShieldAlert } from 'lucide-react';
import type { RemoteState } from '../types';

interface DiagnosticsDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  state: RemoteState;
  echoLog: { text: string; time: string; status: string }[];
  onTestEcho: (text: string) => void;
  onRefreshState: () => void;
}

export const DiagnosticsDrawer: React.FC<DiagnosticsDrawerProps> = ({
  isOpen,
  onClose,
  state,
  echoLog,
  onTestEcho,
  onRefreshState,
}) => {
  const [echoInput, setEchoInput] = useState('Hello from Mobile Remote!');

  if (!isOpen) return null;

  const handleSendEcho = (e: React.FormEvent) => {
    e.preventDefault();
    if (!echoInput.trim()) return;
    onTestEcho(echoInput.trim());
  };

  return (
    <div className="drawer-backdrop" onClick={onClose} role="dialog" aria-modal="true">
      <div className="drawer-panel" onClick={(e) => e.stopPropagation()}>
        <div className="drawer-header">
          <div className="drawer-title-group">
            <Terminal size={20} className="drawer-icon" />
            <h2 className="drawer-title">Transport Diagnostics</h2>
          </div>
          <button onClick={onClose} className="drawer-close-btn touch-target" aria-label="Close Drawer">
            <X size={20} />
          </button>
        </div>

        <div className="drawer-content">
          {/* Phase 3 Notice */}
          <div className="diagnostics-notice">
            <ShieldAlert size={16} className="notice-icon" />
            <span>
              In Phase 3, only <strong>test_echo</strong> and <strong>query_state</strong> are permitted across the transport boundary. Remote Teams control commands are locked.
            </span>
          </div>

          {/* Test Echo Section */}
          <div className="drawer-section">
            <h3 className="section-title">Test Echo Round-Trip</h3>
            <form onSubmit={handleSendEcho} className="echo-form">
              <input
                type="text"
                value={echoInput}
                onChange={(e) => setEchoInput(e.target.value)}
                placeholder="Enter text to echo back..."
                className="echo-input font-mono"
                disabled={state.connection !== 'connected'}
              />
              <button
                type="submit"
                disabled={state.connection !== 'connected' || !echoInput.trim()}
                className="echo-send-btn touch-target"
                title="Send test_echo"
              >
                <Send size={16} />
                <span>Send</span>
              </button>
            </form>

            <div className="echo-log-container font-mono">
              <div className="log-header">Echo History (Latest 10):</div>
              {echoLog.length === 0 ? (
                <div className="log-empty">No echo messages sent yet.</div>
              ) : (
                echoLog.map((item, idx) => (
                  <div key={idx} className="log-entry">
                    <span className="log-time">[{item.time}]</span>
                    <span className="log-status font-bold">[{item.status}]</span>
                    <span className="log-text">{item.text}</span>
                  </div>
                ))
              )}
            </div>
          </div>

          {/* Query State Section */}
          <div className="drawer-section">
            <div className="section-header-row">
              <h3 className="section-title">Current State Snapshot</h3>
              <button
                onClick={onRefreshState}
                disabled={state.connection !== 'connected'}
                className="action-btn refresh-btn touch-target"
              >
                <RefreshCw size={14} />
                <span>query_state</span>
              </button>
            </div>

            <pre className="state-raw-preview font-mono">
              {JSON.stringify(
                {
                  connection: state.connection,
                  pairing: state.pairing,
                  stateRevision: state.stateRevision,
                  meeting: state.meetingStatus,
                  observedAt: state.observedAt,
                  controls: state.controls,
                },
                null,
                2
              )}
            </pre>
          </div>
        </div>
      </div>
    </div>
  );
};
