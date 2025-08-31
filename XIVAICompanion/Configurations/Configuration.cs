using Dalamud.Configuration;
using Dalamud.Plugin;
using System.Numerics;

namespace XIVAICompanion.Configurations
{
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; }

        // General Settings
        public string ApiKey { get; set; } = "";
        public int MaxTokens { get; set; } = 1024;
        public string AImodel { get; set; } = "gemini-2.5-flash";

        // Persona Settings
        public string AIName { get; set; } = "AI";
        public bool LetSystemPromptHandleAIName { get; set; } = false;
        public int AddressingMode { get; set; } = 0;
        public string CustomUserName { get; set; } = "Adventurer";
        public string SystemPrompt { get; set; } = "";
        public float Temperature { get; set; } = 1.0f;
        public string MinionToReplace { get; set; } = string.Empty;
        public string NpcGlamourerDesignGuid { get; set; } = string.Empty;

        // Behavior Settings
        public bool GreetOnLogin { get; set; } = true;
        public string LoginGreetingPrompt { get; set; } = "I'm back to Eorzea, please greet me.";
        public bool EnableConversationHistory { get; set; } = true;
        public int ConversationHistoryLimit { get; set; } = 10;
        public bool EnableAutoFallback { get; set; } = true;
        public bool EnableInGameContext { get; set; } = true;

        // Chat Log Display Settings
        public bool ShowPrompt { get; set; } = true;
        public bool RemoveLineBreaks { get; set; } = true;
        public bool ShowAdditionalInfo { get; set; } = false;
        public bool UseCustomColors { get; set; } = false;
        public Vector4 ForegroundColor { get; set; } = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);

        // Chat Window Settings
        public bool SaveChatHistoryToFile { get; set; } = false;
        public int SessionsToLoad { get; set; } = 1;
        public int DaysToKeepLogs { get; set; } = 30;

        //Auto RP Settings
        public AutoRpConfiguration AutoRpConfig { get; set; } = new();

        // Mode Toggles
        public bool SearchMode { get; set; } = false;
        public bool ThinkMode { get; set; } = false;

        public bool IsDevModeEnabled { get; set; } = false;

        private IDalamudPluginInterface pluginInterface = null!;

        public void Initialize(IDalamudPluginInterface pInterface)
        {
            pluginInterface = pInterface;
        }

        public void Save()
        {
            pluginInterface.SavePluginConfig(this);
        }
    }
}