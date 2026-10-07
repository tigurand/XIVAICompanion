using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace XIVAICompanion.Models
{
    // Common AI Models
    public class SavedConversationCache
    {
        [JsonProperty("lruKeys")] public List<string> LruKeys { get; set; } = new();
        [JsonProperty("cache")] public Dictionary<string, List<Content>> Cache { get; set; } = new();
    }

    public class Content
    {
        [JsonProperty("role")] public string Role { get; set; } = string.Empty;
        [JsonProperty("parts")] public List<Part> Parts { get; set; } = new();
    }

    public class Part
    {
        [JsonProperty("text")] public string Text { get; set; } = string.Empty;
    }

    public class ApiResult
    {
        public bool WasSuccessful { get; set; }
        public JObject? ResponseJson { get; set; }
        public HttpResponseMessage? HttpResponse { get; set; }
        public Exception? Exception { get; set; }
        public int ResponseTokensUsed { get; set; }
        public int? ThinkingBudgetUsed { get; set; }
    }

    // Gemini Interactions API Models
    public class InteractionRequest
    {
        [JsonProperty("model")] public string Model { get; set; } = string.Empty;
        [JsonProperty("input")] public List<InteractionInput> Input { get; set; } = new();
        [JsonProperty("generation_config", NullValueHandling = NullValueHandling.Ignore)] public InteractionGenerationConfig? GenerationConfig { get; set; }
        // Custom safety settings are not yet supported by the Interactions API (the field is rejected with "invalid_request"). Kept for later use (if Google implemented it).
        [JsonProperty("safety_settings", NullValueHandling = NullValueHandling.Ignore)] public List<SafetySetting>? SafetySettings { get; set; }
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)] public List<Tool>? Tools { get; set; }
    }

    public class InteractionInput
    {
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        [JsonProperty("content")] public List<InteractionContentBlock> Content { get; set; } = new();
    }

    public class InteractionContentBlock
    {
        [JsonProperty("type")] public string Type { get; set; } = "text";
        [JsonProperty("text")] public string Text { get; set; } = string.Empty;
    }

    public class InteractionGenerationConfig
    {
        [JsonProperty("max_output_tokens", NullValueHandling = NullValueHandling.Ignore)] public int? MaxOutputTokens { get; set; }
        [JsonProperty("thinking_level", NullValueHandling = NullValueHandling.Ignore)] public string? ThinkingLevel { get; set; }
        [JsonProperty("thinking_summaries", NullValueHandling = NullValueHandling.Ignore)] public string? ThinkingSummaries { get; set; }
    }

    public class SafetySetting
    {
        [JsonProperty("category")] public string Category { get; set; } = string.Empty;
        [JsonProperty("threshold")] public string Threshold { get; set; } = string.Empty;
    }

    public class Tool
    {
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)] public string? Type { get; set; }
        [JsonProperty("function_declarations", NullValueHandling = NullValueHandling.Ignore)] public List<FunctionDeclaration>? FunctionDeclarations { get; set; }
    }

    public class FunctionDeclaration
    {
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("description")] public string Description { get; set; } = string.Empty;
        [JsonProperty("parameters")] public object? Parameters { get; set; }
    }

    public class GoogleSearch { }
    public class UrlContext { }
}
