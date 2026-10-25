import React from 'react';
import { Video, AlertTriangle } from 'lucide-react';

interface CameraSafetyModalProps {
  isOpen: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export const CameraSafetyModal: React.FC<CameraSafetyModalProps> = ({
  isOpen,
  onConfirm,
  onCancel,
}) => {
  if (!isOpen) return null;

  return (
    <div className="modal-backdrop" role="presentation" onClick={onCancel}>
      <div
        className="modal-container camera-safety-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="camera-safety-title"
        aria-describedby="camera-safety-description"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <div className="modal-icon-badge warning-badge">
            <AlertTriangle size={24} className="warning-icon" />
          </div>
          <h2 id="camera-safety-title" className="modal-title">
            Enable Remote Camera?
          </h2>
        </div>

        <div className="modal-body">
          <p id="camera-safety-description" className="modal-description">
            Your laptop camera will be turned on remotely, and your video feed will immediately become visible to all meeting participants.
          </p>
          <div className="safety-callout">
            <Video size={16} />
            <span>Make sure your camera is uncovered and positioned appropriately.</span>
          </div>
        </div>

        <div className="modal-actions">
          <button
            type="button"
            className="modal-btn secondary-btn touch-target"
            onClick={onCancel}
            data-testid="camera-safety-cancel"
          >
            Cancel
          </button>
          <button
            type="button"
            className="modal-btn primary-btn confirm-camera-btn touch-target"
            onClick={onConfirm}
            data-testid="camera-safety-confirm"
            autoFocus
          >
            <Video size={16} />
            <span>Turn Camera On</span>
          </button>
        </div>
      </div>
    </div>
  );
};
