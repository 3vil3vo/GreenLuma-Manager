namespace GreenLuma_Manager.Models;

public class GreenLumaVersionInfo
{
    public const string ForumUrl = "https://cs.rin.ru/forum/viewtopic.php?f=29&t=103709";

    public required string LatestVersionTag { get; init; }
    public Version? LatestSemanticVersion { get; init; }
    public Version? InstalledVersion { get; init; }
    public DateTime CheckedAt { get; init; }
    public bool CheckSucceeded { get; init; }
    public string? ErrorMessage { get; init; }

    public bool UpdateAvailable =>
        LatestSemanticVersion is not null &&
        InstalledVersion is not null &&
        LatestSemanticVersion > InstalledVersion;
}
