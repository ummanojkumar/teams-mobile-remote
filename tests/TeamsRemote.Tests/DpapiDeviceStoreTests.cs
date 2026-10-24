using System.Text;
using TeamsRemote.Core.Security;
using Xunit;

namespace TeamsRemote.Tests;

public class DpapiDeviceStoreTests : IDisposable
{
    private readonly string _tempFilePath;

    public DpapiDeviceStoreTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"teams_remote_test_{Guid.NewGuid():N}.dat");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            try { File.Delete(_tempFilePath); } catch { }
        }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsCredentialsAccurately()
    {
        var store = new DpapiDeviceStore(_tempFilePath);
        var record = new PairedDeviceRecord
        {
            DeviceId = Guid.NewGuid(),
            DeviceLabel = "Pixel 8 Pro",
            PairedAt = DateTimeOffset.UtcNow,
            SessionToken = "secret_session_token_12345"
        };

        await store.SaveAsync(record);

        Assert.True(File.Exists(_tempFilePath));

        // Verify file is encrypted on disk: plaintext secret and label should NOT appear as raw strings
        string rawFileContent = Encoding.UTF8.GetString(File.ReadAllBytes(_tempFilePath));
        Assert.DoesNotContain("secret_session_token_12345", rawFileContent);
        Assert.DoesNotContain("Pixel 8 Pro", rawFileContent);

        // Load decrypted record
        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(record.DeviceId, loaded.DeviceId);
        Assert.Equal(record.DeviceLabel, loaded.DeviceLabel);
        Assert.Equal(record.SessionToken, loaded.SessionToken);
    }

    [Fact]
    public async Task ClearAsync_DeletesCredentials()
    {
        var store = new DpapiDeviceStore(_tempFilePath);
        var record = new PairedDeviceRecord
        {
            DeviceId = Guid.NewGuid(),
            DeviceLabel = "iPhone 15",
            SessionToken = "token_abc"
        };

        await store.SaveAsync(record);
        Assert.True(File.Exists(_tempFilePath));

        await store.ClearAsync();

        Assert.False(File.Exists(_tempFilePath));
        var loaded = await store.LoadAsync();
        Assert.Null(loaded);
    }
}
