using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniAppBox;

/// <summary>电脑端设置（RootDir/settings.json）：设备名、同步服务开关。</summary>
public sealed class AppSettings
{
    [JsonPropertyName("deviceName")] public string DeviceName { get; set; } = "";
    [JsonPropertyName("syncEnabled")] public bool SyncEnabled { get; set; } = true;

    private static string FilePath => Path.Combine(AppRepo.RootDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* 损坏时回默认 */ }
        return new AppSettings { DeviceName = DefaultDeviceName() };
    }

    public void Save()
    {
        Directory.CreateDirectory(AppRepo.RootDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    public string EffectiveDeviceName => string.IsNullOrWhiteSpace(DeviceName) ? DefaultDeviceName() : DeviceName.Trim();

    public static string DefaultDeviceName() => Environment.MachineName;
}
