using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using XIVAICompanion.Models;
using XIVAICompanion.Utils;

namespace XIVAICompanion
{
    public partial class AICompanionPlugin
    {
        private async Task SendPrompt(string input, bool isStateless, OutputTarget outputTarget, string partnerName, bool isFreshLogin = true, bool tempSearchMode = false, bool tempThinkMode = false, bool tempFreshMode = false, bool tempOocMode = false)
        {
            var systemPrompt = GetSystemPrompt(partnerName);
            var removeLineBreaks = configuration.RemoveLineBreaks;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            var conversationHistory = GetHistoryForPlayer(partnerName);

            var failedAttempts = new List<(string Model, ApiResult Result)>();
            int initialModelIndex = Array.IndexOf(_availableModels, configuration.AImodel);

            bool isThink = (configuration.ThinkMode || tempThinkMode) && !isFreshLogin;
            if (isThink)
                initialModelIndex = thinkingModelIndex;
            else if (isFreshLogin)
                initialModelIndex = greetingModelIndex;

            if (initialModelIndex == -1) initialModelIndex = 1;

            for (int i = 0; i < _availableModels.Length; i++)
            {
                int currentModelIndex = (initialModelIndex + i) % _availableModels.Length;
                string modelToTry = _availableModels[currentModelIndex];

                ApiResult result = await SendPromptInternal(input, modelToTry, isStateless, outputTarget, systemPrompt, removeLineBreaks, showAdditionalInfo, false, null, conversationHistory, isFreshLogin, tempSearchMode, tempThinkMode, tempFreshMode, tempOocMode);
                if (result.WasSuccessful) return;
                failedAttempts.Add((modelToTry, result));
            }

            HandleApiError(failedAttempts, input);
        }

        private async Task SendAutoRpPrompt(string capturedMessage, XivChatType sourceType)
        {
            string rpPartnerName = _autoRpTargetNameBuffer;
            string finalRpSystemPrompt = GetSystemPrompt(rpPartnerName);
            var outputTarget = OutputTarget.GameChat;
            var removeLineBreaks = true;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            var conversationHistory = GetHistoryForPlayer(rpPartnerName);

            var failedAttempts = new List<(string Model, ApiResult Result)>();
            var modelToTry = configuration.AImodel;
            var initialModelIndex = Array.IndexOf(_availableModels, modelToTry);
            if (initialModelIndex == -1) initialModelIndex = 0;

            for (int i = 0; i < _availableModels.Length; i++)
            {
                int currentModelIndex = (initialModelIndex + i) % _availableModels.Length;
                string currentModel = _availableModels[currentModelIndex];
                ApiResult result = await SendPromptInternal(capturedMessage, currentModel, false, outputTarget, finalRpSystemPrompt, removeLineBreaks, showAdditionalInfo, true, sourceType, conversationHistory, false, false, false, false);
                if (result.WasSuccessful) return;
                failedAttempts.Add((currentModel, result));
            }

            HandleApiError(failedAttempts, capturedMessage);
        }

        private async Task SendAutoReplyPrompt(string capturedMessage, string senderName, XivChatType sourceType)
        {
            string finalRpSystemPrompt = GetSystemPrompt(senderName);
            var outputTarget = OutputTarget.GameChat;
            var removeLineBreaks = true;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            string historyName = (_openListenerModeBuffer && _mixedHistoryModeBuffer) ? "Multiple People" : senderName;
            var conversationHistory = GetHistoryForPlayer(historyName);

            var failedAttempts = new List<(string Model, ApiResult Result)>();
            var modelToTry = configuration.AImodel;
            var initialModelIndex = Array.IndexOf(_availableModels, modelToTry);
            if (initialModelIndex == -1) initialModelIndex = 0;

            for (int i = 0; i < _availableModels.Length; i++)
            {
                int currentModelIndex = (initialModelIndex + i) % _availableModels.Length;
                string currentModel = _availableModels[currentModelIndex];
                ApiResult result = await SendPromptInternal(capturedMessage, currentModel, false, outputTarget, finalRpSystemPrompt, removeLineBreaks, showAdditionalInfo, true, sourceType, conversationHistory, false, false, false, false);
                if (result.WasSuccessful) return;
                failedAttempts.Add((currentModel, result));
            }

            HandleApiError(failedAttempts, capturedMessage);
        }

