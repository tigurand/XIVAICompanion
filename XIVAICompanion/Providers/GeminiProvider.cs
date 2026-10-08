using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XIVAICompanion.Models;

namespace XIVAICompanion.Providers
{
    public class GeminiProvider : IAiProvider
    {
        private const string InteractionsEndpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";

        private readonly HttpClient _httpClient;

        public string Name => "Gemini";

        public GeminiProvider(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ProviderResult> SendPromptAsync(ProviderRequest request, ModelProfile profile)
        {
            var result = new ProviderResult
            {
                ModelUsed = profile.ModelId,
                ResponseTokensUsed = request.MaxTokens,
                ThinkingBudgetUsed = request.ThinkingBudget
            };

            var requestContents = new List<Content>(request.ConversationHistory);

            // Ensure system prompt is at the start if it's the first message
            if (requestContents.Count == 0)
            {
                requestContents.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = request.SystemPrompt } } });
                requestContents.Add(new Content { Role = "model", Parts = new List<Part> { new Part { Text = "Understood. I will follow all instructions." } } });
            }
            else if (requestContents.Count > 0 && requestContents[0].Parts.Count > 0 && requestContents[0].Parts[0].Text == request.SystemPrompt)
            {
                // System prompt is already there and matches, no need to inject a model turn
            }
            else
            {
                // Update existing system prompt
                requestContents[0] = new Content { Role = "user", Parts = new List<Part> { new Part { Text = request.SystemPrompt } } };
            }

            var interactionInput = new List<InteractionInput>();
            foreach (var content in requestContents)
            {
                string text = string.Join("\n", content.Parts.Select(p => p.Text));
                if (string.IsNullOrWhiteSpace(text)) continue;

                interactionInput.Add(new InteractionInput
                {
                    Type = content.Role == "model" ? "model_output" : "user_input",
                    Content = new List<InteractionContentBlock>
                    {
                        new InteractionContentBlock { Type = "text", Text = text }
                    }
                });
            }

            var modelInfo = new GeminiModelInfo(profile.ModelId);

            var generationConfig = new InteractionGenerationConfig
            {
                MaxOutputTokens = request.MaxTokens
            };

            if (modelInfo.IsGemini3)
            {
                generationConfig.ThinkingLevel = request.IsThinkingEnabled
                    ? ProviderConstants.GeminiThinkingLevel
                    : ProviderConstants.GeminiThinkingLevelDefault;
            }

            if (request.ShowThoughts)
            {
                generationConfig.ThinkingSummaries = "auto";
            }

            var interactionRequest = new InteractionRequest
            {
                Model = profile.ModelId,
                Input = interactionInput,
                GenerationConfig = generationConfig
            };

            // TEMPORARILY DISABLED: custom safety settings are supported by the legacy
            // generateContent API but are NOT yet available in the Interactions API (the field
            // is rejected with "invalid_request"). Kept commented out for a while, in case Google implement it.
            //
            // interactionRequest.SafetySettings = new List<SafetySetting>
            // {
            //     new SafetySetting { Category = "HARM_CATEGORY_HARASSMENT", Threshold = "BLOCK_NONE" },
            //     new SafetySetting { Category = "HARM_CATEGORY_HATE_SPEECH", Threshold = "BLOCK_NONE" },
            //     new SafetySetting { Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", Threshold = "BLOCK_NONE" },
            //     new SafetySetting { Category = "HARM_CATEGORY_DANGEROUS_CONTENT", Threshold = "BLOCK_NONE" }
            // };

            if (request.UseWebSearch)
            {
                interactionRequest.Tools = new List<Tool>
                {
                    new Tool { Type = "google_search" },
                    new Tool { Type = "url_context" }
                };
            }

            try
            {
                var requestBody = JsonConvert.SerializeObject(interactionRequest, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var requestContent = new StringContent(requestBody, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, InteractionsEndpoint)
                {
                    Content = requestContent
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", profile.ApiKey);

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

                var answerBlocks = new List<string>();
                var thoughtBlocks = new List<string>();

                var steps = result.ResponseJson?["steps"] as JArray;
                if (steps != null)
                {
                    foreach (var step in steps)
                    {
                        string? stepType = (string?)step?["type"];

                        if (string.Equals(stepType, "thought", StringComparison.OrdinalIgnoreCase))
                        {
                            var summary = step?["summary"] as JArray;
                            if (summary == null) continue;

                            foreach (var contentBlock in summary)
                            {
                                if (!string.Equals((string?)contentBlock?["type"], "text", StringComparison.OrdinalIgnoreCase)) continue;

                                string? thoughtText = (string?)contentBlock?["text"];
                                if (!string.IsNullOrEmpty(thoughtText)) thoughtBlocks.Add(thoughtText);
                            }
                        }
                        else if (string.Equals(stepType, "model_output", StringComparison.OrdinalIgnoreCase))
                        {
                            var content = step?["content"] as JArray;
                            if (content == null) continue;

                            foreach (var contentBlock in content)
                            {
                                if (!string.Equals((string?)contentBlock?["type"], "text", StringComparison.OrdinalIgnoreCase)) continue;

                                string? answerText = (string?)contentBlock?["text"];
                                if (!string.IsNullOrEmpty(answerText)) answerBlocks.Add(answerText);
                            }
                        }
                    }
                }

                string answer = string.Join("\n\n", answerBlocks);

                if (request.ShowThoughts && thoughtBlocks.Count > 0)
                {
                    string thoughts = string.Join("\n\n", thoughtBlocks);
                    result.ResponseText = string.IsNullOrEmpty(answer) ? thoughts : $"{thoughts}\n\n{answer}";
                }
                else
                {
                    result.ResponseText = answer;
                }

                result.FinishReason = (string?)result.ResponseJson?.SelectToken("stop_reason")
                    ?? (string?)result.ResponseJson?.SelectToken("stopReason")
                    ?? (string?)result.ResponseJson?.SelectToken("finish_reason")
                    ?? (string?)result.ResponseJson?.SelectToken("finishReason");
                result.BlockReason = (string?)result.ResponseJson?.SelectToken("prompt_feedback.block_reason")
                    ?? (string?)result.ResponseJson?.SelectToken("promptFeedback.blockReason");

                result.PromptTokens = (int?)result.ResponseJson?.SelectToken("usage_metadata.prompt_token_count")
                    ?? (int?)result.ResponseJson?.SelectToken("usageMetadata.promptTokenCount")
                    ?? (int?)result.ResponseJson?.SelectToken("usage.input_tokens")
                    ?? (int?)result.ResponseJson?.SelectToken("usage.inputTokens") ?? 0;
                result.ResponseTokens = (int?)result.ResponseJson?.SelectToken("usage_metadata.candidates_token_count")
                    ?? (int?)result.ResponseJson?.SelectToken("usageMetadata.candidatesTokenCount")
                    ?? (int?)result.ResponseJson?.SelectToken("usage.output_tokens")
                    ?? (int?)result.ResponseJson?.SelectToken("usage.outputTokens") ?? 0;
                result.TotalTokens = (int?)result.ResponseJson?.SelectToken("usage_metadata.total_token_count")
                    ?? (int?)result.ResponseJson?.SelectToken("usageMetadata.totalTokenCount")
                    ?? (result.PromptTokens + result.ResponseTokens);

                result.WasSuccessful = (int)response.StatusCode != 503
                    && (result.ResponseTokens > 0 || !string.IsNullOrEmpty(result.ResponseText));

                // If finish reason was MAX_TOKENS, original code treated it as failure for fallback.
                if (string.Equals(result.FinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase)) result.WasSuccessful = false;

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
