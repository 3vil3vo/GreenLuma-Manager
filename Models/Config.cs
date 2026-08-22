using System.Runtime.Serialization;

namespace GreenLuma_Manager.Models;

public enum GreenLumaLaunchMode
{
    Normal,
    InjectorStealth,
    FullStealth
}

public enum FullStealthVariant
{
    Standard,
    SteamFamilies
}

[DataContract]
public class Config
{
    [DataMember] public string SteamPath { get; set; } = string.Empty;

    [DataMember] public string GreenLumaPath { get; set; } = string.Empty;

    [DataMember] public bool NoHook { get; set; }

    [DataMember] public GreenLumaLaunchMode LaunchMode { get; set; }

    [DataMember] public FullStealthVariant FullStealthVariant { get; set; }

    [DataMember] public bool DisableUpdateCheck { get; set; }

    [DataMember] public bool AutoUpdate { get; set; } = true;

    [DataMember] public string LastProfile { get; set; } = "default";

    [DataMember] public bool CheckUpdate { get; set; } = true;

    [DataMember] public bool ReplaceSteamAutostart { get; set; }

    [DataMember] public bool PrefetchAppList { get; set; }

    [DataMember] public bool StartSteamMinimized { get; set; }

    [DataMember] public bool DisableGreenLumaVersionNotice { get; set; }

    [DataMember] public bool GreenLumaVersionPromptShown { get; set; }

    [DataMember] public string GreenLumaVersionOverride { get; set; } = string.Empty;

    [DataMember] public bool CheckGreenLumaUpdates { get; set; }

    [DataMember] public bool GreenLumaUpdateCheckAutoDetectDone { get; set; }

    [DataMember] public int GreenLumaUpdateCheckFailedAttempts { get; set; }

    [DataMember] public bool FirstRun { get; set; } = true;

    [DataMember] public string SteamApiKey { get; set; } = string.Empty;

    [DataMember] public double WindowWidth { get; set; }

    [DataMember] public double WindowHeight { get; set; }
}
