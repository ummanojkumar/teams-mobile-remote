import React, { useState } from 'react';
import { usePwaInstall } from './hooks/usePwaInstall';
import { useRemoteTransport } from './hooks/useRemoteTransport';
import { Header } from './components/Header';
import { StatusBanner } from './components/StatusBanner';
import { ControlCard } from './components/ControlCard';
import { PairingView } from './components/PairingView';
import { AwaitingApprovalModal } from './components/AwaitingApprovalModal';
import { DiagnosticsDrawer } from './components/DiagnosticsDrawer';

export const App: React.FC = () => {
  const { canInstall, install } = usePwaInstall();
  const {
    state,
    echoLog,
    startPairing,
    unpair,
    testEcho,
    refreshState,
    resetPairingState,
  } = useRemoteTransport();

  const [isDiagnosticsOpen, setIsDiagnosticsOpen] = useState(false);

  const isPaired = state.pairing === 'paired';

  return (
    <div className="app-container">
      {/* App Header */}
      <Header
        connection={state.connection}
        pairing={state.pairing}
        canInstall={canInstall}
        onInstall={install}
        onOpenDiagnostics={() => setIsDiagnosticsOpen(true)}
        onUnpair={unpair}
      />

      {/* Main Content Area */}
      <main className="app-main" role="main">
        {!isPaired ? (
          <PairingView
            pairingStatus={state.pairing}
            errorMessage={state.errorMessage}
            onSubmitChallenge={startPairing}
            onReset={resetPairingState}
          />
        ) : (
          <div className="dashboard-content">
            {/* Status & Sync Banner */}
            <StatusBanner state={state} onRefresh={refreshState} />

            {/* Three Accessible Control Cards */}
            <section className="controls-stack" aria-label="Meeting Controls">
              <ControlCard
                type="microphone"
                snapshot={state.controls.microphone}
                isConnected={state.connection === 'connected'}
              />
              <ControlCard
                type="camera"
                snapshot={state.controls.camera}
                isConnected={state.connection === 'connected'}
              />
              <ControlCard
                type="hand"
                snapshot={state.controls.hand}
                isConnected={state.connection === 'connected'}
              />
            </section>
          </div>
        )}
      </main>

      {/* Awaiting Companion Approval Modal */}
      {state.pairing === 'awaiting_approval' && (
        <AwaitingApprovalModal
          matchingCode={state.matchingCode}
          onCancel={resetPairingState}
        />
      )}

      {/* Transport Diagnostics & Inspection Drawer */}
      <DiagnosticsDrawer
        isOpen={isDiagnosticsOpen}
        onClose={() => setIsDiagnosticsOpen(false)}
        state={state}
        echoLog={echoLog}
        onTestEcho={testEcho}
        onRefreshState={refreshState}
      />
    </div>
  );
};

export default App;
