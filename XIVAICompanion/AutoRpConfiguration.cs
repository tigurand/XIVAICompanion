using System;

namespace XIVAICompanion
{
    [Serializable]
    public class AutoRpConfiguration
    {
        public string TargetName { get; set; } = "";
        public bool AutoTarget { get; set; } = false;
        public float ResponseDelay { get; set; } = 1.5f;
        public bool ReplyInOriginalChannel { get; set; } = true;
        public bool AutoReplyToAllTells { get; set; } = false;
        public bool AutoReplyToParty { get; set; } = false;
        public float InitialResponseDelaySeconds { get; set; } = 1.5f;

        // Channels
        public bool ListenSay { get; set; } = true;
        public bool ListenTell { get; set; } = true;
        public bool ListenShout { get; set; } = false;
        public bool ListenYell { get; set; } = false;
        public bool ListenParty { get; set; } = true;
        public bool ListenCrossParty { get; set; } = true;
        public bool ListenAlliance { get; set; } = false;
        public bool ListenFreeCompany { get; set; } = false;
        public bool ListenNoviceNetwork { get; set; } = false;
        public bool ListenPvPTeam { get; set; } = false;

        // Linkshell Channels
        public bool[] ListenLs { get; set; } = new bool[8];
        public bool[] ListenCwls { get; set; } = new bool[8];
    }
}