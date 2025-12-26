using System;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using XIVAICompanion.Models;

namespace XIVAICompanion.Providers
{
    public class ProviderRequest
    {
        public string Model { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public List<Content> ConversationHistory { get; set; } = new();
        public int MaxTokens { get; set; }
        public double Temperature { get; set; }
        public bool UseWebSearch { get; set; }
        public int? ThinkingBudget { get; set; }
        public bool ShowThoughts { get; set; }
        public bool IsThinkingEnabled { get; set; }
    }

    public class ProviderResult
    {
        public bool WasSuccessful { get; set; }
        public string? ResponseText { get; set; }
        public string? RawResponse { get; set; }
        public JObject? ResponseJson { get; set; }
        public HttpResponseMessage? HttpResponse { get; set; }
        public Exception? Exception { get; set; }

        // Metrics
        public int PromptTokens { get; set; }
        public int ResponseTokens { get; set; }
        public int TotalTokens { get; set; }
        
        // Context for logging/UI
        public string ModelUsed { get; set; } = string.Empty;
        public int ResponseTokensUsed { get; set; }
        public int? ThinkingBudgetUsed { get; set; }

        // Provider-specific metadata for better error reporting
        public string? FinishReason { get; set; }
        public string? BlockReason { get; set; }
        public long ResponseTimeMs { get; set; }
    }

    public enum AiProviderType
    {
        Gemini,
        OpenAiCompatible
    }

    public static class ProviderConstants
    {
        public const string OpenAIReasoningEffort = "High";
    }

    public class ModelProfile
    {
        public string ProfileName { get; set; } = "New Profile";
        public AiProviderType ProviderType { get; set; } = AiProviderType.Gemini;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public int MaxTokens { get; set; } = 1024;

        // Tavily Web Search integration
        public bool UseTavilyInstead { get; set; } = false;
        public string TavilyApiKey { get; set; } = string.Empty;
    }
}
