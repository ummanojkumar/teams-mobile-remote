import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { PairingView } from '../src/components/PairingView';
import { AwaitingApprovalModal } from '../src/components/AwaitingApprovalModal';

describe('Pairing Flow State Machine & UI Verification', () => {
  it('renders pairing container with camera and manual tabs when unpaired', () => {
    render(
      <PairingView
        pairingStatus="unpaired"
        errorMessage={null}
        onSubmitChallenge={vi.fn()}
        onReset={vi.fn()}
      />
    );

    expect(screen.getByText('Pair with Companion')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Scan QR Code/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Enter Manually/i })).toBeInTheDocument();
  });

  it('switches between camera scan and manual entry tabs', () => {
    render(
      <PairingView
        pairingStatus="unpaired"
        errorMessage={null}
        onSubmitChallenge={vi.fn()}
        onReset={vi.fn()}
      />
    );

    const manualTab = screen.getByRole('button', { name: /Enter Manually/i });
    fireEvent.click(manualTab);

    expect(screen.getByText('Enter 6-Char Code')).toBeInTheDocument();
    expect(screen.getByText('6-Character Pairing Code')).toBeInTheDocument();

    const pasteTab = screen.getByRole('button', { name: /Paste QR Payload/i });
    fireEvent.click(pasteTab);

    expect(screen.getByText('QR Code Payload (JSON)')).toBeInTheDocument();
  });

  it('renders expired state card when 60-second challenge expires', () => {
    const resetSpy = vi.fn();
    render(
      <PairingView
        pairingStatus="expired"
        errorMessage="The 60-second pairing challenge expired."
        onSubmitChallenge={vi.fn()}
        onReset={resetSpy}
      />
    );

    expect(screen.getByText('Pairing Expired')).toBeInTheDocument();
    expect(screen.getByText('The 60-second pairing challenge expired.')).toBeInTheDocument();

    const retryBtn = screen.getByRole('button', { name: /Generate New QR & Try Again/i });
    fireEvent.click(retryBtn);
    expect(resetSpy).toHaveBeenCalled();
  });

  it('renders declined state card when companion rejects pairing request', () => {
    render(
      <PairingView
        pairingStatus="rejected"
        errorMessage="Pairing was declined on the companion."
        onSubmitChallenge={vi.fn()}
        onReset={vi.fn()}
      />
    );

    expect(screen.getByText('Pairing Declined')).toBeInTheDocument();
    expect(screen.getByText('Pairing was declined on the companion.')).toBeInTheDocument();
  });

  it('renders revoked state card when device was revoked', () => {
    render(
      <PairingView
        pairingStatus="revoked"
        errorMessage="Device was revoked by the companion."
        onSubmitChallenge={vi.fn()}
        onReset={vi.fn()}
      />
    );

    expect(screen.getByText('Device Revoked')).toBeInTheDocument();
    expect(screen.getByText('Device was revoked by the companion.')).toBeInTheDocument();
  });

  it('displays matching confirmation code prominently during awaiting_approval modal', () => {
    const cancelSpy = vi.fn();
    render(
      <AwaitingApprovalModal
        matchingCode="482-195"
        onCancel={cancelSpy}
      />
    );

    expect(screen.getByText('Awaiting Approval')).toBeInTheDocument();
    expect(screen.getByText('Matching Confirmation Code')).toBeInTheDocument();
    expect(screen.getByText('482-195')).toBeInTheDocument();
    expect(screen.getByText(/on your laptop companion to confirm/i)).toBeInTheDocument();

    const cancelBtn = screen.getByRole('button', { name: /Cancel/i });
    fireEvent.click(cancelBtn);
    expect(cancelSpy).toHaveBeenCalled();
  });
});
