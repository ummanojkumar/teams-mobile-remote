using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeamsRemote.Core.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiDeviceStore : IDeviceCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TeamsRemote.DeviceProtection.v1");
    private readonly string _storagePath;
    private readonly object _lock = new();

    public DpapiDeviceStore(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _storagePath = customPath;
        }
        else
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(localAppData, "TeamsRemote");
            _storagePath = Path.Combine(dir, "device.dat");
        }
    }

    public Task SaveAsync(PairedDeviceRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            string? directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(record);
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(json);
            byte[] encryptedBytes = ProtectedData.Protect(plaintextBytes, Entropy, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(_storagePath, encryptedBytes);
        }

        return Task.CompletedTask;
    }

    public Task<PairedDeviceRecord?> LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!File.Exists(_storagePath))
            {
                return Task.FromResult<PairedDeviceRecord?>(null);
            }

            try
            {
                byte[] encryptedBytes = File.ReadAllBytes(_storagePath);
                byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(decryptedBytes);

                var record = JsonSerializer.Deserialize<PairedDeviceRecord>(json);
                return Task.FromResult(record);
            }
            catch
            {
                // Corrupted or from different user/machine session - treat as no device
                return Task.FromResult<PairedDeviceRecord?>(null);
            }
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (File.Exists(_storagePath))
            {
                try
                {
                    File.Delete(_storagePath);
                }
                catch
                {
                    // Ignore deletion error if file already gone
                }
            }
        }

        return Task.CompletedTask;
    }
}
