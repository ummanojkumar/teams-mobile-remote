import React from 'react';
import { Download, Activity, LogOut, ShieldCheck, Wifi, WifiOff } from 'lucide-react';
import type { ConnectionStatus, PairingStatus } from '../types';

interface HeaderProps {
  connection: ConnectionStatus;
  pairing: PairingStatus;
  canInstall: boolean;
  onInstall: () => void;
  onOpenDiagnostics: () => void;
  onUnpair: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  connection,
  pairing,
  canInstall,
  onInstall,
  onOpenDiagnostics,
  onUnpair,
}) => {
  const isPaired = pairing === 'paired';

  return (
    <header className="app-header">
      <div className="header-brand">
        <div className="brand-icon">
          <ShieldCheck size={20} className="brand-logo-icon" />
        </div>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <h1 className="brand-title">Teams Remote</h1>
            <span style={{ fontSize: '0.65rem', padding: '1px 5px', borderRadius: '4px', background: 'rgba(255,255,255,0.08)', color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)' }}>
              v{typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.9.0-beta.1'}
            </span>
          </div>
          <div className="connection-indicator">
            {connection === 'connected' ? (
              <span className="status-pill status-connected">
                <Wifi size={12} /> Connected
              </span>
            ) : connection === 'reconnecting' ? (
              <span className="status-pill status-reconnecting">
                <WifiOff size={12} /> Reconnecting...
              </span>
            ) : (
              <span className="status-pill status-disconnected">
                <WifiOff size={12} /> Disconnected
              </span>
            )}
          </div>
        </div>
      </div>

      <div className="header-actions">
        {canInstall && (
          <button
            onClick={onInstall}
            className="action-btn install-btn"
            title="Install PWA to Home Screen"
            aria-label="Install App"
          >
            <Download size={18} />
            <span className="btn-text">Install</span>
          </button>
        )}

        <button
          onClick={onOpenDiagnostics}
          className="action-btn diag-btn"
          title="Diagnostics & Transport Tests"
          aria-label="Diagnostics"
        >
          <Activity size={18} />
        </button>

        {isPaired && (
          <button
            onClick={() => {
              if (window.confirm('Disconnect and unpair this phone? You will need to re-scan the QR code to pair again.')) {
                onUnpair();
              }
            }}
            className="action-btn unpair-btn"
            title="Disconnect & Unpair"
            aria-label="Unpair device"
          >
            <LogOut size={18} />
          </button>
        )}
      </div>
    </header>
  );
};
