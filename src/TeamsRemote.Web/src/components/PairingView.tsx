import React, { useState } from 'react';
import { QrCode, Keyboard, AlertTriangle, RefreshCw } from 'lucide-react';
import { parsePairingPayload, type ParsedChallenge } from '../services/pairing';
import { QrCameraScanner } from './QrCameraScanner';
import { ManualPairingForm } from './ManualPairingForm';
import type { PairingStatus } from '../types';

interface PairingViewProps {
  pairingStatus: PairingStatus;
  errorMessage: string | null;
  onSubmitChallenge: (challenge: ParsedChallenge) => void;
  onReset: () => void;
}

export const PairingView: React.FC<PairingViewProps> = ({
  pairingStatus,
  errorMessage,
  onSubmitChallenge,
  onReset,
}) => {
  const [activeTab, setActiveTab] = useState<'camera' | 'manual'>('camera');
  const [scanError, setScanError] = useState<string | null>(null);

  const handleScanResult = (rawQr: string) => {
    setScanError(null);
    const parsed = parsePairingPayload(rawQr);
    if (parsed.success && parsed.challenge) {
      onSubmitChallenge(parsed.challenge);
    } else {
      setScanError(parsed.error || 'Invalid QR code');
    }
  };

  const isFailedState = pairingStatus === 'expired' || pairingStatus === 'rejected' || pairingStatus === 'revoked';

  return (
    <div className="pairing-container">
      <div className="pairing-hero">
        <h2 className="pairing-hero-title">Pair with Companion</h2>
        <p className="pairing-hero-desc">
          Connect securely to your Windows laptop to inspect and control your Microsoft Teams meeting.
        </p>
      </div>

      {isFailedState && (
        <div className="pairing-failure-card" role="alert">
          <AlertTriangle size={32} className="failure-icon" />
          <h3 className="failure-title">
            {pairingStatus === 'expired'
              ? 'Pairing Expired'
              : pairingStatus === 'rejected'
              ? 'Pairing Declined'
              : 'Device Revoked'}
          </h3>
          <p className="failure-desc">
            {errorMessage ||
              (pairingStatus === 'expired'
                ? 'The 60-second pairing challenge expired before it was completed.'
                : 'The pairing request was not approved on the companion.')}
          </p>
          <button onClick={onReset} className="primary-submit-btn touch-target">
            <RefreshCw size={16} />
            <span>Generate New QR & Try Again</span>
          </button>
        </div>
      )}

      {scanError && (
        <div className="form-error-banner" role="alert">
          {scanError}
        </div>
      )}

      {!isFailedState && (
        <>
          <div className="pairing-tabs">
            <button
              type="button"
              onClick={() => setActiveTab('camera')}
              className={`tab-btn ${activeTab === 'camera' ? 'active' : ''}`}
            >
              <QrCode size={16} />
              <span>Scan QR Code</span>
            </button>
            <button
              type="button"
              onClick={() => setActiveTab('manual')}
              className={`tab-btn ${activeTab === 'manual' ? 'active' : ''}`}
            >
              <Keyboard size={16} />
              <span>Enter Manually</span>
            </button>
          </div>

          <div className="tab-content">
            {activeTab === 'camera' ? (
              <QrCameraScanner
                onScan={handleScanResult}
                onFallbackToManual={() => setActiveTab('manual')}
              />
            ) : (
              <ManualPairingForm
                onSubmitChallenge={onSubmitChallenge}
                onSwitchToCamera={() => setActiveTab('camera')}
              />
            )}
          </div>
        </>
      )}
    </div>
  );
};
