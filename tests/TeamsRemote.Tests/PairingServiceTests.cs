using TeamsRemote.Core.Pairing;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.Security;
using Xunit;

namespace TeamsRemote.Tests;

public class PairingServiceTests
{
    private class InMemoryCredentialStore : IDeviceCredentialStore
    {
        public PairedDeviceRecord? StoredRecord { get; private set; }

        public Task SaveAsync(PairedDeviceRecord record, CancellationToken cancellationToken = default)
        {
            StoredRecord = record;
            return Task.CompletedTask;
        }

        public Task<PairedDeviceRecord?> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoredRecord);
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            StoredRecord = null;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void GeneratePairingCode_Produces6CharacterBase32WithoutAmbiguousCharacters()
    {
        for (int i = 0; i < 50; i++)
        {
            string code = PairingService.GeneratePairingCode();
            Assert.Equal(6, code.Length);
            Assert.Matches(@"^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$", code);
            Assert.DoesNotContain('0', code);
            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('1', code);
            Assert.DoesNotContain('I', code);
        }
    }

    [Fact]
    public void CreateSingleUseChallenge_Creates60SecondExpiryAndBase32PairingCode()
    {
        var store = new InMemoryCredentialStore();
        var service = new PairingService(store, (lbl, code) => Task.FromResult(true));

        var challenge = service.CreateSingleUseChallenge("ws://localhost:5100/ws/client");

        Assert.NotEqual(Guid.Empty, challenge.ChallengeId);
        Assert.Matches(@"^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$", challenge.PairingCode);
        Assert.Matches(@"^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$", challenge.ShortCode);
        Assert.False(challenge.IsExpired);
        Assert.True(challenge.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(55));
        Assert.True(challenge.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(61));
    }

    [Fact]
    public async Task RedeemChallenge_WhenValidAndApproved_SucceedsAndEnforcesOneDeviceLimit()
    {
        var store = new InMemoryCredentialStore();
        string approvedLabel = "";
        string approvedCode = "";

        var service = new PairingService(store, (lbl, code) =>
        {
            approvedLabel = lbl;
            approvedCode = code;
            return Task.FromResult(true); // User approves
        });

        var challenge = service.CreateSingleUseChallenge("ws://localhost:5100/ws/client");

        var request = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = challenge.ShortCode,
            DeviceLabel = "Galaxy S24"
        };

        var (success, record, errorCode, errorReason) = await service.RedeemChallengeAsync(request);

        Assert.True(success);
        Assert.NotNull(record);
        Assert.Null(errorCode);
        Assert.Equal("Galaxy S24", record.DeviceLabel);
        Assert.Equal("Galaxy S24", approvedLabel);
        Assert.Equal(challenge.ShortCode, approvedCode);
        Assert.True(service.IsPaired);
        Assert.Equal(record.DeviceId, service.ActiveDeviceId);

        // Single-use check: immediate second redemption of the same challenge must fail
        var (secondSuccess, secondRecord, secondError, _) = await service.RedeemChallengeAsync(request);
        Assert.False(secondSuccess);
        Assert.Null(secondRecord);
        Assert.Equal(ProtocolErrorCodes.InvalidPairingCode, secondError);
    }

    [Fact]
    public async Task RedeemChallenge_WhenCompanionUserDeclines_ReturnsRejected()
    {
        var store = new InMemoryCredentialStore();
        var service = new PairingService(store, (lbl, code) => Task.FromResult(false)); // User declines

        var challenge = service.CreateSingleUseChallenge("ws://localhost:5100/ws/client");

        var request = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = challenge.ShortCode,
            DeviceLabel = "Unknown Device"
        };

        var (success, record, errorCode, _) = await service.RedeemChallengeAsync(request);

        Assert.False(success);
        Assert.Null(record);
        Assert.Equal(ProtocolErrorCodes.PairingRejected, errorCode);
        Assert.False(service.IsPaired);
    }

    [Fact]
    public async Task RedeemChallenge_WhenCodeMismatches_ReturnsInvalidCode()
    {
        var store = new InMemoryCredentialStore();
        var service = new PairingService(store, (lbl, code) => Task.FromResult(true));

        var challenge = service.CreateSingleUseChallenge("ws://localhost:5100/ws/client");

        var request = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = "000-000", // Wrong code
            DeviceLabel = "Attacker Device"
        };

        var (success, _, errorCode, _) = await service.RedeemChallengeAsync(request);

        Assert.False(success);
        Assert.Equal(ProtocolErrorCodes.InvalidPairingCode, errorCode);
    }

    [Fact]
    public async Task RedeemChallenge_WhenIncorrectCodeEntered_DoesNotInvalidateActiveChallenge()
    {
        var store = new InMemoryCredentialStore();
        var service = new PairingService(store, (lbl, code) => Task.FromResult(true));

        var challenge = service.CreateSingleUseChallenge("ws://localhost:5100/ws/client");

        // Attacker / accidental typo guess
        var wrongRequest = new PairRequestPayload
        {
            PairingCode = "WRONGX",
            DeviceLabel = "Typo Phone"
        };
        var (wrongSuccess, _, wrongErrorCode, _) = await service.RedeemChallengeAsync(wrongRequest);
        Assert.False(wrongSuccess);
        Assert.Equal(ProtocolErrorCodes.InvalidPairingCode, wrongErrorCode);

        // Challenge is STILL active for the legitimate user
        Assert.NotNull(service.GetActiveChallenge());

        // Legitimate user enters the correct pairing code
        var validRequest = new PairRequestPayload
        {
            PairingCode = challenge.PairingCode,
            DeviceLabel = "Legitimate Phone"
        };
        var (validSuccess, record, _, _) = await service.RedeemChallengeAsync(validRequest);
        Assert.True(validSuccess);
        Assert.NotNull(record);
        Assert.True(service.IsPaired);
    }

    [Fact]
    public async Task RevokeDevice_ClearsCredentialsAndActiveDevice()
    {
        var store = new InMemoryCredentialStore();
        var service = new PairingService(store, (lbl, code) => Task.FromResult(true));

        var challenge = service.CreateSingleUseChallenge("ws://localhost:5100/ws/client");
        await service.RedeemChallengeAsync(new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = challenge.ShortCode,
            DeviceLabel = "Phone 1"
        });

        Assert.True(service.IsPaired);

        await service.RevokeDeviceAsync();

        Assert.False(service.IsPaired);
        Assert.Null(service.ActiveDeviceId);
        Assert.Null(store.StoredRecord);
    }
}
