namespace XIVAICompanion
{
    public class Persona
    {
        public string AIName { get; set; } = "AI";
        public bool LetSystemPromptHandleAIName { get; set; } = true;
        public int AddressingMode { get; set; }
        public string CustomUserName { get; set; } = "Adventurer";
        public string SystemPrompt { get; set; } = "";
    }
}