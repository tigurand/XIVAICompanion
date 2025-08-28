using System;

namespace XIVAICompanion.Configurations
{
    [Serializable]
    public class AutoRpConfiguration
    {
        public string TargetName { get; set; } = "";
        public bool AutoTarget { get; set; } = false;
        public float ResponseDelay { get; set; } = 1.5f;
        public bool ReplyInOriginalChannel { get; set; } = true;
        public bool ReplyInSpecificChannel { get; set; } = false;
        public int SpecificReplyChannel { get; set; } = 0;
        public bool AutoReplyToAllTells { get; set; } = false;
        public bool IsOpenListenerModeEnabled { get; set; } = false;
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
        public bool[] ListenLs { get; set; } = new bool[8];
        public bool[] ListenCwls { get; set; } = new bool[8];

        // Open Channels
        public bool OpenListenerListenSay { get; set; } = false;
        public bool OpenListenerListenTell { get; set; } = false;
        public bool OpenListenerListenShout { get; set; } = false;
        public bool OpenListenerListenYell { get; set; } = false;
        public bool OpenListenerListenParty { get; set; } = false;
        public bool OpenListenerListenCrossParty { get; set; } = false;
        public bool OpenListenerListenAlliance { get; set; } = false;
        public bool OpenListenerListenFreeCompany { get; set; } = false;
        public bool OpenListenerListenNoviceNetwork { get; set; } = false;
        public bool OpenListenerListenPvPTeam { get; set; } = false;
        public bool[] OpenListenerListenLs { get; set; } = new bool[8];
        public bool[] OpenListenerListenCwls { get; set; } = new bool[8];
    }
}