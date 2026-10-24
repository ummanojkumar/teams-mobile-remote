namespace TeamsRemote.Core.Security;

public interface IDeviceCredentialStore
{
    Task SaveAsync(PairedDeviceRecord record, CancellationToken cancellationToken = default);
    Task<PairedDeviceRecord?> LoadAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
