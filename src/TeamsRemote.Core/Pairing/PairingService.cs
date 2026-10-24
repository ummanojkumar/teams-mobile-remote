using System.Security.Cryptography;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.Security;

namespace TeamsRemote.Core.Pairing;

public sealed class PairingService
{
    private readonly IDeviceCredentialStore _credentialStore;
    private readonly Func<string, string, Task<bool>> _confirmPairingPromptFunc;
    private readonly object _lock = new();

    private PairChallengePayload? _activeChallenge;
    private PairedDeviceRecord? _activeDevice;

    public event Action? DevicePairingChanged;

    public PairingService(
        IDeviceCredentialStore credentialStore,
        Func<string, string, Task<bool>> confirmPairingPromptFunc)
    {
        _credentialStore = credentialStore;
        _confirmPairingPromptFunc = confirmPairingPromptFunc;
    }

    public async Task InitializeAsync()
    {
        _activeDevice = await _credentialStore.LoadAsync();
        DevicePairingChanged?.Invoke();
    }

    public PairedDeviceRecord? ActiveDevice
    {
        get
        {
            lock (_lock)
            {
                return _activeDevice;
            }
        }
    }

    public bool IsPaired => ActiveDevice != null;

    public Guid? ActiveDeviceId => ActiveDevice?.DeviceId;

    public PairChallengePayload CreateSingleUseChallenge(string relayUrl)
    {
        lock (_lock)
        {
            int codeNumber = RandomNumberGenerator.GetInt32(100000, 1000000);
            string shortCode = $"{codeNumber / 1000:D3}-{codeNumber % 1000:D3}";

            _activeChallenge = new PairChallengePayload
            {
                ChallengeId = Guid.NewGuid(),
                RelayUrl = relayUrl,
                ShortCode = shortCode,
                // Exactly 60 seconds TTL as required
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
            };

            return _activeChallenge;
        }
    }

    public PairChallengePayload? GetActiveChallenge()
    {
        lock (_lock)
        {
            if (_activeChallenge != null && _activeChallenge.IsExpired)
            {
                _activeChallenge = null;
            }
            return _activeChallenge;
        }
    }

    public async Task<(bool Success, PairedDeviceRecord? Record, string? ErrorCode, string? ErrorReason)> RedeemChallengeAsync(
        PairRequestPayload request,
        CancellationToken cancellationToken = default)
    {
        PairChallengePayload? challenge;

        lock (_lock)
        {
            if (_activeChallenge == null || _activeChallenge.ChallengeId != request.ChallengeId)
            {
                return (false, null, ProtocolErrorCodes.InvalidPairingCode, "No matching pairing challenge found.");
            }

            if (_activeChallenge.IsExpired)
            {
                _activeChallenge = null;
                return (false, null, ProtocolErrorCodes.PairingExpired, "Pairing challenge has expired after 60 seconds.");
            }

            if (!string.Equals(_activeChallenge.ShortCode, request.ShortCode, StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, ProtocolErrorCodes.InvalidPairingCode, "Verification code does not match the challenge.");
            }

            // Invalidate immediately to ensure single-use
            challenge = _activeChallenge;
            _activeChallenge = null;
        }

        // Ask companion user for confirmation with matching short code and device label
        bool userApproved = await _confirmPairingPromptFunc(request.DeviceLabel, challenge.ShortCode);
        if (!userApproved)
        {
            return (false, null, ProtocolErrorCodes.PairingRejected, "Pairing was declined on the companion.");
        }

        // User approved: issue new device credentials and enforce 1-device limit
        var newRecord = new PairedDeviceRecord
        {
            DeviceId = Guid.NewGuid(),
            DeviceLabel = string.IsNullOrWhiteSpace(request.DeviceLabel) ? "Mobile Phone" : request.DeviceLabel.Trim(),
            PairedAt = DateTimeOffset.UtcNow,
            SessionToken = Guid.NewGuid().ToString("N")
        };

        await _credentialStore.SaveAsync(newRecord, cancellationToken);

        lock (_lock)
        {
            _activeDevice = newRecord;
        }

        DevicePairingChanged?.Invoke();
        return (true, newRecord, null, null);
    }

    public async Task RevokeDeviceAsync(CancellationToken cancellationToken = default)
    {
        await _credentialStore.ClearAsync(cancellationToken);

        lock (_lock)
        {
            _activeDevice = null;
            _activeChallenge = null;
        }

        DevicePairingChanged?.Invoke();
    }
}
