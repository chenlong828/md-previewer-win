using System.IO;
using System.Text.Json;

namespace MDPreview.Win.Settings;

/// <summary>用户偏好：主题、缩放、缩略图、最近文件。单文件 JSON，跨窗口共享。</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "system";        // system | light | dark
    public double Zoom { get; set; } = 1.0;
    public bool MinimapVisible { get; set; } = true;
    public bool CodeLineNumbers { get; set; } = true;
    public List<string> Recent { get; set; } = [];
    public string? LastFolder { get; set; }

    public const int MaxRecent = 10;

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppInfo.SettingsPath))
            {
                var json = File.ReadAllText(AppInfo.SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"settings load failed: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.UserDataDirectory);
            File.WriteAllText(AppInfo.SettingsPath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Log.Warn($"settings save failed: {ex.Message}");
        }
    }

    public void AddRecent(string path)
    {
        Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
        Save();
    }

    public void RemoveRecent(string path)
    {
        if (Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0) Save();
    }

    public double ClampZoom(double scale) => Math.Min(3.0, Math.Max(0.5, scale));
}
