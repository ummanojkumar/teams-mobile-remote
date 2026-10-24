import React, { useState } from 'react';
import { KeyRound, ArrowRight, Clipboard } from 'lucide-react';
import { parseManualInput, parsePairingPayload, type ParsedChallenge } from '../services/pairing';

interface ManualPairingFormProps {
  onSubmitChallenge: (challenge: ParsedChallenge) => void;
  onSwitchToCamera: () => void;
}

export const ManualPairingForm: React.FC<ManualPairingFormProps> = ({
  onSubmitChallenge,
  onSwitchToCamera,
}) => {
  const [pastePayload, setPastePayload] = useState('');
  const [challengeId, setChallengeId] = useState('');
  const [shortCode, setShortCode] = useState('');
  const [relayUrl, setRelayUrl] = useState('');
  const [mode, setMode] = useState<'paste' | 'fields'>('paste');
  const [formError, setFormError] = useState<string | null>(null);

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

  const handleFieldsSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const result = parseManualInput(challengeId, shortCode, relayUrl || undefined);
    if (!result.success || !result.challenge) {
      setFormError(result.error || 'Invalid input fields');
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
          onClick={() => { setMode('paste'); setFormError(null); }}
          className={`mode-tab-btn ${mode === 'paste' ? 'active' : ''}`}
        >
          Paste QR Payload
        </button>
        <button
          type="button"
          onClick={() => { setMode('fields'); setFormError(null); }}
          className={`mode-tab-btn ${mode === 'fields' ? 'active' : ''}`}
        >
          Enter ID & Code
        </button>
      </div>

      {formError && (
        <div className="form-error-banner" role="alert">
          {formError}
        </div>
      )}

      {mode === 'paste' ? (
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
      ) : (
        <form onSubmit={handleFieldsSubmit} className="pairing-form">
          <div className="form-group">
            <label htmlFor="challenge-id" className="form-label">
              Challenge ID (UUID)
            </label>
            <input
              id="challenge-id"
              type="text"
              value={challengeId}
              onChange={(e) => setChallengeId(e.target.value)}
              placeholder="e.g. 12345678-1234-1234-1234-1234567890ab"
              className="form-input font-mono"
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="short-code" className="form-label">
              Verification Code
            </label>
            <input
              id="short-code"
              type="text"
              value={shortCode}
              onChange={(e) => setShortCode(e.target.value)}
              placeholder="e.g. 482-910"
              maxLength={7}
              className="form-input font-mono shortcode-input"
              required
            />
            <p className="form-helper">
              Enter the 6-digit code shown on the Companion QR screen.
            </p>
          </div>

          <div className="form-group">
            <label htmlFor="relay-url" className="form-label">
              Relay URL <span className="label-optional">(optional)</span>
            </label>
            <input
              id="relay-url"
              type="text"
              value={relayUrl}
              onChange={(e) => setRelayUrl(e.target.value)}
              placeholder="ws://127.0.0.1:5100/ws/client"
              className="form-input font-mono"
            />
          </div>

          <button type="submit" className="primary-submit-btn touch-target">
            <KeyRound size={18} />
            <span>Redeem Challenge</span>
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
