import jsQR from 'jsqr';

export interface QrScannerCallbacks {
  onScan: (data: string) => void;
  onError?: (error: Error) => void;
}

export class QrScannerService {
  private video: HTMLVideoElement | null = null;
  private canvas: HTMLCanvasElement | null = null;
  private ctx: CanvasRenderingContext2D | null = null;
  private stream: MediaStream | null = null;
  private animationFrameId: number | null = null;
  private isRunning = false;
  private callbacks: QrScannerCallbacks | null = null;

  async start(videoElement: HTMLVideoElement, callbacks: QrScannerCallbacks): Promise<void> {
    this.stop();
    this.video = videoElement;
    this.callbacks = callbacks;
    this.canvas = document.createElement('canvas');
    this.ctx = this.canvas.getContext('2d', { willReadFrequently: true });

    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: {
          facingMode: { ideal: 'environment' },
          width: { ideal: 1280 },
          height: { ideal: 720 },
        },
        audio: false,
      });

      this.video.srcObject = this.stream;
      this.video.setAttribute('playsinline', 'true'); // Required for iOS Safari
      await this.video.play();
      this.isRunning = true;
      this.scanLoop();
    } catch (err) {
      const error = err instanceof Error ? err : new Error('Unable to access camera');
      this.callbacks.onError?.(error);
      throw error;
    }
  }

  private scanLoop = () => {
    if (!this.isRunning || !this.video || !this.canvas || !this.ctx) {
      return;
    }

    if (this.video.readyState === this.video.HAVE_ENOUGH_DATA) {
      const width = this.video.videoWidth;
      const height = this.video.videoHeight;

      if (width > 0 && height > 0) {
        this.canvas.width = width;
        this.canvas.height = height;
        this.ctx.drawImage(this.video, 0, 0, width, height);

        const imageData = this.ctx.getImageData(0, 0, width, height);
        const code = jsQR(imageData.data, imageData.width, imageData.height, {
          inversionAttempts: 'dontInvert',
        });

        if (code && code.data && code.data.trim().length > 0) {
          this.callbacks?.onScan(code.data);
          return; // Pause scanning after detection
        }
      }
    }

    this.animationFrameId = requestAnimationFrame(this.scanLoop);
  };

  stop(): void {
    this.isRunning = false;
    if (this.animationFrameId !== null) {
      cancelAnimationFrame(this.animationFrameId);
      this.animationFrameId = null;
    }

    if (this.stream) {
      this.stream.getTracks().forEach((track) => track.stop());
      this.stream = null;
    }

    if (this.video) {
      this.video.srcObject = null;
      this.video = null;
    }

    this.canvas = null;
    this.ctx = null;
    this.callbacks = null;
  }
}
