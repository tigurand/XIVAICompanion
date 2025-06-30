using Dalamud.Configuration;
using Dalamud.Plugin;

namespace XIVAICompanion
{
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; }        
        public string ApiKey { get; set; } = "";
        public int MaxTokens { get; set; }

        public string AIName { get; set; } = "AI";
        public bool LetSystemPromptHandleAIName { get; set; }
        public int AddressingMode { get; set; }
        public string CustomUserName { get; set; } = "Adventurer";
        public string SystemPrompt { get; set; } = "";

        // The lightest and fastest model for simple, quick chat.
        public const string fastModel = "gemini-2.5-flash-lite-preview-06-17";

        // The more powerful model for complex tasks like Google Search that need higher accuracy.
        public const string smartModel = "gemini-2.5-flash";

        public bool RemoveLineBreaks { get; set; }
        public bool ShowAdditionalInfo { get; set; }
        public bool ShowPrompt { get; set; }
        public bool GreetOnLogin { get; set; } = true;
        public bool EnableConversationHistory { get; set; } = true;        

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