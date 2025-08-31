namespace XIVAICompanion.Configurations
{
    public class PersonaConfiguration
    {
        public string AIName { get; set; } = "AI";
        public bool LetSystemPromptHandleAIName { get; set; } = false;
        public int AddressingMode { get; set; } = 0;
        public string CustomUserName { get; set; } = "Adventurer";
        public string SystemPrompt { get; set; } = "";
        public float Temperature { get; set; } = 1.0f;
        public string MinionToReplace { get; set; } = string.Empty;
        public string NpcGlamourerDesignGuid { get; set; } = string.Empty;
    }
}