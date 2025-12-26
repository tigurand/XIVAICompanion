using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XIVAICompanion.Models;
using XIVAICompanion.Providers;
using XIVAICompanion.Utils;

namespace XIVAICompanion
{
    public partial class AICompanionPlugin
    {
        private static bool ContainsAnyIgnoreCase(string input, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            foreach (var n in needles)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (input.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string BuildRecentConversationSnippetForSearch(List<Content>? conversationHistory, int maxTurns = 8, int maxChars = 1200)
        {
            if (conversationHistory == null || conversationHistory.Count == 0) return "(none)";

            int startIndex = Math.Max(0, conversationHistory.Count - maxTurns);
            startIndex = Math.Max(startIndex, 2);

            var lines = new List<string>();
            for (int i = startIndex; i < conversationHistory.Count; i++)
            {
                var c = conversationHistory[i];
                string role = c.Role == "model" ? "Assistant" : "User";
                string text = string.Join("\n", c.Parts.Select(p => p.Text)).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                const int perTurnMax = 240;
                if (text.Length > perTurnMax) text = text.Substring(0, perTurnMax) + "...";

                lines.Add($"{role}: {text}");
            }

            string combined = string.Join("\n", lines);
            if (combined.Length > maxChars) combined = combined.Substring(combined.Length - maxChars);
            return string.IsNullOrWhiteSpace(combined) ? "(none)" : combined;
        }

        private static string ExtractInGameContextBlockForSearch(string systemPrompt, int maxChars = 1000)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt)) return string.Empty;

            int start = systemPrompt.IndexOf("=== Player Information ===", StringComparison.Ordinal);
            if (start < 0) return string.Empty;

            string block = systemPrompt.Substring(start);
            if (block.Length > maxChars) block = block.Substring(0, maxChars) + "...";
            return block.Trim();
        }

