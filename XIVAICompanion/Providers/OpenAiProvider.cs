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

            var host = new OpenAiCompatibleHostInfo(baseUrl);

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

            bool useResponsesApi = host.UsesResponsesApi;

            JObject openAiRequest;
            if (useResponsesApi)
            {
                var inputMessages = new List<object>();
                bool skippedSystemAsUser = false;
                foreach (var content in request.ConversationHistory)
                {
                    string role = content.Role == "model" ? "assistant" : "user";
                    string text = string.Join("\n", content.Parts.Select(p => p.Text));
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    if (!skippedSystemAsUser
                        && role == "user"
                        && string.Equals(text.Trim(), request.SystemPrompt.Trim(), StringComparison.Ordinal))
                    {
                        skippedSystemAsUser = true;
                        continue;
                    }

                    inputMessages.Add(new { role = role, content = text });
                }

                openAiRequest = new JObject
                {
                    ["model"] = profile.ModelId,
                    ["instructions"] = request.SystemPrompt,
                    ["input"] = JArray.FromObject(inputMessages),
                    ["max_output_tokens"] = request.MaxTokens,
                    ["temperature"] = request.Temperature
                };
            }
            else
            {
                openAiRequest = new JObject
                {
                    ["model"] = profile.ModelId,
                    ["messages"] = JArray.FromObject(messages),
                    ["max_tokens"] = request.MaxTokens,
                    ["temperature"] = request.Temperature
                };
            }

            if (request.IsThinkingEnabled)
            {
                if (host.IsOpenAi || host.IsHuggingFace)
                {
                    if (openAiRequest["reasoning"] is not JObject reasoning)
                    {
                        reasoning = new JObject();
                        openAiRequest["reasoning"] = reasoning;
                    }
                    reasoning["effort"] = ProviderConstants.OpenAIReasoningEffort;
                }
                else if (host.IsOpenRouter)
                {
                    openAiRequest["effort"] = ProviderConstants.OpenAIReasoningEffort;
                }
                else if (host.IsGroq)
                {
                    openAiRequest["reasoning_effort"] = ProviderConstants.OpenAIReasoningEffort;
                }

                if (request.ShowThoughts)
                {
                    if (host.IsOpenAi || host.IsHuggingFace)
                    {
                        if (openAiRequest["reasoning"] is not JObject reasoning)
                        {
                            reasoning = new JObject();
                            openAiRequest["reasoning"] = reasoning;
                        }
                        reasoning["summary"] = "auto";
                    }
                    else if (host.IsOpenRouter)
                    {
                        openAiRequest["exclude"] = false;
                    }
                    else if (host.IsGroq)
                    {
                        openAiRequest["reasoning_format"] = "parsed";
                    }
                }
            }

            if (request.UseWebSearch && !string.IsNullOrEmpty(profile.TavilyApiKey))
            {
                if (useResponsesApi)
                {
                    var searchTool = new JObject
                    {
                        ["type"] = "function",
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
                    };
                    openAiRequest["tools"] = new JArray { searchTool };
                    openAiRequest["tool_choice"] = "auto";
                }
                else
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
            }

            try
            {
                var requestBody = JsonConvert.SerializeObject(openAiRequest);
                var requestContent = new StringContent(requestBody, Encoding.UTF8, "application/json");

                string endpoint = OpenAiCompatibleRouting.BuildCompletionsEndpoint(baseUrl, host);

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
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

                if (useResponsesApi)
                {
                    result.ResponseText = (string?)result.ResponseJson?.SelectToken("output_text");

                    if (string.IsNullOrEmpty(result.ResponseText))
                    {
                        var output = result.ResponseJson?["output"] as JArray;
                        if (output != null)
                        {
                            var messageTexts = new List<string>();
                            var reasoningTexts = new List<string>();
                            foreach (var item in output)
                            {
                                string? itemType = (string?)item?["type"];

                                var content = item?["content"] as JArray;
                                if (content == null) continue;

                                foreach (var part in content)
                                {
                                    string? text = (string?)part?["text"];
                                    if (string.IsNullOrEmpty(text)) continue;

                                    if (string.Equals(itemType, "message", StringComparison.OrdinalIgnoreCase))
                                        messageTexts.Add(text);
                                    else if (string.Equals(itemType, "reasoning", StringComparison.OrdinalIgnoreCase))
                                        reasoningTexts.Add(text);
                                }
                            }

                            if (messageTexts.Count > 0)
                            {
                                result.ResponseText = string.Join("\n", messageTexts);
                            }
                            else if (reasoningTexts.Count > 0)
                            {
                                result.ResponseText = string.Join("\n", reasoningTexts);
                            }
                        }
                    }

                    result.PromptTokens = (int?)result.ResponseJson?.SelectToken("usage.input_tokens") ?? 0;
                    result.ResponseTokens = (int?)result.ResponseJson?.SelectToken("usage.output_tokens") ?? 0;
                    result.TotalTokens = (int?)result.ResponseJson?.SelectToken("usage.total_tokens") ?? (result.PromptTokens + result.ResponseTokens);

                    result.WasSuccessful = !string.IsNullOrEmpty(result.ResponseText);
                }
                else
                {
                    result.ResponseText = (string?)result.ResponseJson?.SelectToken("choices[0].message.content");

                    if (request.ShowThoughts && host.IsGroq)
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
                }

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
