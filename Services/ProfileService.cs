using System.IO;
using System.Text;
using System.Text.Json;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Services;

public class ProfileService
{
    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GLM_Manager",
        "profiles");

    public static List<Profile> LoadAll()
    {
        var profiles = new List<Profile>();
        try
        {
            EnsureProfilesDirectoryExists();
            TryImportFromGlrManager();
            TryMigrateProfilesFromOldVersion();
            LoadProfilesFromDirectory(profiles);
            if (profiles.Count == 0) return CreateDefaultProfile(profiles);
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.LoadAll", ex);
        }

        return profiles;
    }

    private static void EnsureProfilesDirectoryExists()
    {
        if (!Directory.Exists(ProfilesDir)) Directory.CreateDirectory(ProfilesDir);
    }

    private static List<Profile> CreateDefaultProfile(List<Profile> profiles)
    {
        var defaultProfile = new Profile { Name = "default" };
        Save(defaultProfile);
        profiles.Add(defaultProfile);
        return profiles;
    }

    private static void LoadProfilesFromDirectory(List<Profile> profiles)
    {
        foreach (var file in Directory.GetFiles(ProfilesDir, "*.json"))
            try
            {
                var profile = DeserializeProfile(File.ReadAllText(file, Encoding.UTF8));
                if (profile != null) profiles.Add(profile);
            }
            catch (Exception ex)
            {
                LogService.LogError("ProfileService.LoadFromDir", ex);
            }
    }

    public static Profile? Load(string profileName)
    {
        try
        {
            var filePath = GetProfileFilePath(profileName);
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            return DeserializeProfile(json);
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.Load", ex);
            return null;
        }
    }

    public static void Save(Profile profile)
    {
        try
        {
            EnsureProfilesDirectoryExists();
            var filePath = GetProfileFilePath(profile.Name);
            var json = SerializeProfile(profile);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.Save", ex);
        }
    }

    public static void Delete(string profileName)
    {
        try
        {
            if (string.Equals(profileName, "default", StringComparison.OrdinalIgnoreCase))
                return;

            var filePath = GetProfileFilePath(profileName);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.Delete", ex);
        }
    }

    public static void Export(Profile profile, string destinationPath)
    {
        try
        {
            var json = SerializeProfile(profile);
            File.WriteAllText(destinationPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.Export", ex);
        }
    }

    public static Profile? Import(string sourcePath)
    {
        try
        {
            var json = File.ReadAllText(sourcePath, Encoding.UTF8);
            return DeserializeProfile(json);
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.Import", ex);
            return null;
        }
    }

    private static string GetProfileFilePath(string profileName)
    {
        var sanitizedName = SanitizeFileName(profileName);
        return Path.Combine(ProfilesDir, $"{sanitizedName}.json");
    }

    private static Profile? DeserializeProfile(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Profile>(json);
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.DeserializeProfile", ex);
            return null;
        }
    }

    private static string SerializeProfile(Profile profile)
    {
        return JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string([.. name.Select(c => invalidChars.Contains(c) ? '_' : c)]);
        sanitized = sanitized.Replace("..", "_");
        return Path.GetFileName(sanitized);
    }

    private static void TryImportFromGlrManager()
    {
        try
        {
            var existingFiles = Directory.GetFiles(ProfilesDir, "*.json");
            if (existingFiles.Length > 0) return;

            var glrProfilesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GLR_Manager",
                "Profiles");

            if (!Directory.Exists(glrProfilesDir)) return;

            var glrFiles = Directory.GetFiles(glrProfilesDir, "*.json");
            if (glrFiles.Length == 0) return;

            foreach (var file in glrFiles)
                try
                {
                    var fileName = Path.GetFileName(file);
                    var destPath = Path.Combine(ProfilesDir, SanitizeFileName(fileName));

                    if (!File.Exists(destPath))
                        File.Copy(file, destPath);
                }
                catch (Exception ex)
                {
                    LogService.LogError("ProfileService.ImportGlrFile", ex);
                }
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.TryImportFromGlrManager", ex);
        }
    }

    private static void TryMigrateProfilesFromOldVersion()
    {
        try
        {
            if (!Directory.Exists(ProfilesDir)) return;

            var migratedFlag = Path.Combine(ProfilesDir, ".migrated");
            if (File.Exists(migratedFlag)) return;

            var filesToMigrate = Directory.GetFiles(ProfilesDir, "*.json");
            var anyMigrated = false;

            foreach (var file in filesToMigrate)
                try
                {
                    var rc3Json = File.ReadAllText(file, Encoding.UTF8);
                    using var doc = JsonDocument.Parse(rc3Json);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("games", out var gamesArray) ||
                        gamesArray.ValueKind != JsonValueKind.Array ||
                        gamesArray.GetArrayLength() == 0) continue;

                    var firstGame = gamesArray[0];
                    if (firstGame.ValueKind != JsonValueKind.Object ||
                        !firstGame.TryGetProperty("id", out _)) continue;

                    var profileName = root.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? "default"
                        : "default";

                    var profile = new Profile
                    {
                        Name = profileName,
                        Games =
                        [
                            .. gamesArray.EnumerateArray()
                                .Select(gameElem => new Game
                                {
                                    AppId = gameElem.TryGetProperty("id", out var idProp) ? idProp.ToString() : string.Empty,
                                    Name = gameElem.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty,
                                    Type = gameElem.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "Game" : "Game"
                                })
                                .Where(g => !string.IsNullOrEmpty(g.AppId))
                        ]
                    };

                    Save(profile);
                    anyMigrated = true;
                }
                catch (Exception ex)
                {
                    LogService.LogError("ProfileService.MigrateFile", ex);
                }

            if (anyMigrated)
                File.WriteAllText(migratedFlag, string.Empty);
        }
        catch (Exception ex)
        {
            LogService.LogError("ProfileService.TryMigrateProfiles", ex);
        }
    }
}