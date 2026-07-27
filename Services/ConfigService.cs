using System.IO;
using System.Text;
using System.Text.Json;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Utilities;
using Newtonsoft.Json.Linq;

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
            Logger.Error(ex, "ConfigService.Load");
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
            Logger.Error(ex, "ConfigService.DeserializeConfig");
            return null;
        }
    }

    private static Config? TryMigrateFromOldVersion(string configJson)
    {
        try
        {
            var jsonData = JObject.Parse(configJson);

            if (jsonData["steam_path"] == null && jsonData["SteamPath"] != null) return null;

            if (jsonData["steam_path"] != null)
            {
                var config = new Config
                {
                    SteamPath = jsonData["steam_path"]?.ToString() ?? string.Empty,
                    GreenLumaPath = jsonData["greenluma_path"]?.ToString() ?? string.Empty,
                    NoHook = jsonData["no_hook"]?.ToObject<bool>() ?? false,
                    DisableUpdateCheck = jsonData["disable_update_check"]?.ToObject<bool>() ?? false,
                    AutoUpdate = jsonData["auto_update"]?.ToObject<bool>() ?? true,
                    LastProfile = jsonData["last_profile"]?.ToString() ?? "default",
                    CheckUpdate = jsonData["check_update"]?.ToObject<bool>() ?? true,
                    ReplaceSteamAutostart = jsonData["replace_steam_autostart"]?.ToObject<bool>() ?? false,
                    PrefetchAppList = jsonData["prefetch_app_list"]?.ToObject<bool>() ?? false,
                    FirstRun = false
                };

                SerializeConfig(config);
                return config;
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ConfigService.TryMigrate");
            return null;
        }
    }

    public static void Save(Config config)
    {
        try
        {
            EnsureConfigDirectoryExists();

            var json = SerializeConfig(config);
            AtomicFile.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ConfigService.Save");
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
            Logger.Error(ex, "ConfigService.WipeData");
        }
    }
}