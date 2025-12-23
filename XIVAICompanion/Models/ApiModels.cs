using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace XIVAICompanion.Models
{
    // Common AI Models
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

    // Gemini-specific Models
    public class GeminiRequest
    {
        [JsonProperty("contents")] public List<Content> Contents { get; set; } = new();
        [JsonProperty("safetySettings")] public List<SafetySetting> SafetySettings { get; set; } = new();
        [JsonProperty("generationConfig")] public GenerationConfig GenerationConfig { get; set; } = new();
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)] public List<Tool>? Tools { get; set; }
    }

    public class SafetySetting
    {
        [JsonProperty("category")] public string Category { get; set; } = string.Empty;
        [JsonProperty("threshold")] public string Threshold { get; set; } = string.Empty;
    }

    public class GenerationConfig
    {
        [JsonProperty("maxOutputTokens")] public int MaxOutputTokens { get; set; }
        [JsonProperty("temperature")] public double Temperature { get; set; }
        [JsonProperty("thinkingConfig", NullValueHandling = NullValueHandling.Ignore)]
        public ThinkingConfig? ThinkingConfig { get; set; }
    }

    public class ThinkingConfig
    {
        [JsonProperty("thinkingBudget")]
        public int ThinkingBudget { get; set; }
        [JsonProperty("includeThoughts")] public bool IncludeThoughts { get; set; }
    }

    public class Tool
    {
        [JsonProperty("googleSearch", NullValueHandling = NullValueHandling.Ignore)]
        public GoogleSearch? GoogleSearch { get; set; }

        [JsonProperty("urlContext", NullValueHandling = NullValueHandling.Ignore)]
        public UrlContext? UrlContext { get; set; }

        [JsonProperty("functionDeclarations", NullValueHandling = NullValueHandling.Ignore)]
        public List<FunctionDeclaration>? FunctionDeclarations { get; set; }
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
