using Dalamud.Configuration;
using Dalamud.Plugin;
using System.Numerics;

namespace XIVAICompanion
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
        public bool LetSystemPromptHandleAIName { get; set; } = true;
        public int AddressingMode { get; set; } = 0;
        public string CustomUserName { get; set; } = "Adventurer";
        public string SystemPrompt { get; set; } = "";

        // Behavior Settings
        public bool GreetOnLogin { get; set; } = true;
        public string LoginGreetingPrompt { get; set; } = "I'm back to Eorzea, please greet me.";
        public bool EnableConversationHistory { get; set; } = true;
        public bool EnableAutoFallback { get; set; } = true;

        // Log Display Settings
        public bool ShowPrompt { get; set; } = true;
        public bool RemoveLineBreaks { get; set; } = true;
        public bool ShowAdditionalInfo { get; set; } = false;
        public bool UseCustomColors { get; set; } = false;
        public Vector4 ForegroundColor { get; set; } = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);

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