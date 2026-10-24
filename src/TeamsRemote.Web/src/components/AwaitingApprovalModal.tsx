import React from 'react';
import { ShieldCheck, RefreshCw, XCircle } from 'lucide-react';

interface AwaitingApprovalModalProps {
  matchingCode: string | null;
  onCancel: () => void;
}

export const AwaitingApprovalModal: React.FC<AwaitingApprovalModalProps> = ({
  matchingCode,
  onCancel,
}) => {
  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="awaiting-title">
      <div className="modal-card">
        <div className="modal-header">
          <div className="modal-icon-badge">
            <ShieldCheck size={28} className="shield-icon" />
          </div>
          <h2 id="awaiting-title" className="modal-title">
            Awaiting Approval
          </h2>
          <p className="modal-subtitle">
            Look at your Windows laptop. Verify that the 6-digit confirmation code below matches what appears on your companion screen:
          </p>
        </div>

        <div className="code-display-container">
          <div className="code-label">Matching Confirmation Code</div>
          <div className="large-code-box font-mono">
            {matchingCode || '--- ---'}
          </div>
          <p className="code-instructions">
            Click <strong>&quot;Approve&quot;</strong> on your laptop companion to confirm.
          </p>
        </div>

        <div className="progress-indicator">
          <RefreshCw size={20} className="spin-icon" />
          <span>Waiting for response from companion...</span>
        </div>

        <div className="modal-actions">
          <button
            type="button"
            onClick={onCancel}
            className="secondary-btn cancel-btn touch-target"
          >
            <XCircle size={16} />
            <span>Cancel</span>
          </button>
        </div>
      </div>
    </div>
  );
};
