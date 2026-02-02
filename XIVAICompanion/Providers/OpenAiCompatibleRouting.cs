using System;

namespace XIVAICompanion.Providers
{
    internal readonly struct OpenAICompatibleHostInfo
    {
        public readonly bool IsOpenAi;
        public readonly bool IsOpenRouter;
        public readonly bool IsGroq;
        public readonly bool IsHuggingFace;
        public readonly bool IsCerebras;

        public OpenAICompatibleHostInfo(string? baseUrl)
        {
            string baseUrlLower = (baseUrl ?? string.Empty).ToLowerInvariant();
            IsOpenAi = baseUrlLower.Contains("openai.com");
            IsOpenRouter = baseUrlLower.Contains("openrouter.ai");
            IsGroq = baseUrlLower.Contains("groq.com");
            IsHuggingFace = baseUrlLower.Contains("huggingface.co");
            IsCerebras = baseUrlLower.Contains("cerebras.ai");
        }

        public bool UsesResponsesApi => IsOpenAi || IsHuggingFace;
    }

    internal readonly struct OpenAICompatibleModelInfo
    {
        public readonly bool IsGPT5;
        public readonly bool IsGPTOSS;
        public readonly bool IsGLM;
        public readonly bool IsQwen;
        public readonly bool IsQwen235;

        public OpenAICompatibleModelInfo(string? modelId)
        {
            string modelIdLower = (modelId ?? string.Empty).ToLowerInvariant();
            IsGPT5 = modelIdLower.Contains("gpt-5");
            IsGPTOSS = modelIdLower.Contains("gpt-oss");
            IsGLM = modelIdLower.Contains("glm");
            IsQwen = modelIdLower.Contains("qwen") && !modelIdLower.Contains("qwen-3-235b");
            IsQwen235 = modelIdLower.Contains("qwen-3-235b");
        }
    }

    internal static class OpenAICompatibleRouting
    {
        public static string BuildCompletionsEndpoint(string baseUrl, OpenAICompatibleHostInfo host)
        {
            return host.UsesResponsesApi
                ? $"{baseUrl}/responses"
                : $"{baseUrl}/chat/completions";
        }
    }
}

