using System;
using System.Collections.Generic;
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
        private readonly HttpClient _httpClient;

        public string Name => "Gemini";

        public GeminiProvider(HttpClient httpClient)
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

            var geminiRequest = new GeminiRequest
            {
                Contents = requestContents,
                GenerationConfig = new GenerationConfig
                {
                    MaxOutputTokens = request.MaxTokens,
                    Temperature = request.Temperature,
                    ThinkingConfig = request.ThinkingBudget.HasValue
                        ? new ThinkingConfig { ThinkingBudget = request.ThinkingBudget.Value, IncludeThoughts = request.ShowThoughts }
                        : null
                },
                SafetySettings = new List<SafetySetting>
                {
                    new SafetySetting { Category = "HARM_CATEGORY_HARASSMENT", Threshold = "BLOCK_NONE" },
                    new SafetySetting { Category = "HARM_CATEGORY_HATE_SPEECH", Threshold = "BLOCK_NONE" },
                    new SafetySetting { Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", Threshold = "BLOCK_NONE" },
                    new SafetySetting { Category = "HARM_CATEGORY_DANGEROUS_CONTENT", Threshold = "BLOCK_NONE" }
                }
            };

            if (request.UseWebSearch)
            {
                if (profile.UseTavilyInstead && !string.IsNullOrEmpty(profile.TavilyApiKey))
                {
                    geminiRequest.Tools = new List<Tool>
                    {
                        new Tool { FunctionDeclarations = new List<FunctionDeclaration> 
                        { 
                            new FunctionDeclaration 
                            { 
                                Name = "web_search", 
                                Description = "Search the web for current information using Tavily.",
                                Parameters = new 
                                { 
                                    type = "OBJECT", 
                                    properties = new { query = new { type = "STRING", description = "The search query" } },
                                    required = new[] { "query" }
                                }
                            } 
                        } }
                    };
                }
                else
                {
                    geminiRequest.Tools = new List<Tool>
                    {
                        new Tool { GoogleSearch = new GoogleSearch() },
                        new Tool { UrlContext = new UrlContext() }
                    };
                }
            }

            try
            {
                var requestBody = JsonConvert.SerializeObject(geminiRequest, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var requestContent = new StringContent(requestBody, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{profile.ModelId}:generateContent")
                {
                    Content = requestContent
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", profile.ApiKey);

                var response = await _httpClient.SendAsync(requestMessage);
                result.HttpResponse = response;
                result.RawResponse = await response.Content.ReadAsStringAsync();

                if (!string.IsNullOrEmpty(result.RawResponse))
                {
                    result.ResponseJson = JObject.Parse(result.RawResponse);
                }

                if (!response.IsSuccessStatusCode)
                {
                    result.WasSuccessful = false;
                    return result;
                }

                var allText = new List<string>();
                var parts = result.ResponseJson?.SelectToken("candidates[0].content.parts");

                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        var partText = (string?)part.SelectToken("text");
                        if (!string.IsNullOrEmpty(partText))
                        {
                            allText.Add(partText);
                        }
                    }
                }
                result.ResponseText = string.Join("\n\n", allText);

                result.FinishReason = (string?)result.ResponseJson?.SelectToken("candidates[0].finishReason");
                result.BlockReason = (string?)result.ResponseJson?.SelectToken("promptFeedback.blockReason");

                if (result.FinishReason == "MAX_TOKENS" || result.FinishReason == "SAFETY" || result.FinishReason == "RECITATION" || result.FinishReason == "OTHER")
                {
                    // Even if we got some text, if it finished for these reasons, we might want to flag it as unsuccessful for fallback purposes
                }

                result.PromptTokens = (int?)result.ResponseJson?.SelectToken("usageMetadata.promptTokenCount") ?? 0;
                result.ResponseTokens = (int?)result.ResponseJson?.SelectToken("usageMetadata.candidatesTokenCount") ?? 0;
                if (result.ResponseTokens == 0)
                {
                    result.ResponseTokens = (int?)result.ResponseJson?.SelectToken("usageMetadata.completionTokenCount") ?? 0;
                }
                result.TotalTokens = result.PromptTokens + result.ResponseTokens;

                result.WasSuccessful = (int)response.StatusCode != 503 && result.ResponseTokens > 0;
                
                // If finish reason was MAX_TOKENS, original code treated it as failure for fallback.
                if (result.FinishReason == "MAX_TOKENS") result.WasSuccessful = false;

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
