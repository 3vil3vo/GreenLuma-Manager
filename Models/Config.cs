using System.Runtime.Serialization;

namespace GreenLuma_Manager.Models;

[DataContract]
public class Config
{
    [DataMember] public string SteamPath { get; set; } = string.Empty;

    [DataMember] public string GreenLumaPath { get; set; } = string.Empty;

    [DataMember] public bool NoHook { get; set; }

    [DataMember] public bool DisableUpdateCheck { get; set; }

    [DataMember] public bool AutoUpdate { get; set; } = true;

    [DataMember] public string LastProfile { get; set; } = "default";

    [DataMember] public bool CheckUpdate { get; set; } = true;

    [DataMember] public bool ReplaceSteamAutostart { get; set; }

    [DataMember] public bool FirstRun { get; set; } = true;

    [DataMember] public string SteamApiKey { get; set; } = string.Empty;
}