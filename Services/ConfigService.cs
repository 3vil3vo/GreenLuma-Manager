using System.IO;
using System.Text;
using System.Text.Json;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Utilities;

namespace GreenLuma_Manager.Services;

public class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GLM_Manager");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");


    public static Config Load()
    {
        try
        {
            EnsureConfigDirectoryExists();

            if (!File.Exists(ConfigPath)) return CreateDefaultConfig();

            var configJson = File.ReadAllText(ConfigPath, Encoding.UTF8);

            var migratedConfig = TryMigrateFromOldVersion(configJson);
            if (migratedConfig != null) return migratedConfig;

            return DeserializeConfig(configJson) ?? new Config();
        }
        catch (Exception ex)
        {
            LogService.LogError("ConfigService.Load", ex);
            return new Config();
        }
    }

    private static void EnsureConfigDirectoryExists()
    {
        if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
    }

    private static Config CreateDefaultConfig()
    {
        var config = new Config();
        var (steamPath, greenLumaPath) = PathDetector.DetectPaths();

        config.SteamPath = steamPath;
        config.GreenLumaPath = greenLumaPath;

        Save(config);
        return config;
    }

    private static Config? DeserializeConfig(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Config>(json);
        }
        catch (Exception ex)
        {
            LogService.LogError("ConfigService.DeserializeConfig", ex);
            return null;
        }
    }

    private static Config? TryMigrateFromOldVersion(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("steam_path", out _) && root.TryGetProperty("SteamPath", out _)) return null;

            if (root.TryGetProperty("steam_path", out _))
            {
                var config = new Config
                {
                    SteamPath = GetStringOrDefault(root, "steam_path", string.Empty),
                    GreenLumaPath = GetStringOrDefault(root, "greenluma_path", string.Empty),
                    NoHook = GetBoolOrDefault(root, "no_hook", false),
                    DisableUpdateCheck = GetBoolOrDefault(root, "disable_update_check", false),
                    AutoUpdate = GetBoolOrDefault(root, "auto_update", true),
                    LastProfile = GetStringOrDefault(root, "last_profile", "default"),
                    CheckUpdate = GetBoolOrDefault(root, "check_update", true),
                    ReplaceSteamAutostart = GetBoolOrDefault(root, "replace_steam_autostart", false),
                    PrefetchAppList = GetBoolOrDefault(root, "prefetch_app_list", false),
                    SteamApiKey = GetStringOrDefault(root, "steam_api_key", string.Empty),
                    FirstRun = false
                };

                SerializeConfig(config);
                return config;
            }

            return null;
        }
        catch (Exception ex)
        {
            LogService.LogError("ConfigService.TryMigrate", ex);
            return null;
        }
    }

    private static string GetStringOrDefault(JsonElement root, string propertyName, string defaultValue)
    {
        return root.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? defaultValue : defaultValue;
    }

    private static bool GetBoolOrDefault(JsonElement root, string propertyName, bool defaultValue)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? prop.GetBoolean()
            : defaultValue;
    }

    public static void Save(Config config)
    {
        try
        {
            EnsureConfigDirectoryExists();

            var json = SerializeConfig(config);
            File.WriteAllText(ConfigPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LogService.LogError("ConfigService.Save", ex);
        }
    }

    private static string SerializeConfig(Config config)
    {
        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void WipeData()
    {
        try
        {
            AutostartManager.CleanupAll();

            if (Directory.Exists(ConfigDir)) Directory.Delete(ConfigDir, true);
        }
        catch (Exception ex)
        {
            LogService.LogError("ConfigService.WipeData", ex);
        }
    }
}