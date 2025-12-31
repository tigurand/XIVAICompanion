using System;

namespace XIVAICompanion.Providers
{
    internal readonly struct OpenAiCompatibleHostInfo
    {
        public readonly bool IsOpenAi;
        public readonly bool IsOpenRouter;
        public readonly bool IsGroq;
        public readonly bool IsHuggingFace;

        public OpenAiCompatibleHostInfo(string? baseUrl)
        {
            string baseUrlLower = (baseUrl ?? string.Empty).ToLowerInvariant();
            IsOpenAi = baseUrlLower.Contains("openai.com");
            IsOpenRouter = baseUrlLower.Contains("openrouter.ai");
            IsGroq = baseUrlLower.Contains("groq.com");
            IsHuggingFace = baseUrlLower.Contains("huggingface.co");
        }

        public bool UsesResponsesApi => IsOpenAi || IsHuggingFace;
    }

    internal static class OpenAiCompatibleRouting
    {
        public static string BuildCompletionsEndpoint(string baseUrl, OpenAiCompatibleHostInfo host)
        {
            return host.UsesResponsesApi
                ? $"{baseUrl}/responses"
                : $"{baseUrl}/chat/completions";
        }
    }
}

