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
        public bool UseModelDefaultThinking { get; set; }
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
        OpenAICompatible
    }

    public enum SearchEngineType
    {
        Default,
        Exa,
        Firecrawl,
        Parallel,
        Tavily
    }

    public static class ProviderConstants
    {
        // Reasoning effort used when "thinking mode" is ON.
        // Group 1 (OpenAI, DeepSeek, HuggingFace, OpenRouter) supports "max".
        public const string OpenAIReasoningEffortMax = "max";

        // Everything else (Groq, Cerebras and any other provider) uses "high".
        public const string OpenAIReasoningEffortHigh = "high";

        // Reasoning effort used when "thinking mode" is OFF.
        // Matches the documented Gemini default so all OpenAI-compatible providers stay consistent.
        public const string OpenAIReasoningEffortDefault = "medium";

        // Gemini thinking_level ("minimal"|"low"|"medium"|"high").
        public const string GeminiThinkingLevel = "high";
        public const string GeminiThinkingLevelDefault = "medium";
    }

    public class ModelProfile
    {
        public string ProfileName { get; set; } = "New Profile";
        public AiProviderType ProviderType { get; set; } = AiProviderType.Gemini;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public int MaxTokens { get; set; } = 1024;

        // Web Search integration
        public SearchEngineType SearchEngine { get; set; } = SearchEngineType.Default;        
        public string ExaApiKey { get; set; } = string.Empty;        
        public string FirecrawlApiKey { get; set; } = string.Empty;
        public string ParallelApiKey { get; set; } = string.Empty;
        public string TavilyApiKey { get; set; } = string.Empty;

        // Fallback settings
        public bool UseAsFallback { get; set; } = true;
    }
}
