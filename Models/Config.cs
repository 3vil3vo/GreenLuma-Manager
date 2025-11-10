using System.Runtime.Serialization;

namespace GreenLuma_Manager.Models
{
    [DataContract]
    public class Config
    {
        public Config()
        {
            SteamPath = string.Empty;
            GreenLumaPath = string.Empty;
            NoHook = false;
            DisableUpdateCheck = false;
            AutoUpdate = true;
            LastProfile = "default";
            CheckUpdate = true;
            ReplaceSteamAutostart = false;
            FirstRun = true;
            Language = string.Empty;
        }

        [DataMember] public string SteamPath { get; set; }

        [DataMember] public string GreenLumaPath { get; set; }

        [DataMember] public bool NoHook { get; set; }

        [DataMember] public bool DisableUpdateCheck { get; set; }

        [DataMember] public bool AutoUpdate { get; set; }

        [DataMember] public string LastProfile { get; set; }

        [DataMember] public bool CheckUpdate { get; set; }

        [DataMember] public bool ReplaceSteamAutostart { get; set; }

        [DataMember] public bool FirstRun { get; set; }

        [DataMember] public string Language { get; set; }
    }
}
