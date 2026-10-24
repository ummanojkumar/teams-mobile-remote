import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ControlCard } from '../src/components/ControlCard';
import type { ControlSnapshotItem } from '../src/types';

describe('Authoritative State Model Display', () => {
  it('renders unknown state as "State Unknown" and NEVER as muted or unmuted', () => {
    const unknownMic: ControlSnapshotItem = {
      value: 'unknown',
      capability: 'observe-only',
      confidence: 'unknown',
    };

    render(<ControlCard type="microphone" snapshot={unknownMic} isConnected={true} />);

    expect(screen.getByText('State Unknown')).toBeInTheDocument();
    expect(screen.queryByText('Muted')).not.toBeInTheDocument();
    expect(screen.queryByText('Unmuted (Live)')).not.toBeInTheDocument();
  });

  it('renders unknown camera state as "State Unknown" and NEVER as Camera On or Off', () => {
    const unknownCamera: ControlSnapshotItem = {
      value: 'unknown',
      capability: 'observe-only',
      confidence: 'unknown',
    };

    render(<ControlCard type="camera" snapshot={unknownCamera} isConnected={true} />);

    expect(screen.getByText('State Unknown')).toBeInTheDocument();
    expect(screen.queryByText('Camera On')).not.toBeInTheDocument();
    expect(screen.queryByText('Camera Off')).not.toBeInTheDocument();
  });

  it('renders unknown hand state as "State Unknown" and NEVER as Raised or Lowered', () => {
    const unknownHand: ControlSnapshotItem = {
      value: 'unknown',
      capability: 'observe-only',
      confidence: 'unknown',
    };

    render(<ControlCard type="hand" snapshot={unknownHand} isConnected={true} />);

    expect(screen.getByText('State Unknown')).toBeInTheDocument();
    expect(screen.queryByText('Hand Raised')).not.toBeInTheDocument();
    expect(screen.queryByText('Hand Lowered')).not.toBeInTheDocument();
  });

  it('renders confirmed muted microphone accurately', () => {
    const mutedMic: ControlSnapshotItem = {
      value: 'muted',
      capability: 'verified',
      confidence: 'confirmed',
    };

    render(<ControlCard type="microphone" snapshot={mutedMic} isConnected={true} />);

    expect(screen.getByText('Muted')).toBeInTheDocument();
    expect(screen.getByText('CONFIRMED')).toBeInTheDocument();
  });

  it('renders confirmed unmuted microphone accurately', () => {
    const unmutedMic: ControlSnapshotItem = {
      value: 'unmuted',
      capability: 'verified',
      confidence: 'confirmed',
    };

    render(<ControlCard type="microphone" snapshot={unmutedMic} isConnected={true} />);

    expect(screen.getByText('Unmuted (Live)')).toBeInTheDocument();
    expect(screen.getByText('CONFIRMED')).toBeInTheDocument();
  });

  it('renders stale state warning when confidence is stale', () => {
    const staleMic: ControlSnapshotItem = {
      value: 'muted',
      capability: 'verified',
      confidence: 'stale',
    };

    render(<ControlCard type="microphone" snapshot={staleMic} isConnected={true} />);

    expect(screen.getByText('State Stale')).toBeInTheDocument();
    expect(screen.getByText('STALE')).toBeInTheDocument();
  });

  it('renders unavailable capability message when capability is unavailable', () => {
    const unavailMic: ControlSnapshotItem = {
      value: 'unknown',
      capability: 'unavailable',
      confidence: 'unknown',
    };

    render(<ControlCard type="microphone" snapshot={unavailMic} isConnected={true} />);

    expect(screen.getByText('Control unavailable in current Teams window')).toBeInTheDocument();
  });
});
