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

    public static readonly char[] Base32Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();

    public static string GeneratePairingCode()
    {
        Span<byte> randomBytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(randomBytes);
        Span<char> code = stackalloc char[6];
        for (int i = 0; i < 6; i++)
        {
            code[i] = Base32Alphabet[randomBytes[i] % Base32Alphabet.Length];
        }
        return new string(code);
    }

    public PairChallengePayload CreateSingleUseChallenge(string relayUrl)
    {
        lock (_lock)
        {
            string pairingCode = GeneratePairingCode();

            _activeChallenge = new PairChallengePayload
            {
                ChallengeId = Guid.NewGuid(),
                RelayUrl = relayUrl,
                PairingCode = pairingCode,
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
            if (_activeChallenge == null)
            {
                return (false, null, ProtocolErrorCodes.InvalidPairingCode, "No matching pairing challenge found.");
            }

            if (_activeChallenge.IsExpired)
            {
                _activeChallenge = null;
                return (false, null, ProtocolErrorCodes.PairingExpired, "Pairing challenge has expired after 60 seconds.");
            }

            // The pairing code MUST match the active challenge
            bool codeMatches = string.Equals(_activeChallenge.PairingCode, request.EffectivePairingCode, StringComparison.OrdinalIgnoreCase);
            if (!codeMatches)
            {
                // Invalid guesses MUST NOT affect another active challenge
                return (false, null, ProtocolErrorCodes.InvalidPairingCode, "Verification code does not match the active challenge.");
            }

            // If ChallengeId was supplied, it must also match
            if (request.ChallengeId != Guid.Empty && _activeChallenge.ChallengeId != request.ChallengeId)
            {
                return (false, null, ProtocolErrorCodes.InvalidPairingCode, "Challenge ID mismatch.");
            }

            // Consume challenge only after a correct code redemption starts the approval flow
            challenge = _activeChallenge;
            _activeChallenge = null;
        }

        // Ask companion user for confirmation with matching pairing code and device label
        bool userApproved = await _confirmPairingPromptFunc(request.DeviceLabel, challenge.PairingCode);
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
