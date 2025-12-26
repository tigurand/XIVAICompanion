using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace XIVAICompanion.Providers
{
    public class OpenAiProvider : IAiProvider
    {
        private readonly HttpClient _httpClient;

        public string Name => "OpenAI";

        public OpenAiProvider(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ProviderResult> SendPromptAsync(ProviderRequest request, ModelProfile profile)
        {
            return await SendPromptAsync(request, profile, false);
        }

        public async Task<ProviderResult> SendPromptAsync(ProviderRequest request, ModelProfile profile, bool skipToolDetection)
        {
            var result = new ProviderResult
            {
                ModelUsed = profile.ModelId,
                ResponseTokensUsed = request.MaxTokens,
                ThinkingBudgetUsed = request.ThinkingBudget
            };

            string baseUrl = profile.BaseUrl.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                baseUrl = "https://api.openai.com/v1";
            }

            var messages = new List<object>();

            messages.Add(new { role = "system", content = request.SystemPrompt });

            foreach (var content in request.ConversationHistory)
            {
                string role = content.Role == "model" ? "assistant" : "user";
                string text = string.Join("\n", content.Parts.Select(p => p.Text));

                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(new { role = role, content = text });
                }
            }

            var openAiRequest = new JObject
            {
                ["model"] = profile.ModelId,
                ["messages"] = JArray.FromObject(messages),
                ["max_tokens"] = request.MaxTokens,
                ["temperature"] = request.Temperature
            };

            if (request.IsThinkingEnabled)
            {
                string baseUrlLower = baseUrl.ToLowerInvariant();
                string modelId = profile.ModelId.ToLowerInvariant();

                bool isOpenAi = baseUrlLower.Contains("openai.com");
                bool isOpenRouter = baseUrlLower.Contains("openrouter.ai");
                bool isGroq = baseUrlLower.Contains("groq.com");

                if (isOpenAi || isOpenRouter)
                {
                    openAiRequest["effort"] = ProviderConstants.OpenAIReasoningEffort;
                }
                // Groq uses reasoning_format instead of reasoning_effort

                if (request.ShowThoughts)
                {
                    if (isOpenAi)
                    {
                        openAiRequest["summary"] = "auto";
                    }
                    else if (isOpenRouter)
                    {
                        openAiRequest["exclude"] = false;
                    }
                    else if (isGroq)
                    {
                        openAiRequest["reasoning_format"] = "parsed";
                    }
                }
            }

            if (request.UseWebSearch && !string.IsNullOrEmpty(profile.TavilyApiKey))
            {
                var searchTool = new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = "web_search",
                        ["description"] = "Search the web for current information using Tavily.",
                        ["parameters"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["query"] = new JObject
                                {
                                    ["type"] = "string",
                                    ["description"] = "The search query"
                                }
                            },
                            ["required"] = new JArray { "query" }
                        }
                    }
                };
                openAiRequest["tools"] = new JArray { searchTool };
                openAiRequest["tool_choice"] = "auto";
            }

            try
            {
                var requestBody = JsonConvert.SerializeObject(openAiRequest);
                var requestContent = new StringContent(requestBody, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
                {
                    Content = requestContent
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);

                var stopwatch = Stopwatch.StartNew();
                var response = await _httpClient.SendAsync(requestMessage);
                result.HttpResponse = response;
                result.RawResponse = await response.Content.ReadAsStringAsync();
                stopwatch.Stop();
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

                if (!string.IsNullOrEmpty(result.RawResponse))
                {
                    result.ResponseJson = JObject.Parse(result.RawResponse);
                }

                if (!response.IsSuccessStatusCode)
                {
                    result.WasSuccessful = false;
                    return result;
                }

                result.ResponseText = (string?)result.ResponseJson?.SelectToken("choices[0].message.content");

                if (request.ShowThoughts && baseUrl.ToLowerInvariant().Contains("groq.com"))
                {
                    string? reasoning = (string?)result.ResponseJson?.SelectToken("choices[0].message.reasoning");
                    if (!string.IsNullOrEmpty(reasoning))
                    {
                        result.ResponseText = $"{reasoning}\n\n{result.ResponseText}";
                    }
                }

                result.PromptTokens = (int?)result.ResponseJson?.SelectToken("usage.prompt_tokens") ?? 0;
                result.ResponseTokens = (int?)result.ResponseJson?.SelectToken("usage.completion_tokens") ?? 0;
                result.TotalTokens = (int?)result.ResponseJson?.SelectToken("usage.total_tokens") ?? (result.PromptTokens + result.ResponseTokens);

                string? finishReason = (string?)result.ResponseJson?.SelectToken("choices[0].finish_reason");

                result.WasSuccessful = !string.IsNullOrEmpty(result.ResponseText);

                if (finishReason == "length") result.WasSuccessful = false;

                return result;
            }
            catch (Exception ex)
            {
                result.WasSuccessful = false;
                result.Exception = ex;
                return result;
            }
        }
    }
}
