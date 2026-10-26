using System.Text.Json;

namespace TeamsRemote.Companion.Settings;

public sealed class CompanionSettings
{
    public bool EnablePhoneAccess { get; set; } = false;
    public string? SelectedAdapterId { get; set; }
    public string? SelectedIpAddress { get; set; }
}

public sealed class CompanionSettingsManager
{
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CompanionSettingsManager(string? customFilePath = null)
    {
        _settingsFilePath = customFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TeamsRemote",
            "settings.json");
    }

    public async Task<CompanionSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                using var stream = File.OpenRead(_settingsFilePath);
                var settings = await JsonSerializer.DeserializeAsync<CompanionSettings>(stream, JsonOptions, cancellationToken);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Fall back to default on read/parse error
        }

        return new CompanionSettings();
    }

    public async Task SaveAsync(CompanionSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            string dir = Path.GetDirectoryName(_settingsFilePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var stream = File.Create(_settingsFilePath);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }
        catch
        {
            // Suppress persistence errors in constrained environments
        }
    }
}