        private async Task<string> ComposeTavilyQueryAsync(string userQuery, string systemPrompt, List<Content>? conversationHistory, ModelProfile profile)
        {
            if (string.IsNullOrWhiteSpace(userQuery)) return userQuery;

            try
            {
                IAiProvider providerToUse = profile.ProviderType == AiProviderType.Gemini
                    ? (IAiProvider)new GeminiProvider(httpClient)
                    : (IAiProvider)new OpenAiProvider(httpClient);

                string recentConversation = BuildRecentConversationSnippetForSearch(conversationHistory);
                string gameContext = ExtractInGameContextBlockForSearch(systemPrompt);
                string todayLocal = DateTime.Now.ToString("yyyy-MM-dd");

                const string rewriteSystemPrompt =
                    "You rewrite a user's conversational message into ONE web search query. " +
                    "Return ONLY the query as plain text (no quotes, no markdown, no extra commentary).";

                var rewriteUserPrompt = new StringBuilder();
                rewriteUserPrompt.AppendLine("Rewrite the user's message into a self-contained web search query.");
                rewriteUserPrompt.AppendLine("Rules:");
                rewriteUserPrompt.AppendLine("- Make it explicit: include the game/app/topic name if implied by context.");
                rewriteUserPrompt.AppendLine("- Prefer official/commonly-used terms.");
                rewriteUserPrompt.AppendLine($"- If the user says 'today'/'now', include today's date: {todayLocal}.");
                rewriteUserPrompt.AppendLine("- Keep it concise and clear.");
                rewriteUserPrompt.AppendLine();
                if (!string.IsNullOrWhiteSpace(gameContext))
                {
                    rewriteUserPrompt.AppendLine("Environment context:");
                    rewriteUserPrompt.AppendLine(gameContext);
                    rewriteUserPrompt.AppendLine();
                }
                rewriteUserPrompt.AppendLine("Recent conversation context:");
                rewriteUserPrompt.AppendLine(recentConversation);
                rewriteUserPrompt.AppendLine();
                rewriteUserPrompt.AppendLine("User message:");
                rewriteUserPrompt.AppendLine(userQuery.Trim());

                var rewriteContents = new List<Content>
                {
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = rewriteSystemPrompt } } },
                    new Content { Role = "model", Parts = new List<Part> { new Part { Text = "Understood." } } },
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = rewriteUserPrompt.ToString().TrimEnd() } } }
                };

                var rewriteRequest = new ProviderRequest
                {
                    Model = profile.ModelId,
                    SystemPrompt = rewriteSystemPrompt,
                    ConversationHistory = rewriteContents,
                    MaxTokens = 128,
                    Temperature = 0.2,
                    UseWebSearch = false,
                    ThinkingBudget = null,
                    ShowThoughts = false
                };

                ProviderResult rewriteResult = await providerToUse.SendPromptAsync(rewriteRequest, profile, true);
                string rewritten = (rewriteResult.ResponseText ?? string.Empty).Trim();

                rewritten = rewritten.Trim().Trim('"', '\'', '`');
                rewritten = rewritten.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();

                if (string.IsNullOrWhiteSpace(rewritten)) return userQuery;
                if (rewritten.Length > 256) rewritten = rewritten.Substring(0, 256);

                return rewritten;
            }
            catch (Exception ex)
            {
                Service.Log.Warning($">> Tavily query rewrite failed; using raw query. Error: {ex.Message}");
                return userQuery;
            }
        }

        private async Task SendPrompt(string input, bool isStateless, OutputTarget outputTarget, string partnerName, bool isFreshLogin = true, bool tempSearchMode = false, bool tempThinkMode = false, bool tempFreshMode = false, bool tempOocMode = false)
        {
            var systemPrompt = GetSystemPrompt(partnerName);
            var removeLineBreaks = configuration.RemoveLineBreaks;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            var conversationHistory = GetHistoryForPlayer(partnerName);

            var failedAttempts = new List<(ModelProfile Profile, ProviderResult Result)>();

            var profilesToTry = new List<ModelProfile>();

            int initialProfileIndex = configuration.DefaultModelIndex;

            bool isThink = (configuration.ThinkMode || tempThinkMode) && !isFreshLogin;
            if (isThink && configuration.ThinkingModelIndex != -1)
            {
                initialProfileIndex = configuration.ThinkingModelIndex;
            }
            else if (isFreshLogin && configuration.GreetingModelIndex != -1)
            {
                initialProfileIndex = configuration.GreetingModelIndex;
            }

            if (initialProfileIndex != -1 && initialProfileIndex < configuration.ModelProfiles.Count)
            {
                for (int i = 0; i < configuration.ModelProfiles.Count; i++)
                {
                    int idx = (initialProfileIndex + i) % configuration.ModelProfiles.Count;
                    profilesToTry.Add(configuration.ModelProfiles[idx]);
                }
            }

            if (profilesToTry.Count == 0)
            {
                PrintSystemMessage($"{_aiNameBuffer}>> Error: No model profiles configured. Please add one in settings.");
                return;
            }

            foreach (var profile in profilesToTry)
            {
                ProviderResult result = await SendPromptInternal(input, profile, isStateless, outputTarget, systemPrompt, removeLineBreaks, showAdditionalInfo, false, null, conversationHistory, isFreshLogin, tempSearchMode, tempThinkMode, tempFreshMode, tempOocMode);
                if (result.WasSuccessful) return;
                failedAttempts.Add((profile, result));

                if (!configuration.EnableAutoFallback) break;
            }

            HandleApiError(failedAttempts, input);
        }

        private async Task SendAutoRpPrompt(string capturedMessage, XivChatType sourceType)
        {
            if (!TryEnterAutoRpProcessing())
            {
                return;
            }

            string rpPartnerName = _autoRpTargetNameBuffer;
            string finalRpSystemPrompt = GetSystemPrompt(rpPartnerName);
            var outputTarget = OutputTarget.GameChat;
            var removeLineBreaks = true;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            var conversationHistory = GetHistoryForPlayer(rpPartnerName);

            try
            {
                var delaySec = Math.Clamp(configuration.AutoRpConfig.InitialResponseDelaySeconds, 0.0f, 10.0f);
                if (delaySec > 0.01f)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySec));
                }

            var failedAttempts = new List<(ModelProfile Profile, ProviderResult Result)>();

            var profilesToTry = new List<ModelProfile>();
            int initialProfileIndex = configuration.DefaultModelIndex;

            if (initialProfileIndex != -1 && initialProfileIndex < configuration.ModelProfiles.Count)
            {
                for (int i = 0; i < configuration.ModelProfiles.Count; i++)
                {
                    int idx = (initialProfileIndex + i) % configuration.ModelProfiles.Count;
                    profilesToTry.Add(configuration.ModelProfiles[idx]);
                }
            }

            if (profilesToTry.Count == 0) return;

            foreach (var profile in profilesToTry)
            {
                ProviderResult result = await SendPromptInternal(capturedMessage, profile, false, outputTarget, finalRpSystemPrompt, removeLineBreaks, showAdditionalInfo, true, sourceType, conversationHistory, false, false, false, false);
                if (result.WasSuccessful)
                {
                    _lastRpResponseTimestamp = DateTime.Now;
                    return;
                }
                failedAttempts.Add((profile, result));

                if (!configuration.EnableAutoFallback) break;
            }

            HandleApiError(failedAttempts, capturedMessage);
            }
            finally
            {
                ExitAutoRpProcessing();
            }
        }

        private async Task SendAutoReplyPrompt(string capturedMessage, string senderName, XivChatType sourceType)
        {
            if (!TryEnterAutoRpProcessing())
            {
                return;
            }

            string finalRpSystemPrompt = GetSystemPrompt(senderName);
            var outputTarget = OutputTarget.GameChat;
            var removeLineBreaks = true;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            string historyName = (_openListenerModeBuffer && _mixedHistoryModeBuffer) ? "Multiple People" : senderName;
            var conversationHistory = GetHistoryForPlayer(historyName);

            try
            {
                var delaySec = Math.Clamp(configuration.AutoRpConfig.InitialResponseDelaySeconds, 0.0f, 10.0f);
                if (delaySec > 0.01f)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySec));
                }

            var failedAttempts = new List<(ModelProfile Profile, ProviderResult Result)>();

            var profilesToTry = new List<ModelProfile>();
            int initialProfileIndex = configuration.DefaultModelIndex;

            if (initialProfileIndex != -1 && initialProfileIndex < configuration.ModelProfiles.Count)
            {
                for (int i = 0; i < configuration.ModelProfiles.Count; i++)
                {
                    int idx = (initialProfileIndex + i) % configuration.ModelProfiles.Count;
                    profilesToTry.Add(configuration.ModelProfiles[idx]);
                }
            }

            if (profilesToTry.Count == 0) return;

            foreach (var profile in profilesToTry)
            {
                ProviderResult result = await SendPromptInternal(capturedMessage, profile, false, outputTarget, finalRpSystemPrompt, removeLineBreaks, showAdditionalInfo, true, sourceType, conversationHistory, false, false, false, false);
                if (result.WasSuccessful)
                {
                    _lastRpResponseTimestamp = DateTime.Now;
                    return;
                }
                failedAttempts.Add((profile, result));

                if (!configuration.EnableAutoFallback) break;
            }

            HandleApiError(failedAttempts, capturedMessage);
            }
            finally
            {
                ExitAutoRpProcessing();
            }
        }

        private async Task<ProviderResult> SendPromptInternal(string input, ModelProfile profile, bool isStateless, OutputTarget outputTarget, string systemPrompt,
                                                 bool removeLineBreaks, bool showAdditionalInfo, bool forceHistory = false, XivChatType? replyChannel = null,
                                                 List<Content>? conversationHistory = null, bool isFreshLogin = false, bool tempSearchMode = false, bool tempThinkMode = false, bool tempFreshMode = false, bool tempOocMode = false)
        {
            int responseTokensToUse = profile.MaxTokens;
            string currentPrompt = input.Replace('　', ' ').Trim();

            bool isSearch = (configuration.SearchMode || tempSearchMode) && !isFreshLogin;
            bool isThink = (configuration.ThinkMode || tempThinkMode) && !isFreshLogin;
            bool isFresh = (_chatFreshMode || tempFreshMode) && !isFreshLogin;
            bool isOoc = (_chatOocMode || tempOocMode) && !isFreshLogin;

            int thinkingBudget = isThink ? maxResponseTokens : defaultThinkingBudget;
            bool useWebSearch = isSearch;

            string finalUserPrompt = string.Empty;
            if (useWebSearch && profile.ProviderType == AiProviderType.Gemini && (!profile.UseTavilyInstead || string.IsNullOrEmpty(profile.TavilyApiKey)))
            {
                finalUserPrompt = "[SYSTEM COMMAND: GOOGLE SEARCH]\n" +
                    "1.  **PRIMARY DIRECTIVE:** Check if Google Search tool is needed to answer the *entire* User Message.\n" +
                    "2.  **SECONDARY DIRECTIVE:** If needed, immediately use the Google Search tool to answer the *entire* User Message.\n" +
                    "3.  **TERTIARY DIRECTIVE:** Adhere strictly to the Language Protocol, while still being consistent with your personality.\n" +
                    "4.  **RULES:** Do not converse. Do not acknowledge. Provide a direct, synthesized answer from the search results.\n\n";
            }
            finalUserPrompt += $"--- User Message ---\n{currentPrompt}";

            bool shouldPreSearchWithTavily = useWebSearch
                && !string.IsNullOrEmpty(profile.TavilyApiKey)
                && (profile.ProviderType == AiProviderType.OpenAiCompatible
                    || (profile.ProviderType == AiProviderType.Gemini && profile.UseTavilyInstead));

            bool didPreSearchWithTavily = false;
            if (shouldPreSearchWithTavily)
            {
                string tavilyQuery = await ComposeTavilyQueryAsync(currentPrompt, systemPrompt, conversationHistory, profile);
                string tavilyResults = await TavilySearchHelper.SearchAsync(tavilyQuery, profile.TavilyApiKey);
                const int maxTavilyChars = 6000;
                if (!string.IsNullOrEmpty(tavilyResults) && tavilyResults.Length > maxTavilyChars)
                    tavilyResults = tavilyResults.Substring(0, maxTavilyChars) + "\n... (truncated)";

                finalUserPrompt = "[SYSTEM COMMAND: TAVILY WEB SEARCH]\n" +
                                "Use the following web search results to answer the user, prefer them over prior knowledge.\n\n" +
                                tavilyResults + "\n\n" +
                                finalUserPrompt;

                useWebSearch = false;
                didPreSearchWithTavily = true;
            }

            if (!configuration.EnableConversationHistory)
            {
                isStateless = true;
            }

            List<Content> requestContents;
            Content? userTurn = null;

            if (isStateless || isFresh)
            {
                requestContents = new List<Content>
                {
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = systemPrompt } } },
                    new Content { Role = "model", Parts = new List<Part> { new Part { Text = $"Understood. I am {_aiNameBuffer}. I will follow all instructions." } } },
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = finalUserPrompt } } }
                };
            }
            else
            {
                var activeHistory = (conversationHistory != null) ? new List<Content>(conversationHistory) : new List<Content>();
                if (activeHistory.Count > 0)
                {
                    activeHistory[0] = new Content { Role = "user", Parts = new List<Part> { new Part { Text = systemPrompt } } };
                }
                else
                {
                    activeHistory.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = systemPrompt } } });
                    activeHistory.Add(new Content { Role = "model", Parts = new List<Part> { new Part { Text = $"Understood. I am {_aiNameBuffer}. I will follow all instructions." } } });
                }

                userTurn = new Content { Role = "user", Parts = new List<Part> { new Part { Text = finalUserPrompt } } };
                activeHistory.Add(userTurn);
                requestContents = activeHistory;

                if (configuration.ConversationHistoryLimit > 0)
                {
                    int maxHistoryItems = (configuration.ConversationHistoryLimit * 2) + 2;
                    if (requestContents.Count > maxHistoryItems)
                    {
                        requestContents.RemoveRange(2, requestContents.Count - maxHistoryItems);
                    }
                }
            }

            var request = new ProviderRequest
            {
                Model = profile.ModelId,
                SystemPrompt = systemPrompt,
                ConversationHistory = requestContents,
                MaxTokens = responseTokensToUse,
                Temperature = configuration.Temperature,
                UseWebSearch = useWebSearch,
                ThinkingBudget = thinkingBudget,
                ShowThoughts = configuration.ShowThoughts,
                IsThinkingEnabled = isThink
            };

            try
            {
                IAiProvider providerToUse = profile.ProviderType == AiProviderType.Gemini ? (IAiProvider)new GeminiProvider(httpClient) : (IAiProvider)new OpenAiProvider(httpClient);

                ProviderResult result = await providerToUse.SendPromptAsync(request, profile);

                if (!didPreSearchWithTavily && result.WasSuccessful && result.ResponseJson != null)
                {
                    bool toolCalled = false;
                    string searchQuery = string.Empty;

                    var toolCalls = result.ResponseJson.SelectToken("choices[0].message.tool_calls");
                    if (toolCalls is JArray calls && calls.Count > 0)
                    {
                        var firstCall = calls[0];

                        var functionName = firstCall?["function"]?["name"]?.Value<string>();
                        if (functionName == "web_search")
                        {
                            var args = firstCall?["function"]?["arguments"]?.Value<string>();
                            if (!string.IsNullOrWhiteSpace(args))
                            {
                                var parsedArgs = JObject.Parse(args);
                                searchQuery = parsedArgs["query"]?.Value<string>() ?? string.Empty;
                                toolCalled = true;
                            }
                        }
                    }

                    var geminiCalls = result.ResponseJson.SelectToken("candidates[0].content.parts");
                    if (geminiCalls != null && profile.UseTavilyInstead && !string.IsNullOrEmpty(profile.TavilyApiKey))
                    {
                        var callPart = geminiCalls.FirstOrDefault(p => p["functionCall"] != null);
                        if (callPart != null && callPart["functionCall"]?["name"]?.ToString() == "web_search")
                        {
                            searchQuery = callPart["functionCall"]?["args"]?["query"]?.ToString() ?? string.Empty;
                            toolCalled = true;
                        }
                    }

                    if (toolCalled && !string.IsNullOrEmpty(searchQuery))
                    {
                        string searchQueryToUse = searchQuery;

                        if (!string.IsNullOrEmpty(profile.TavilyApiKey)
                            && !ContainsAnyIgnoreCase(searchQuery, "ffxiv", "final fantasy xiv", "ff14", "final fantasy 14"))
                        {
                            searchQueryToUse = await ComposeTavilyQueryAsync(searchQuery, systemPrompt, request.ConversationHistory, profile);
                        }

                        Service.Log.Info($">> Web search: '{searchQuery}' => '{searchQueryToUse}'");
                        string searchResults = await TavilySearchHelper.SearchAsync(searchQueryToUse, profile.TavilyApiKey);
                        var followUpHistory = new List<Content>(request.ConversationHistory);
                        followUpHistory.Add(new Content { Role = "model", Parts = new List<Part> { new Part { Text = (profile.ProviderType == AiProviderType.OpenAiCompatible ? "TOOL_RESPONSE: " : "SEARCH_RESULTS: ") + searchResults } } });
                        request.ConversationHistory = followUpHistory;
                        result = await providerToUse.SendPromptAsync(request, profile, true);
                    }
                }

                if (!result.WasSuccessful)
                {
                    if (configuration.EnableConversationHistory && userTurn != null && conversationHistory != null)
                        conversationHistory.Remove(userTurn);
                    return result;
                }

                string sanitizedText = result.ResponseText ?? string.Empty;

                if ((forceHistory || configuration.EnableConversationHistory) && !isStateless && !isFresh && conversationHistory != null)
                {
                    bool needsSync = conversationHistory.Count == 0 || (userTurn != null && !conversationHistory.Contains(userTurn));
                    if (needsSync)
                    {
                        conversationHistory.Clear();
                        conversationHistory.AddRange(requestContents);
                    }
                    conversationHistory.Add(new Content { Role = "model", Parts = new List<Part> { new Part { Text = sanitizedText } } });
                }

                string finalResponse = removeLineBreaks ? sanitizedText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("  ", " ") : sanitizedText;
                _currentSessionChatLog.Add(new ChatMessage { Timestamp = DateTime.Now, Author = configuration.AIName, Message = finalResponse });
                _shouldScrollToBottom = true;

                switch (outputTarget)
                {
                    case OutputTarget.GameChat:
                        await Service.Framework.RunOnFrameworkThread(() =>
                        {
                            string commandPrefix = string.Empty;
                            if (configuration.AutoRpConfig.ReplyInSpecificChannel) commandPrefix = GetPrefixForChannelIndex(configuration.AutoRpConfig.SpecificReplyChannel);
                            else if (configuration.AutoRpConfig.AutoReplyToAllTells && replyChannel.HasValue && replyChannel.Value == XivChatType.TellIncoming) commandPrefix = "/r ";
                            else if (configuration.AutoRpConfig.ReplyInOriginalChannel && replyChannel.HasValue) commandPrefix = GetReplyPrefix(replyChannel.Value);
                            SendMessageToGameChat(finalResponse, commandPrefix: commandPrefix, isAutoRp: true);
                        });
                        break;
                    case OutputTarget.PluginDebug:
                    default:
                        foreach (var chunk in SplitIntoChunks(finalResponse, 1000)) PrintMessageToChat($"{configuration.AIName}: {chunk}");
                        break;
                }

                if (showAdditionalInfo)
                {
                    var infoBuilder = new StringBuilder();
                    infoBuilder.AppendLine($"{_aiNameBuffer}>> --- Technical Info ---");
                    infoBuilder.AppendLine($"Provider: {providerToUse.Name}");
                    infoBuilder.AppendLine($"Model: {profile.ModelId}");
                    infoBuilder.AppendLine($"Tokens=[P:{result.PromptTokens}, R:{result.ResponseTokens}, T:{result.TotalTokens}]");
                    infoBuilder.AppendLine($"Response Time: {result.ResponseTimeMs}ms");
                    PrintSystemMessage(infoBuilder.ToString());
                }

                string reasoningInfo = providerToUse.Name == "OpenAI" ? (isThink ? $"ReasoningEffort='{ProviderConstants.OpenAIReasoningEffort}'" : "ReasoningEffort='none'") : $"ThinkingBudget={thinkingBudget}";

                Service.Log.Info(
                    $"API Call Success: Provider='{providerToUse.Name}', Model='{profile.ModelId}', HTTP Status={(int?)result.HttpResponse?.StatusCode} - {result.HttpResponse?.StatusCode}, " +
                    $"ResponseTokenLimit={responseTokensToUse}, {reasoningInfo}, Temperature={configuration.Temperature}, " +
                    $"Tokens=[P:{result.PromptTokens}, R:{result.ResponseTokens}, T:{result.TotalTokens}], ResponseTime={result.ResponseTimeMs}ms"
                );

                return result;
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Error in SendPromptInternal");
                return new ProviderResult { WasSuccessful = false, Exception = ex, ModelUsed = profile.ModelId };
            }
        }

        private void HandleApiError(List<(ModelProfile Profile, ProviderResult Result)> failedAttempts, string input)
        {
            if (failedAttempts.Count == 0) return;

            var primaryFailure = failedAttempts[0];
            var primaryResult = primaryFailure.Result;
            string userPrompt = input.Trim();
            string finalErrorMessage;

            if (configuration.EnableAutoFallback && failedAttempts.Count > 1)
            {
                string? finishReason = (string?)primaryResult.ResponseJson?.SelectToken("candidates[0].finishReason") ?? (string?)primaryResult.ResponseJson?.SelectToken("choices[0].finishReason");
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
                else if (!string.IsNullOrEmpty(finishReason) && finishReason != "stop")
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
                    string? attemptRawResponse = attemptResult.ResponseJson?.ToString(Newtonsoft.Json.Formatting.Indented);

                    logBuilder.AppendLine($"--- Attempt {i + 1} of {failedAttempts.Count} ({attempt.Profile.ProviderType} - {attempt.Profile.ModelId}) ---");
                    logBuilder.AppendLine($"--> Status: {attemptHttpStatus}");
                    logBuilder.AppendLine($"--> Params: ResponseTokenLimit={attempt.Profile.MaxTokens}, ThinkingBudget={attempt.Profile.MaxTokens}, Temperature={configuration.Temperature}"); // Approximate, since thinkingBudget not stored
                    logBuilder.AppendLine($"--> Tokens: [P:{attemptResult.PromptTokens}, R:{attemptResult.ResponseTokens}, T:{attemptResult.TotalTokens}]");
                    logBuilder.AppendLine($"--> ResponseTime: {attemptResult.ResponseTimeMs}ms");
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
                    Service.Log.Error(primaryResult.Exception, $"A critical network or parsing error occurred. Provider: {primaryFailure.Profile.ProviderType}, Model: {primaryFailure.Profile.ModelId}, Prompt: {userPrompt}, ResponseTokenLimit: {primaryFailure.Profile.MaxTokens}, Temperature: {configuration.Temperature}");
                }
                else if (primaryResult.ResponseJson != null && primaryResult.HttpResponse != null)
                {
                    string? blockReason = (string?)primaryResult.ResponseJson.SelectToken("promptFeedback.blockReason");
                    string? finishReason = (string?)primaryResult.ResponseJson.SelectToken("candidates[0].finishReason") ?? (string?)primaryResult.ResponseJson.SelectToken("choices[0].finishReason");

                    if (primaryResult.HttpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: API rate limit reached. This could be Requests Per Minute (RPM) or Requests Per Day (RPD).";
                        Service.Log.Warning($"API Failure: Rate Limit Exceeded.{Environment.NewLine}" +
                                    $"--> Provider: {primaryFailure.Profile.ProviderType}, Model: {primaryFailure.Profile.ModelId}{Environment.NewLine}" +
                                    $"--> Status: {(int)primaryResult.HttpResponse.StatusCode} {primaryResult.HttpResponse.ReasonPhrase}{Environment.NewLine}" +
                                    $"--> Params: ResponseTokenLimit={primaryFailure.Profile.MaxTokens}, Temperature={configuration.Temperature}{Environment.NewLine}" +
                                    $"--> Tokens: [P:{primaryResult.PromptTokens}, R:{primaryResult.ResponseTokens}, T:{primaryResult.TotalTokens}]{Environment.NewLine}" +
                                    $"--> ResponseTime: {primaryResult.ResponseTimeMs}ms{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}");
                    }
                    else if (!string.IsNullOrEmpty(finishReason) && finishReason != "stop")
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The response was terminated by the API. Reason: {finishReason}.";
                        if (finishReason == "length")
                        {
                            finalErrorMessage += " You can increase this value in /ai cfg.";
                        }
                        Service.Log.Warning($"API Failure: Response Terminated.{Environment.NewLine}" +
                                    $"--> Reason: {finishReason}{Environment.NewLine}" +
                                    $"--> Provider: {primaryFailure.Profile.ProviderType}, Model: {primaryFailure.Profile.ModelId}{Environment.NewLine}" +
                                    $"--> Status: {(int)primaryResult.HttpResponse.StatusCode} {primaryResult.HttpResponse.ReasonPhrase}{Environment.NewLine}" +
                                    $"--> Params: ResponseTokenLimit={primaryFailure.Profile.MaxTokens}, Temperature={configuration.Temperature}{Environment.NewLine}" +
                                    $"--> Tokens: [P:{primaryResult.PromptTokens}, R:{primaryResult.ResponseTokens}, T:{primaryResult.TotalTokens}]{Environment.NewLine}" +
                                    $"--> ResponseTime: {primaryResult.ResponseTimeMs}ms{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}");
                    }
                    else if (!string.IsNullOrEmpty(blockReason))
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The prompt was blocked by the API. Reason: {blockReason}.";
                        Service.Log.Warning($"API Failure: Prompt Blocked.{Environment.NewLine}" +
                                    $"--> Reason: {blockReason}{Environment.NewLine}" +
                                    $"--> Provider: {primaryFailure.Profile.ProviderType}, Model: {primaryFailure.Profile.ModelId}{Environment.NewLine}" +
                                    $"--> Status: {(int)primaryResult.HttpResponse.StatusCode} {primaryResult.HttpResponse.ReasonPhrase}{Environment.NewLine}" +
                                    $"--> Params: ResponseTokenLimit={primaryFailure.Profile.MaxTokens}, Temperature={configuration.Temperature}{Environment.NewLine}" +
                                    $"--> Tokens: [P:{primaryResult.PromptTokens}, R:{primaryResult.ResponseTokens}, T:{primaryResult.TotalTokens}]{Environment.NewLine}" +
                                    $"--> ResponseTime: {primaryResult.ResponseTimeMs}ms{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}");
                    }
                    else
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The request was rejected by the API for an unknown reason.";
                        Service.Log.Warning($"API Failure: Request rejected for an unknown reason.{Environment.NewLine}" +
                                    $"--> Provider: {primaryFailure.Profile.ProviderType}, Model: {primaryFailure.Profile.ModelId}{Environment.NewLine}" +
                                    $"--> Status: {(int)primaryResult.HttpResponse.StatusCode} {primaryResult.HttpResponse.ReasonPhrase}{Environment.NewLine}" +
                                    $"--> Params: ResponseTokenLimit={primaryFailure.Profile.MaxTokens}, Temperature={configuration.Temperature}{Environment.NewLine}" +
                                    $"--> Tokens: [P:{primaryResult.PromptTokens}, R:{primaryResult.ResponseTokens}, T:{primaryResult.TotalTokens}]{Environment.NewLine}" +
                                    $"--> ResponseTime: {primaryResult.ResponseTimeMs}ms{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}{Environment.NewLine}" +
                                    $"--> RawResponse:{Environment.NewLine}{primaryResult.ResponseJson?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "N/A"}");
                    }
                }
                else
                {
                    finalErrorMessage = $"{_aiNameBuffer}>> The request failed with an unknown critical error.";
                    Service.Log.Error($"A critical unknown error occurred. Provider: {primaryFailure.Profile.ProviderType}, Model: {primaryFailure.Profile.ModelId}, Prompt: {userPrompt}, ResponseTokenLimit: {primaryFailure.Profile.MaxTokens}, Temperature: {configuration.Temperature}");
                }
            }

            PrintSystemMessage(finalErrorMessage);

            if (configuration.ShowAdditionalInfo)
            {
                var infoBuilder = new StringBuilder();
                infoBuilder.AppendLine($"{_aiNameBuffer}>> --- Technical Info ---");
                infoBuilder.AppendLine($"Provider: {primaryFailure.Profile.ProviderType}");
                infoBuilder.AppendLine($"Prompt: {userPrompt}");
                infoBuilder.AppendLine($"Primary Model Setting: {primaryFailure.Profile.ModelId}");

                if (failedAttempts.Count > 1)
                {
                    infoBuilder.AppendLine("--- Attempt Breakdown ---");
                    for (int i = 0; i < failedAttempts.Count; i++)
                    {
                        var attempt = failedAttempts[i];
                        string? finishReason = (string?)attempt.Result.ResponseJson?.SelectToken("candidates[0].finishReason") ?? (string?)attempt.Result.ResponseJson?.SelectToken("choices[0].finishReason");
                        string? blockReason = (string?)attempt.Result.ResponseJson?.SelectToken("promptFeedback.blockReason");
                        string status = attempt.Result.HttpResponse != null ? $"{(int)attempt.Result.HttpResponse.StatusCode} - {attempt.Result.HttpResponse.ReasonPhrase}" : "N/A";

                        infoBuilder.AppendLine($"Attempt {i + 1} ({attempt.Profile.ProviderType} - {attempt.Profile.ModelId}): FAILED");
                        infoBuilder.AppendLine($"  Status: {status}");
                        infoBuilder.AppendLine($"  Finish Reason: {finishReason ?? "N/A"}");
                        infoBuilder.AppendLine($"  Block Reason: {blockReason ?? "N/A"}");
                        infoBuilder.AppendLine($"  Tokens: [P:{attempt.Result.PromptTokens}, R:{attempt.Result.ResponseTokens}, T:{attempt.Result.TotalTokens}]");
                        infoBuilder.AppendLine($"  Response Time: {attempt.Result.ResponseTimeMs}ms");
                    }
                }

                PrintSystemMessage(infoBuilder.ToString().TrimEnd());
            }
        }

        private void ProcessPrompt(string rawPrompt, string? historyOverride = null)
        {
            string currentPrompt = rawPrompt.Replace('　', ' ').Trim();
            bool isFresh = _chatFreshMode || _tempFreshMode;
            bool isOoc = _chatOocMode || _tempOocMode;
            string processedPrompt = currentPrompt;
            string userMessageContent = currentPrompt;

            string partnerName;
            if (isOoc && !string.IsNullOrEmpty(_currentRpPartnerName)) partnerName = _currentRpPartnerName;
            else if (_isAutoRpRunning && !string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer)) partnerName = _autoRpTargetNameBuffer;
            else partnerName = GetPlayerDisplayName();

            _currentSessionChatLog.Add(new ChatMessage { Timestamp = DateTime.Now, Author = partnerName, Message = isOoc ? $"[OOC] {userMessageContent}" : userMessageContent });
            var historyEntry = historyOverride ?? rawPrompt;
            _chatInputHistory.Remove(historyEntry);
            _chatInputHistory.Add(historyEntry);
            if (_chatInputHistory.Count > 20) _chatInputHistory.RemoveAt(0);
            _chatHistoryIndex = -1;
            _shouldScrollToBottom = true;

            string processedPromptForApi = ProcessTextAliases(processedPrompt);
            OutputTarget outputTarget = isOoc ? OutputTarget.PluginWindow : (_isAutoRpRunning ? OutputTarget.GameChat : OutputTarget.PluginDebug);
            bool isStateless = isFresh;

            if (configuration.ShowPrompt && !isOoc && !_isAutoRpRunning) PrintMessageToChat($"{GetPlayerDisplayName()}: {userMessageContent}");

            bool tSearch = _tempSearchMode;
            bool tThink = _tempThinkMode;
            bool tFresh = _tempFreshMode;
            bool tOoc = _tempOocMode;

            Task.Run(async () =>
            {
                await SendPrompt(processedPromptForApi, isStateless, outputTarget, partnerName, false, tSearch, tThink, tFresh, tOoc);
                _tempSearchMode = false; _tempThinkMode = false; _tempFreshMode = false; _tempOocMode = false;
            });
        }

        private string GetSystemPrompt(string? nameOverride = null)
        {
            string basePrompt = "You are interacting through the in-game chat of the MMORPG Final Fantasy XIV, as such your responses can only be displayed as simple text without any markup.\n" +
                                "Avoid special characters, diacritics, or non-standard symbols (e.g., macrons like 'ū', 'ō') that may render as placeholders (like '=') or incorrect characters.\n" +
                                "For Japanese romaji, use standard approximations (e.g., 'uu' for 'ū', 'ou' for 'ō'). If a character's display is uncertain, default to its most basic, widely compatible ASCII equivalent.\n" +
                                "When the user asks about the time, if you have their time zone information, provide the answer in their time zone (e.g., convert the time accordingly).\n" +
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

            string userNameInstruction = string.Empty;
            if (nameOverride != null) userNameInstruction = $"You are speaking with {nameOverride}.\n";
            else
            {
                switch (configuration.AddressingMode)
                {
                    case 0: userNameInstruction = $"You are speaking with {(_localPlayerName ?? "Adventurer")}.\n"; break;
                    case 1: userNameInstruction = $"You are speaking with {(configuration.CustomUserName ?? "Adventurer")}.\n"; break;
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
                        if (_isAutoRpRunning && !string.IsNullOrEmpty(nameOverride))
                        {
                            targetPlayer = Service.ObjectTable.OfType<IPlayerCharacter>().FirstOrDefault(p => p.Name.TextValue.Equals(nameOverride, StringComparison.OrdinalIgnoreCase));
                        }
                        targetPlayer ??= Service.ObjectTable.LocalPlayer;
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
                catch { }
            }

            return $"{basePrompt}{aiNameInstruction}{userNameInstruction}{inGameContextPrompt}{userPersonaPrompt}";
        }
    }
}
