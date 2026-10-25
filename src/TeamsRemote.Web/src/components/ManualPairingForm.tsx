import React, { useState } from 'react';
import { KeyRound, ArrowRight, Clipboard, ChevronDown, ChevronUp } from 'lucide-react';
import {
  parseManualCode,
  parsePairingPayload,
  getDefaultRelayUrl,
  type ParsedChallenge,
} from '../services/pairing';

interface ManualPairingFormProps {
  onSubmitChallenge: (challenge: ParsedChallenge) => void;
  onSwitchToCamera: () => void;
}

export const ManualPairingForm: React.FC<ManualPairingFormProps> = ({
  onSubmitChallenge,
  onSwitchToCamera,
}) => {
  const [pairingCode, setPairingCode] = useState('');
  const [relayUrl, setRelayUrl] = useState('');
  const [pastePayload, setPastePayload] = useState('');
  const [mode, setMode] = useState<'code' | 'paste'>('code');
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const handleCodeChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setFormError(null);
    const upper = e.target.value.toUpperCase();
    if (/[01IO]/.test(upper)) {
      setFormError('Characters 0, 1, I, and O are excluded to prevent confusion.');
    }
    const clean = upper.replace(/[^2-9A-Z]/g, '').replace(/[01IO]/g, '').slice(0, 6);
    setPairingCode(clean);
  };

  const handleCodeSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const result = parseManualCode(pairingCode, relayUrl || undefined);
    if (!result.success || !result.challenge) {
      setFormError(result.error || 'Invalid 6-character pairing code');
      return;
    }

    onSubmitChallenge(result.challenge);
  };

  const handlePasteSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const result = parsePairingPayload(pastePayload);
    if (!result.success || !result.challenge) {
      setFormError(result.error || 'Invalid QR payload format');
      return;
    }

    onSubmitChallenge(result.challenge);
  };

  const handleClipboardPaste = async () => {
    try {
      const text = await navigator.clipboard.readText();
      if (text) {
        setPastePayload(text);
      }
    } catch {
      // Ignore clipboard permission errors
    }
  };

  return (
    <div className="manual-pairing-view">
      <div className="manual-mode-selector">
        <button
          type="button"
          onClick={() => { setMode('code'); setFormError(null); }}
          className={`mode-tab-btn ${mode === 'code' ? 'active' : ''}`}
        >
          Enter 6-Char Code
        </button>
        <button
          type="button"
          onClick={() => { setMode('paste'); setFormError(null); }}
          className={`mode-tab-btn ${mode === 'paste' ? 'active' : ''}`}
        >
          Paste QR Payload
        </button>
      </div>

      {formError && (
        <div className="form-error-banner" role="alert">
          {formError}
        </div>
      )}

      {mode === 'code' ? (
        <form onSubmit={handleCodeSubmit} className="pairing-form">
          <div className="form-group">
            <label htmlFor="pairing-code" className="form-label">
              6-Character Pairing Code
            </label>
            <input
              id="pairing-code"
              type="text"
              value={pairingCode}
              onChange={handleCodeChange}
              placeholder="e.g. K7M4PX"
              maxLength={6}
              autoCapitalize="characters"
              autoComplete="off"
              spellCheck="false"
              className="form-input pairing-code-input"
              required
            />
            <p className="form-helper">
              Enter the single 6-character code shown on your laptop companion (excludes 0, 1, I, O).
            </p>
          </div>

          <div className="advanced-settings-toggle">
            <button
              type="button"
              onClick={() => setShowAdvanced(!showAdvanced)}
              className="text-link-btn"
              style={{ fontSize: '0.8rem', padding: '4px 0', display: 'flex', alignItems: 'center', gap: '4px' }}
            >
              {showAdvanced ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
              <span>{showAdvanced ? 'Hide Relay URL' : 'Advanced: Custom Relay URL'}</span>
            </button>
          </div>

          {showAdvanced && (
            <div className="form-group">
              <label htmlFor="relay-url" className="form-label">
                Relay URL <span className="label-optional">(optional)</span>
              </label>
              <input
                id="relay-url"
                type="text"
                value={relayUrl}
                onChange={(e) => setRelayUrl(e.target.value)}
                placeholder={getDefaultRelayUrl()}
                className="form-input font-mono"
              />
            </div>
          )}

          <button
            type="submit"
            className="primary-submit-btn touch-target"
            disabled={pairingCode.length !== 6}
          >
            <KeyRound size={18} />
            <span>Connect & Pair</span>
          </button>
        </form>
      ) : (
        <form onSubmit={handlePasteSubmit} className="pairing-form">
          <div className="form-group">
            <label htmlFor="payload-input" className="form-label">
              QR Code Payload (JSON)
            </label>
            <div className="textarea-wrapper">
              <textarea
                id="payload-input"
                value={pastePayload}
                onChange={(e) => setPastePayload(e.target.value)}
                placeholder='Paste the JSON payload from the Companion "Copy Details" button...'
                rows={4}
                className="form-textarea font-mono"
                required
              />
              <button
                type="button"
                onClick={handleClipboardPaste}
                className="paste-icon-btn touch-target"
                title="Paste from clipboard"
              >
                <Clipboard size={16} />
              </button>
            </div>
            <p className="form-helper">
              Tip: Click &quot;Copy Details&quot; on the Companion desktop app and paste here.
            </p>
          </div>

          <button type="submit" className="primary-submit-btn touch-target">
            <span>Start Pairing</span>
            <ArrowRight size={18} />
          </button>
        </form>
      )}

      <div className="manual-form-footer">
        <button
          type="button"
          onClick={onSwitchToCamera}
          className="text-link-btn touch-target"
        >
          Switch back to camera scanner
        </button>
      </div>
    </div>
  );
};