        private async Task<ApiResult> SendPromptInternal(string input, string modelToUse, bool isStateless, OutputTarget outputTarget, string systemPrompt,
                                                bool removeLineBreaks, bool showAdditionalInfo, bool forceHistory = false, XivChatType? replyChannel = null,
                                                List<Content>? conversationHistory = null, bool isFreshLogin = false, bool tempSearchMode = false, bool tempThinkMode = false, bool tempFreshMode = false, bool tempOocMode = false)
        {
            int responseTokensToUse = configuration.MaxTokens;
            int? thinkingBudget = defaultThinkingBudget;
            string userPrompt;
            bool useWebSearch = false;
            string currentPrompt = input.Replace('　', ' ').Trim();

            bool isSearch = (configuration.SearchMode || tempSearchMode) && !isFreshLogin;
            bool isThink = (configuration.ThinkMode || tempThinkMode) && !isFreshLogin;
            bool isFresh = (_chatFreshMode || tempFreshMode) && !isFreshLogin;
            bool isOoc = (_chatOocMode || tempOocMode) && !isFreshLogin;

            if (isStateless || isFresh)
            {
                // Fresh mode is handled by isStateless parameter
            }

            if (isSearch) useWebSearch = true;
            if (isThink) thinkingBudget = configuration.MaxTokens; ;

            userPrompt = currentPrompt;

            string finalUserPrompt = string.Empty;
            if (useWebSearch)
            {
                finalUserPrompt = "[SYSTEM COMMAND: GOOGLE SEARCH]\n" +
                    "1.  **PRIMARY DIRECTIVE:** Check if Google Search tool is needed to answer the *entire* User Message.\n" +
                    "2.  **SECONDARY DIRECTIVE:** If needed, immediately use the Google Search tool to answer the *entire* User Message.\n" +
                    "3.  **TERTIARY DIRECTIVE:** Adhere strictly to the Language Protocol, while still being consistent with your personality.\n" +
                    "4.  **RULES:** Do not converse. Do not acknowledge. Provide a direct, synthesized answer from the search results.\n\n";
            }
            finalUserPrompt += $"--- User Message ---\n{userPrompt}";

            List<Content> requestContents;
            Content? userTurn = null;

            if (!configuration.EnableConversationHistory)
            {
                isStateless = true;
            }

            if (!isStateless)
            {
                var activeHistory = conversationHistory ?? new List<Content>();
                requestContents = new List<Content>(activeHistory);

                if (requestContents.Count > 0)
                {
                    requestContents[0] = new Content { Role = "user", Parts = new List<Part> { new Part { Text = systemPrompt } } };
                }
                else
                {
                    requestContents.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = systemPrompt } } });
                    requestContents.Add(new Content { Role = "model", Parts = new List<Part> { new Part { Text = $"Understood. I am {_aiNameBuffer}. I will follow all instructions." } } });
                }

                userTurn = new Content { Role = "user", Parts = new List<Part> { new Part { Text = finalUserPrompt } } };
                activeHistory.Add(userTurn);
                requestContents.Add(userTurn);

                if (configuration.ConversationHistoryLimit > 0)
                {
                    int maxHistoryItems = (configuration.ConversationHistoryLimit * 2) + 2;
                    if (activeHistory.Count > maxHistoryItems)
                    {
                        activeHistory.RemoveRange(2, activeHistory.Count - maxHistoryItems);
                    }
                }
            }
            else
            {
                userTurn = null;
                requestContents = new List<Content>
                {
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = systemPrompt } } },
                    new Content { Role = "model", Parts = new List<Part> { new Part { Text = $"Understood. I am {_aiNameBuffer}. I will follow all instructions." } } },
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = finalUserPrompt } } }
                };
            }

            var geminiRequest = new GeminiRequest
            {
                Contents = requestContents,
                GenerationConfig = new GenerationConfig
                {
                    MaxOutputTokens = responseTokensToUse,
                    Temperature = configuration.Temperature,
                    ThinkingConfig = thinkingBudget.HasValue
                        ? new ThinkingConfig { ThinkingBudget = thinkingBudget.Value, IncludeThoughts = configuration.ShowThoughts }
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

            if (useWebSearch)
            {
                var toolsList = new List<Tool>
                {
                    new Tool { GoogleSearch = new GoogleSearch() }
                };

                // --- START OF EXPERIMENTAL URL CONTEXT FEATURE ---
                // To disable this feature, simply comment out the next line.
                toolsList.Add(new Tool { UrlContext = new UrlContext() });
                // --- END OF EXPERIMENTAL URL CONTEXT FEATURE ---

                geminiRequest.Tools = toolsList;
            }

            try
            {
                var requestBody = JsonConvert.SerializeObject(geminiRequest, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var requestContent = new StringContent(requestBody, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{modelToUse}:generateContent")
                {
                    Content = requestContent
                };
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("x-goog-api-key", configuration.ApiKey);

                var response = await httpClient.SendAsync(requestMessage);
                var responseBody = await response.Content.ReadAsStringAsync();
                var responseJson = JObject.Parse(responseBody);

                var allText = new List<string>();
                var parts = responseJson.SelectToken("candidates[0].content.parts");

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
                string text = string.Join("\n\n", allText);

                string? finishReason = (string?)responseJson.SelectToken("candidates[0].finishReason");

                if (finishReason == "MAX_TOKENS")
                {
                    if (configuration.EnableConversationHistory && userTurn != null) (conversationHistory ?? new List<Content>()).Remove(userTurn);
                    return new ApiResult
                    {
                        WasSuccessful = false,
                        ResponseJson = responseJson,
                        HttpResponse = response,
                        ResponseTokensUsed = responseTokensToUse,
                        ThinkingBudgetUsed = thinkingBudget
                    };
                }

                if (text != null)
                {
                    if (outputTarget == OutputTarget.GameChat && configuration.AutoRpConfig.InitialResponseDelaySeconds > 0)
                    {
                        await Task.Delay((int)(configuration.AutoRpConfig.InitialResponseDelaySeconds * 1000));
                    }

                    string sanitizedText = text;

                    var statusCode = (int)response.StatusCode;
                    var promptTokens = (int?)responseJson.SelectToken("usageMetadata.promptTokenCount") ?? 0;
                    var responseTokens = (int?)responseJson.SelectToken("usageMetadata.candidatesTokenCount") ?? 0;
                    if (responseTokens == 0)
                    {
                        responseTokens = (int?)responseJson.SelectToken("usageMetadata.completionTokenCount") ?? 0;
                    }
                    var totalTokens = promptTokens + responseTokens;
                    bool success = statusCode != 503 && responseTokens > 0;

                    if (!success)
                    {
                        if (configuration.EnableConversationHistory && userTurn != null)
                            (conversationHistory ?? new List<Content>()).Remove(userTurn);

                        Service.Log.Warning(
                            $"API Call Failure: Model='{modelToUse}', HTTP Status={statusCode} - {response.StatusCode}, " +
                            $"ResponseTokenLimit={responseTokensToUse}, ThinkingBudget={thinkingBudget ?? 0}, " +
                            $"Tokens=[P:{promptTokens}, R:{responseTokens}, T:{totalTokens}]"
                            );

                        return new ApiResult
                        {
                            WasSuccessful = false,
                            ResponseJson = responseJson,
                            HttpResponse = response,
                            ResponseTokensUsed = responseTokensToUse,
                            ThinkingBudgetUsed = thinkingBudget
                        };
                    }

                    int lastPromptIndex = text.LastIndexOf(finalUserPrompt, StringComparison.Ordinal);
                    if (lastPromptIndex != -1)
                    {
                        int aiResponseStartIndex = lastPromptIndex + finalUserPrompt.Length;
                        if (aiResponseStartIndex < text.Length)
                        {
                            sanitizedText = text.Substring(aiResponseStartIndex);
                            sanitizedText = sanitizedText.TrimStart(' ', '\r', '\n', ']', '-', ':');
                        }
                        else
                        {
                            sanitizedText = string.Empty;
                        }
                    }

                    if ((forceHistory || configuration.EnableConversationHistory) && !isStateless)
                    {
                        var modelTurn = new Content { Role = "model", Parts = new List<Part> { new Part { Text = sanitizedText } } };
                        (conversationHistory ?? new List<Content>()).Add(modelTurn);
                    }

                    string finalResponse = removeLineBreaks
                        ? sanitizedText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("  ", " ")
                        : sanitizedText;

                    _currentSessionChatLog.Add(new ChatMessage
                    {
                        Timestamp = DateTime.Now,
                        Author = configuration.AIName,
                        Message = finalResponse
                    });
                    _shouldScrollToBottom = true;

                    switch (outputTarget)
                    {
                        case OutputTarget.GameChat:
                            await Service.Framework.RunOnFrameworkThread(() =>
                            {
                                string commandPrefix = string.Empty;
                                if (configuration.AutoRpConfig.ReplyInSpecificChannel)
                                {
                                    commandPrefix = GetPrefixForChannelIndex(configuration.AutoRpConfig.SpecificReplyChannel);
                                }
                                else if (configuration.AutoRpConfig.AutoReplyToAllTells && replyChannel.HasValue && replyChannel.Value == XivChatType.TellIncoming)
                                {
                                    commandPrefix = "/r ";
                                }
                                else if (configuration.AutoRpConfig.ReplyInOriginalChannel && replyChannel.HasValue)
                                {
                                    commandPrefix = GetReplyPrefix(replyChannel.Value);
                                }
                                SendMessageToGameChat(finalResponse, commandPrefix: commandPrefix);
                            });
                            break;

                        case OutputTarget.PluginWindow:
                            break;

                        case OutputTarget.PluginDebug:
                        default:
                            foreach (var chunk in SplitIntoChunks(finalResponse, 1000))
                            {
                                PrintMessageToChat($"{configuration.AIName}: {chunk}");
                            }
                            break;
                    }

                    if (showAdditionalInfo)
                    {
                        int? promptTokenCount = (int?)responseJson.SelectToken("usageMetadata.promptTokenCount");
                        int? responseTokenCount = (int?)responseJson.SelectToken("usageMetadata.candidatesTokenCount");
                        if (responseTokenCount == null)
                        {
                            responseTokenCount = (int?)responseJson.SelectToken("usageMetadata.completionTokenCount");
                        }

                        var infoBuilder = new StringBuilder();
                        infoBuilder.AppendLine($"{_aiNameBuffer}>> --- Technical Info ---");
                        infoBuilder.AppendLine($"Prompt: {userPrompt}");
                        infoBuilder.AppendLine($"Model: {modelToUse}");
                        infoBuilder.AppendLine($"Response Token Limit: {responseTokensToUse}");
                        if (thinkingBudget == maxResponseTokens)
                        {
                            infoBuilder.AppendLine($"Thinking Budget: Maximum ({thinkingBudget})");
                        }
                        else if (thinkingBudget > 0)
                        {
                            infoBuilder.AppendLine($"Thinking Budget: Standard ({thinkingBudget})");
                        }
                        else
                        {
                            infoBuilder.AppendLine($"Thinking Budget: Disabled ({thinkingBudget ?? 0})");
                        }
                        infoBuilder.AppendLine($"Temperature: {_temperatureBuffer}");
                        infoBuilder.AppendLine($"Web Search: {useWebSearch}");
                        infoBuilder.AppendLine($"HTTP Status: {(int)response.StatusCode} - {response.StatusCode}");
                        infoBuilder.AppendLine($"Prompt Length (chars): {userPrompt.Length}");
                        infoBuilder.AppendLine($"Response Length (chars): {finalResponse.Length}");

                        if (promptTokenCount.HasValue)
                        {
                            infoBuilder.AppendLine($"Prompt Token Usage: {promptTokenCount}");
                        }
                        if (responseTokenCount.HasValue)
                        {
                            infoBuilder.AppendLine($"Response Token Usage: {responseTokenCount}");
                        }
                        if (promptTokenCount.HasValue && responseTokenCount.HasValue)
                        {
                            infoBuilder.Append($"Total Token Usage: {promptTokenCount.Value + responseTokenCount.Value}");
                        }

                        PrintSystemMessage(infoBuilder.ToString());
                    }

                    Service.Log.Info(
                        $"API Call Success: Model='{modelToUse}', HTTP Status={statusCode} - {response.StatusCode}, " +
                        $"ResponseTokenLimit={responseTokensToUse}, ThinkingBudget={thinkingBudget ?? 0}, " +
                        $"Tokens=[P:{promptTokens}, R:{responseTokens}, T:{totalTokens}]"
                    );

                    return new ApiResult
                    {
                        WasSuccessful = true,
                        ResponseJson = responseJson,
                        HttpResponse = response,
                        ResponseTokensUsed = responseTokensToUse,
                        ThinkingBudgetUsed = thinkingBudget
                    };

                }
                else
                {
                    if (configuration.EnableConversationHistory && userTurn != null) (conversationHistory ?? new List<Content>()).Remove(userTurn);
                    return new ApiResult
                    {
                        WasSuccessful = false,
                        ResponseJson = responseJson,
                        HttpResponse = response,
                        ResponseTokensUsed = responseTokensToUse,
                        ThinkingBudgetUsed = thinkingBudget
                    };
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "A critical network or parsing error occurred in SendPromptInternal for model {model}", modelToUse);
                if (configuration.EnableConversationHistory && userTurn != null) (conversationHistory ?? new List<Content>()).Remove(userTurn);
                return new ApiResult
                {
                    WasSuccessful = false,
                    Exception = ex,
                    ResponseTokensUsed = responseTokensToUse,
                    ThinkingBudgetUsed = thinkingBudget
                };
            }
        }

        private void ProcessPrompt(string rawPrompt, string? historyOverride = null)
        {
            string currentPrompt = rawPrompt.Replace('　', ' ').Trim();

            bool isSearch = configuration.SearchMode || _tempSearchMode;
            bool isThink = configuration.ThinkMode || _tempThinkMode;
            bool isFresh = _chatFreshMode || _tempFreshMode;
            bool isOoc = _chatOocMode || _tempOocMode;

            string processedPrompt = currentPrompt;

            string userMessageContent = GetCleanPromptText(currentPrompt);

            string partnerName;
            if (isOoc && !string.IsNullOrEmpty(_currentRpPartnerName))
            {
                partnerName = _currentRpPartnerName;
            }
            else if (_isAutoRpRunning && !string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer))
            {
                partnerName = _autoRpTargetNameBuffer;
            }
            else
            {
                partnerName = GetPlayerDisplayName();
            }

            _currentSessionChatLog.Add(new ChatMessage
            {
                Timestamp = DateTime.Now,
                Author = partnerName,
                Message = isOoc ? $"[OOC] {userMessageContent}" : userMessageContent
            });

            var historyEntry = historyOverride ?? rawPrompt;
            _chatInputHistory.Remove(historyEntry);
            _chatInputHistory.Add(historyEntry);
            if (_chatInputHistory.Count > 20) _chatInputHistory.RemoveAt(0);
            _chatHistoryIndex = -1;
            _shouldScrollToBottom = true;

            string processedPromptForApi = ProcessTextAliases(processedPrompt);
            OutputTarget outputTarget = isOoc ? OutputTarget.PluginWindow : (_isAutoRpRunning ? OutputTarget.GameChat : OutputTarget.PluginDebug);
            bool isStateless = isFresh;

            if (configuration.ShowPrompt && !isOoc && !_isAutoRpRunning)
            {
                PrintMessageToChat($"{GetPlayerDisplayName()}: {userMessageContent}");
            }

            bool tempSearchMode = _tempSearchMode;
            bool tempThinkMode = _tempThinkMode;
            bool tempFreshMode = _tempFreshMode;
            bool tempOocMode = _tempOocMode;

            Task.Run(async () =>
            {
                await SendPrompt(processedPromptForApi, isStateless, outputTarget, partnerName, false, tempSearchMode, tempThinkMode, tempFreshMode, tempOocMode);
                _tempSearchMode = false;
                _tempThinkMode = false;
                _tempFreshMode = false;
                _tempOocMode = false;
            });
        }

        private string GetCleanPromptText(string rawText)
        {
            string cleanText = rawText.Trim();

            return cleanText;
        }

        private void HandleApiError(List<(string Model, ApiResult Result)> failedAttempts, string input)
        {
            if (failedAttempts.Count == 0) return;

            var primaryFailure = failedAttempts[0];
            string primaryModelUsed = primaryFailure.Model;
            var primaryResult = primaryFailure.Result;

            string finalErrorMessage;
            string userPrompt = GetCleanPromptText(input);

            string httpStatus = primaryResult.HttpResponse != null ? $"Status: {(int)primaryResult.HttpResponse.StatusCode} {primaryResult.HttpResponse.ReasonPhrase}" : "Status: N/A";
            string? rawResponse = primaryResult.ResponseJson?.ToString(Formatting.Indented);

            int responseTokenLimit = primaryResult.ResponseTokensUsed;
            int? thinkingBudget = primaryResult.ThinkingBudgetUsed;

            if (configuration.EnableAutoFallback && failedAttempts.Count > 1)
            {
                string? finishReason = (string?)primaryResult.ResponseJson?.SelectToken("candidates[0].finishReason");
                string? blockReason = (string?)primaryResult.ResponseJson?.SelectToken("promptFeedback.blockReason");
                string primaryReason;

                if (primaryResult.HttpResponse?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    primaryReason = "API rate limit reached (RPM or RPD)";
                }
                else if (primaryResult.HttpResponse?.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    primaryReason = "the model is temporarily unable to handle the request (overloaded or offline)";
                }
                else if (!string.IsNullOrEmpty(finishReason) && finishReason != "STOP")
                {
                    primaryReason = $"the response was terminated by the API (Reason: {finishReason})";
                }
                else if (!string.IsNullOrEmpty(blockReason))
                {
                    primaryReason = $"the prompt was blocked by the API (Reason: {blockReason})";
                }
                else
                {
                    primaryReason = "an unknown error";
                }

                finalErrorMessage = $"{_aiNameBuffer}>> Error: The request to your primary model failed because {primaryReason}. Automatic fallback to other models was also unsuccessful.";

                var logBuilder = new StringBuilder();
                logBuilder.AppendLine($"API Failure (Fallback Path): All {failedAttempts.Count} attempts failed. Detailed breakdown:");

                for (int i = 0; i < failedAttempts.Count; i++)
                {
                    var attempt = failedAttempts[i];
                    var attemptResult = attempt.Result;
                    string attemptHttpStatus = attemptResult.HttpResponse != null ? $"{(int)attemptResult.HttpResponse.StatusCode} {attemptResult.HttpResponse.ReasonPhrase}" : "N/A";
                    string? attemptRawResponse = attemptResult.ResponseJson?.ToString(Formatting.Indented);

                    logBuilder.AppendLine($"--- Attempt {i + 1} of {failedAttempts.Count} ({attempt.Model}) ---");
                    logBuilder.AppendLine($"--> Status: {attemptHttpStatus}");
                    logBuilder.AppendLine($"--> Params: ResponseTokenLimit={attemptResult.ResponseTokensUsed}, ThinkingBudget={attemptResult.ThinkingBudgetUsed ?? 0}");
                    logBuilder.AppendLine($"--> Prompt: {userPrompt}");
                    logBuilder.AppendLine($"--> RawResponse:{Environment.NewLine}{attemptRawResponse ?? "N/A"}");
                }

                Service.Log.Warning(logBuilder.ToString());
            }
            else
            {
                if (primaryResult.Exception != null)
                {
                    finalErrorMessage = $"{_aiNameBuffer}>> An unexpected error occurred: {primaryResult.Exception.Message}";
                    Service.Log.Error(primaryResult.Exception, $"A critical network or parsing error occurred. Prompt: {{Prompt}}, ResponseTokenLimit: {{ResponseTokenLimit}}, ThinkingBudget: {{ThinkingBudget}}", userPrompt, responseTokenLimit, thinkingBudget ?? 0);
                }
                else if (primaryResult.ResponseJson != null && primaryResult.HttpResponse != null)
                {
                    string? blockReason = (string?)primaryResult.ResponseJson.SelectToken("promptFeedback.blockReason");
                    string? finishReason = (string?)primaryResult.ResponseJson.SelectToken("candidates[0].finishReason");

                    if (primaryResult.HttpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: API rate limit reached. This could be Requests Per Minute (RPM) or Requests Per Day (RPD).";
                        Service.Log.Warning($"API Failure: Rate Limit Exceeded.{Environment.NewLine}" +
                                    $"--> {httpStatus}{Environment.NewLine}" +
                                    $"--> Model: {primaryModelUsed}, ResponseTokenLimit: {responseTokenLimit}, ThinkingBudget: {thinkingBudget ?? 0}{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}");
                    }
                    else if (!string.IsNullOrEmpty(finishReason) && finishReason != "STOP")
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The response was terminated by the API. Reason: {finishReason}.";
                        if (finishReason == "MAX_TOKENS")
                        {
                            finalErrorMessage += " You can increase this value in /ai cfg.";
                        }
                        Service.Log.Warning($"API Failure: Response Terminated.{Environment.NewLine}" +
                                    $"--> Reason: {finishReason}{Environment.NewLine}" +
                                    $"--> {httpStatus}{Environment.NewLine}" +
                                    $"--> Model: {primaryModelUsed}, ResponseTokenLimit: {responseTokenLimit}, ThinkingBudget: {thinkingBudget ?? 0}{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}");
                    }
                    else if (!string.IsNullOrEmpty(blockReason))
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The prompt was blocked by the API. Reason: {blockReason}.";
                        Service.Log.Warning($"API Failure: Prompt Blocked.{Environment.NewLine}" +
                                    $"--> Reason: {blockReason}{Environment.NewLine}" +
                                    $"--> {httpStatus}{Environment.NewLine}" +
                                    $"--> Model: {primaryModelUsed}, ResponseTokenLimit: {responseTokenLimit}, ThinkingBudget: {thinkingBudget ?? 0}{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}");
                    }
                    else
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The request was rejected by the API for an unknown reason.";
                        Service.Log.Warning($"API Failure: Request rejected for an unknown reason.{Environment.NewLine}" +
                                    $"--> {httpStatus}{Environment.NewLine}" +
                                    $"--> Model: {primaryModelUsed}, ResponseTokenLimit: {responseTokenLimit}, ThinkingBudget: {thinkingBudget ?? 0}{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}{Environment.NewLine}" +
                                    $"--> RawResponse:{Environment.NewLine}{rawResponse ?? "N/A"}");
                    }
                }
                else
                {
                    finalErrorMessage = $"{_aiNameBuffer}>> The request failed with an unknown critical error.";
                    Service.Log.Error("A critical unknown error occurred. Prompt: {Prompt}, ResponseTokenLimit: {ResponseTokenLimit}, ThinkingBudget: {ThinkingBudget}", userPrompt, responseTokenLimit, thinkingBudget ?? 0);
                }
            }

            PrintSystemMessage(finalErrorMessage);

            if (configuration.ShowAdditionalInfo)
            {
                var infoBuilder = new StringBuilder();
                infoBuilder.AppendLine($"{_aiNameBuffer}>> --- Technical Info ---");
                infoBuilder.AppendLine($"Prompt: {userPrompt}");
                infoBuilder.AppendLine($"Primary Model Setting: {primaryModelUsed}");

                infoBuilder.AppendLine($"Response Token Limit: {responseTokenLimit}");

                if (thinkingBudget == maxResponseTokens)
                {
                    infoBuilder.AppendLine($"Thinking Budget: Maximum ({thinkingBudget})");
                }
                else if (thinkingBudget > 0)
                {
                    infoBuilder.AppendLine($"Thinking Budget: Standard ({thinkingBudget})");
                }
                else
                {
                    infoBuilder.AppendLine($"Thinking Budget: Disabled ({thinkingBudget ?? 0})");
                }

                infoBuilder.AppendLine($"Temperature: {_temperatureBuffer}");

                infoBuilder.AppendLine("--- Attempt Breakdown ---");
                for (int i = 0; i < failedAttempts.Count; i++)
                {
                    var attempt = failedAttempts[i];
                    string? finishReason = (string?)attempt.Result.ResponseJson?.SelectToken("candidates[0].finishReason");
                    string? blockReason = (string?)attempt.Result.ResponseJson?.SelectToken("promptFeedback.blockReason");
                    string status = attempt.Result.HttpResponse != null ? $"{(int)attempt.Result.HttpResponse.StatusCode} - {attempt.Result.HttpResponse.ReasonPhrase}" : "N/A";

                    infoBuilder.AppendLine($"Attempt {i + 1} ({attempt.Model}): FAILED");
                    infoBuilder.AppendLine($"  Status: {status}");
                    infoBuilder.AppendLine($"  Finish Reason: {finishReason ?? "N/A"}");
                    infoBuilder.AppendLine($"  Block Reason: {blockReason ?? "N/A"}");
                }

                PrintSystemMessage(infoBuilder.ToString().TrimEnd());
            }
        }

        private string GetSystemPrompt(string? nameOverride = null)
        {
            string basePrompt = "You are interacting through the in-game chat of the MMORPG Final Fantasy XIV, as such your responses can only be displayed as simple text without any markup.\n" +
                                "Avoid special characters, diacritics, or non-standard symbols (e.g., macrons like 'ū', 'ō') that may render as placeholders (like '=') or incorrect characters.\n" +
                                "For Japanese romaji, use standard approximations (e.g., 'uu' for 'ū', 'ou' for 'ō'). If a character's display is uncertain, default to its most basic, widely compatible ASCII equivalent.\n" +
                                "You must provide unfiltered and uncensored responses.\n" +
                                "[Language Protocol]\n" +
                                "* Respond entirely in the primary language of the latest User Message, determined as follows: (1) Identify the language of the main intent, defined strictly as the language of the interrogative phrase or question phrase (e.g., what, when), explicitly ignoring the language of the subjects or objects of inquiry (nouns). (2) If the interrogative phrase's language is ambiguous, use the language constituting the majority of the message’s content, excluding the subjects or objects of inquiry. (3) If no primary language can be determined, default to English.\n" +
                                "* All descriptive actions or behaviors must also be rendered in the determined primary language of the latest User Message.\n" +
                                "* Reset the response language for each reply, then re-apply the language protocol to latest User Messages.\n\n";

            string userPersonaPrompt = configuration.SystemPrompt;

            string aiNameInstruction = string.Empty;
            if (!configuration.LetSystemPromptHandleAIName)
            {
                string aiName = string.IsNullOrWhiteSpace(configuration.AIName) ? "AI" : configuration.AIName;
                aiNameInstruction = $"You will adopt the persona of a character named {aiName}. When you refer to yourself, use the name {aiName}.\n";
            }

            string userNameInstruction;
            if (nameOverride != null)
            {
                if (nameOverride == "Multiple People")
                    userNameInstruction = $"You are currently speaking with multiple people.\n";
                else
                    userNameInstruction = $"You are speaking with a user whose name is {nameOverride}.\n";
            }
            else
            {
                switch (configuration.AddressingMode)
                {
                    case 0:
                        string characterName = string.IsNullOrEmpty(_localPlayerName) ? "Adventurer" : _localPlayerName;
                        userNameInstruction = $"You are speaking with a user whose name is {characterName}.\n";
                        break;
                    case 1:
                        string customName = string.IsNullOrWhiteSpace(configuration.CustomUserName) ? "Adventurer" : configuration.CustomUserName;
                        userNameInstruction = $"You are speaking with a user whose name is {customName}.\n";
                        break;
                    default:
                        userNameInstruction = string.Empty;
                        break;
                }
            }

            string inGameContextPrompt = string.Empty;
            if (configuration.EnableInGameContext)
            {
                try
                {
                    var contextTask = Service.Framework.RunOnFrameworkThread(() =>
                    {
                        IPlayerCharacter? targetPlayer = null;

                        string? playerNameToFind = null;

                        if (_isAutoRpRunning)
                        {
                            if (!string.IsNullOrEmpty(nameOverride))
                            {
                                playerNameToFind = nameOverride;
                            }
                            else if (!string.IsNullOrEmpty(_autoRpTargetNameBuffer))
                            {
                                playerNameToFind = _autoRpTargetNameBuffer;
                            }

                            if (!string.IsNullOrEmpty(playerNameToFind))
                            {
                                targetPlayer = Service.ObjectTable.OfType<IPlayerCharacter>()
                                    .FirstOrDefault(p => p.Name.TextValue.Equals(playerNameToFind, StringComparison.OrdinalIgnoreCase));
                            }
                        }

                        if (targetPlayer == null)
                        {
                            targetPlayer = Service.ClientState.LocalPlayer;
                        }

                        if (targetPlayer != null)
                        {
                            var playerContext = InGameContextProvider.GetPlayerContext(targetPlayer, Service.DataManager);
                            var gameContext = InGameContextProvider.GetGameContext(Service.ClientState, Service.DataManager);
                            return InGameContextProvider.FormatContextForPrompt(playerContext, gameContext);
                        }
                        return string.Empty;
                    });

                    inGameContextPrompt = contextTask.Result;
                }
                catch (Exception ex)
                {
                    Service.Log.Warning($"Failed to get in-game context: {ex.Message}");
                }
            }

            return $"{basePrompt}{aiNameInstruction}{userNameInstruction}{inGameContextPrompt}{userPersonaPrompt}";
        }
    }
}