import React, { useEffect, useRef, useState } from 'react';
import { CameraOff, RefreshCw } from 'lucide-react';
import { QrScannerService } from '../services/qrScanner';

interface QrCameraScannerProps {
  onScan: (rawResult: string) => void;
  onFallbackToManual: () => void;
}

export const QrCameraScanner: React.FC<QrCameraScannerProps> = ({
  onScan,
  onFallbackToManual,
}) => {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const scannerRef = useRef<QrScannerService | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isInitializing, setIsInitializing] = useState(true);

  useEffect(() => {
    const scanner = new QrScannerService();
    scannerRef.current = scanner;

    const startCamera = async () => {
      if (!videoRef.current) return;
      setIsInitializing(true);
      setError(null);

      try {
        await scanner.start(videoRef.current, {
          onScan: (data) => {
            onScan(data);
          },
          onError: (err) => {
            setError(err.message || 'Camera permission denied');
          },
        });
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Unable to access device camera');
      } finally {
        setIsInitializing(false);
      }
    };

    startCamera();

    return () => {
      scanner.stop();
    };
  }, [onScan]);

  return (
    <div className="qr-scanner-view">
      <div className="viewfinder-container">
        {error ? (
          <div className="scanner-error-state">
            <CameraOff size={48} className="error-camera-icon" />
            <h3 className="scanner-error-title">Camera Unavailable</h3>
            <p className="scanner-error-desc">
              {error.includes('Permission') || error.includes('denied')
                ? 'Camera access was blocked. You can still pair using manual code entry.'
                : error}
            </p>
            <button
              onClick={onFallbackToManual}
              className="action-btn switch-manual-btn touch-target"
            >
              Enter Pairing Code Manually
            </button>
          </div>
        ) : (
          <>
            <video
              ref={videoRef}
              className="scanner-video"
              muted
              playsInline
              autoPlay
            />
            {isInitializing ? (
              <div className="scanner-loading-overlay">
                <RefreshCw size={28} className="spin-icon" />
                <span>Starting camera...</span>
              </div>
            ) : (
              <div className="scanner-reticle-overlay">
                <div className="reticle-frame">
                  <div className="reticle-corner corner-tl" />
                  <div className="reticle-corner corner-tr" />
                  <div className="reticle-corner corner-bl" />
                  <div className="reticle-corner corner-br" />
                  <div className="scan-laser-line" />
                </div>
                <p className="reticle-caption">Align the Companion QR code inside the box</p>
              </div>
            )}
          </>
        )}
      </div>

      <div className="scanner-footer">
        <button
          onClick={onFallbackToManual}
          className="text-link-btn touch-target"
        >
          Having trouble? Enter code manually
        </button>
      </div>
    </div>
  );
};
