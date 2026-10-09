using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers.Text;
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

        private sealed class SearchQueryDecision
        {
            public bool Failed { get; init; }
            public string? Query { get; init; }
        }

        private async Task<SearchQueryDecision> DecideSearchQueryAsync(string userQuery, string systemPrompt, List<Content>? conversationHistory, ModelProfile profile)
        {
            if (string.IsNullOrWhiteSpace(userQuery)) return new SearchQueryDecision { Query = null };

            try
            {
                IAiProvider providerToUse = profile.ProviderType == AiProviderType.Gemini
                    ? (IAiProvider)new GeminiProvider(httpClient)
                    : (IAiProvider)new OpenAiProvider(httpClient);

                string recentConversation = BuildRecentConversationSnippetForSearch(conversationHistory);
                string gameContext = ExtractInGameContextBlockForSearch(systemPrompt);
                string todayLocal = DateTime.Now.ToString("yyyy-MM-dd");

                const string decideSystemPrompt =
                    "You decide whether a chat message needs a live web search, and if so you rewrite it into ONE search query. " +
                    "Reply with exactly NO_SEARCH when the message can be answered from the conversation and general knowledge " +
                    "(casual chat, opinions, roleplay, or facts you already know). " +
                    "Otherwise reply with ONE self-contained web search query that resolves pronouns and references using the conversation. " +
                    "Output ONLY either NO_SEARCH or the query as plain text, with no quotes, markdown, or explanation.";

                var decideUserPrompt = new StringBuilder();
                decideUserPrompt.AppendLine("Decide whether the user's latest message requires up-to-date or external information from the web.");
                decideUserPrompt.AppendLine("Rules:");
                decideUserPrompt.AppendLine("- If a web search is NOT needed, reply exactly: NO_SEARCH");
                decideUserPrompt.AppendLine("- If it IS needed, reply with ONE self-contained search query and nothing else.");
                decideUserPrompt.AppendLine("- Resolve pronouns/references using the conversation (e.g. 'it' -> the subject currently being discussed).");
                decideUserPrompt.AppendLine("- Make it explicit: include the game/app/topic name if implied by context.");
                decideUserPrompt.AppendLine("- Prefer official/commonly-used terms and keep it concise.");
                decideUserPrompt.AppendLine($"- If the user says 'today'/'now', include today's date: {todayLocal}.");
                decideUserPrompt.AppendLine();
                if (!string.IsNullOrWhiteSpace(gameContext))
                {
                    decideUserPrompt.AppendLine("Environment context:");
                    decideUserPrompt.AppendLine(gameContext);
                    decideUserPrompt.AppendLine();
                }
                decideUserPrompt.AppendLine("Recent conversation context:");
                decideUserPrompt.AppendLine(recentConversation);
                decideUserPrompt.AppendLine();
                decideUserPrompt.AppendLine("User message:");
                decideUserPrompt.AppendLine(userQuery.Trim());

                var decideContents = new List<Content>
                {
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = decideSystemPrompt } } },
                    new Content { Role = "model", Parts = new List<Part> { new Part { Text = "Understood." } } },
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = decideUserPrompt.ToString().TrimEnd() } } }
                };

                var decideRequest = new ProviderRequest
                {
                    Model = profile.ModelId,
                    SystemPrompt = decideSystemPrompt,
                    ConversationHistory = decideContents,
                    MaxTokens = 128,
                    Temperature = 0.2,
                    UseWebSearch = false,
                    ThinkingBudget = null,
                    ShowThoughts = false
                };

                ProviderResult decideResult = await providerToUse.SendPromptAsync(decideRequest, profile);
                string verdict = (decideResult.ResponseText ?? string.Empty).Trim();

                verdict = verdict.Trim().Trim('"', '\'', '`');
                verdict = verdict.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();

                if (!decideResult.WasSuccessful)
                {
                    return new SearchQueryDecision { Failed = true };
                }

                if (string.IsNullOrWhiteSpace(verdict))
                {
                    return new SearchQueryDecision { Failed = true };
                }

                string normalized = verdict.Replace("_", "").Replace("-", "").Replace(" ", "").Replace(".", "").Trim();
                if (normalized.StartsWith("NOSEARCH", StringComparison.OrdinalIgnoreCase))
                {
                    return new SearchQueryDecision { Query = null };
                }

                string rewritten = verdict;
                if (rewritten.Length > 256) rewritten = rewritten.Substring(0, 256);

                return new SearchQueryDecision { Query = rewritten };
            }
            catch (Exception)
            {
                return new SearchQueryDecision { Failed = true };
            }
        }

        private static string GetSearchEngineApiKey(ModelProfile profile, SearchEngineType engine)
        {
            switch (engine)
            {
                case SearchEngineType.Tavily: return profile.TavilyApiKey ?? string.Empty;
                case SearchEngineType.Exa: return profile.ExaApiKey ?? string.Empty;
                case SearchEngineType.Parallel: return profile.ParallelApiKey ?? string.Empty;
                case SearchEngineType.Firecrawl: return profile.FirecrawlApiKey ?? string.Empty;
                default: return string.Empty;
            }
        }

        private List<SearchEngineType> BuildSearchEngineOrder(ModelProfile profile, SearchEngineType primary)
        {
            var order = new List<SearchEngineType>();

            if (!string.IsNullOrEmpty(GetSearchEngineApiKey(profile, primary)))
            {
                order.Add(primary);
            }

            if (configuration.EnableSearchEngineFallback)
            {
                foreach (var engine in new[] { SearchEngineType.Tavily, SearchEngineType.Exa, SearchEngineType.Parallel, SearchEngineType.Firecrawl })
                {
                    if (engine == primary) continue;
                    if (!string.IsNullOrEmpty(GetSearchEngineApiKey(profile, engine)))
                    {
                        order.Add(engine);
                    }
                }
            }

            return order;
        }

        private async Task SendPrompt(string input, bool isStateless, OutputTarget outputTarget, string partnerName, bool isLogin = false, bool tempSearchMode = false, bool tempThinkMode = false, bool tempFreshMode = false, bool tempWhisperMode = false)
        {
            var systemPrompt = GetSystemPrompt(partnerName);
            var removeLineBreaks = configuration.RemoveLineBreaks;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            var conversationHistory = GetHistoryForPlayer(partnerName);

            var failedAttempts = new List<(ModelProfile Profile, ProviderResult Result)>();

            var profilesToTry = new List<ModelProfile>();

            int initialProfileIndex = configuration.DefaultModelIndex;

            bool isThink = (configuration.ThinkMode || tempThinkMode) && !isLogin;
            if (isThink && configuration.ThinkingModelIndex != -1)
            {
                initialProfileIndex = configuration.ThinkingModelIndex;
            }
            else if (isLogin && configuration.GreetingModelIndex != -1)
            {
                initialProfileIndex = configuration.GreetingModelIndex;
            }

            if (initialProfileIndex != -1 && initialProfileIndex < configuration.ModelProfiles.Count)
            {
                profilesToTry.Add(configuration.ModelProfiles[initialProfileIndex]);

                for (int i = 1; i < configuration.ModelProfiles.Count; i++)
                {
                    int idx = (initialProfileIndex + i) % configuration.ModelProfiles.Count;
                    var candidate = configuration.ModelProfiles[idx];
                    if (candidate.UseAsFallback)
                    {
                        profilesToTry.Add(candidate);
                    }
                }
            }

            if (profilesToTry.Count == 0)
            {
                PrintSystemMessage($"{_aiNameBuffer}>> Error: No model profiles configured. Please add one in settings.");
                return;
            }

            for (int i = 0; i < profilesToTry.Count; i++)
            {
                var profile = profilesToTry[i];
                ProviderResult result = await SendPromptInternal(input, profile, isStateless, outputTarget, systemPrompt, removeLineBreaks, showAdditionalInfo, false, null, conversationHistory, isLogin, tempSearchMode, tempThinkMode, tempFreshMode, tempWhisperMode, i > 0);
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
                    profilesToTry.Add(configuration.ModelProfiles[initialProfileIndex]);

                    for (int i = 1; i < configuration.ModelProfiles.Count; i++)
                    {
                        int idx = (initialProfileIndex + i) % configuration.ModelProfiles.Count;
                        var candidate = configuration.ModelProfiles[idx];
                        if (candidate.UseAsFallback)
                        {
                            profilesToTry.Add(candidate);
                        }
                    }
                }

                if (profilesToTry.Count == 0) return;

                for (int i = 0; i < profilesToTry.Count; i++)
                {
                    var profile = profilesToTry[i];
                    ProviderResult result = await SendPromptInternal(capturedMessage, profile, false, outputTarget, finalRpSystemPrompt, removeLineBreaks, showAdditionalInfo, true, sourceType, conversationHistory, false, false, false, false, i > 0);
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
                    profilesToTry.Add(configuration.ModelProfiles[initialProfileIndex]);

                    for (int i = 1; i < configuration.ModelProfiles.Count; i++)
                    {
                        int idx = (initialProfileIndex + i) % configuration.ModelProfiles.Count;
                        var candidate = configuration.ModelProfiles[idx];
                        if (candidate.UseAsFallback)
                        {
                            profilesToTry.Add(candidate);
                        }
                    }
                }

                if (profilesToTry.Count == 0) return;

                for (int i = 0; i < profilesToTry.Count; i++)
                {
                    var profile = profilesToTry[i];
                    ProviderResult result = await SendPromptInternal(capturedMessage, profile, false, outputTarget, finalRpSystemPrompt, removeLineBreaks, showAdditionalInfo, true, sourceType, conversationHistory, false, false, false, false, i > 0);
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
                                                 List<Content>? conversationHistory = null, bool isFreshLogin = false, bool tempSearchMode = false, bool tempThinkMode = false, bool tempFreshMode = false, bool tempWhisperMode = false, bool isFallbackAttempt = false)
        {
            int responseTokensToUse = profile.MaxTokens;
            string currentPrompt = input.Replace('　', ' ').Trim();

            bool isSearch = (configuration.SearchMode || tempSearchMode) && !isFreshLogin;
            bool forceSearch = tempSearchMode && !isFreshLogin;
            bool isThink = (configuration.ThinkMode || tempThinkMode) && !isFreshLogin;
            bool isFresh = (_chatFreshMode || tempFreshMode) && !isFreshLogin;
            bool isWhisper = (_chatWhisperMode || tempWhisperMode) && !isFreshLogin;

            int thinkingBudget = isThink ? maxResponseTokens : defaultThinkingBudget;
            bool useWebSearch = isSearch;

            string finalUserPrompt = currentPrompt;

            string effectiveSystemPrompt = systemPrompt;

            const string googleSearchCommand = "\n\n[SYSTEM COMMAND: GOOGLE SEARCH]\n" +
                "1.  **PRIMARY DIRECTIVE:** Check if Google Search tool is needed to answer the *entire* User Message.\n" +
                "2.  **SECONDARY DIRECTIVE:** If needed, immediately use the Google Search tool to answer the *entire* User Message.\n" +
                "3.  **RULES:** Do not converse. Do not acknowledge. Provide a direct, synthesized answer from the search results.";

            const string googleSearchForcedCommand = "\n\n[SYSTEM COMMAND: GOOGLE SEARCH - REQUIRED]\n" +
                "1.  **PRIMARY DIRECTIVE:** You MUST use the Google Search tool now to answer the *entire* User Message.\n" +
                "2.  **SECONDARY DIRECTIVE:** Do not rely on prior knowledge alone; base your answer on the fresh search results you retrieve.\n" +
                "3.  **RULES:** Do not converse. Do not acknowledge. Provide a direct, synthesized answer from the search results.";

            SearchEngineType selectedSearchEngine = profile.SearchEngine;
            bool usedGoogleSearch = false;
            bool usedExternalSearch = false;
            bool externalSearchAttempted = false;
            bool searchDecisionSkipped = false;
            bool searchDecisionFailed = false;
            SearchEngineType usedSearchEngine = SearchEngineType.Default;

            if (useWebSearch)
            {
                if (selectedSearchEngine == SearchEngineType.Default)
                {
                    if (profile.ProviderType == AiProviderType.Gemini)
                    {
                        effectiveSystemPrompt += forceSearch ? googleSearchForcedCommand : googleSearchCommand;
                        usedGoogleSearch = true;
                    }
                }
                else
                {
                    var searchEngineOrder = BuildSearchEngineOrder(profile, selectedSearchEngine);
                    bool geminiFallbackAvailable = profile.ProviderType == AiProviderType.Gemini;

                    if (searchEngineOrder.Count == 0)
                    {
                        if (geminiFallbackAvailable)
                        {
                            effectiveSystemPrompt += forceSearch ? googleSearchForcedCommand : googleSearchCommand;
                            usedGoogleSearch = true;
                            useWebSearch = forceSearch;
                        }
                        else
                        {
                            useWebSearch = false;
                        }
                    }
                    else
                    {
                        externalSearchAttempted = true;
                        string? searchQuery;

                        if (forceSearch)
                        {
                            searchQuery = currentPrompt;
                        }
                        else
                        {
                            SearchQueryDecision decision = await DecideSearchQueryAsync(currentPrompt, systemPrompt, conversationHistory, profile);
                            searchQuery = decision.Query;

                            if (decision.Failed)
                            {
                                searchDecisionFailed = true;
                                searchQuery = currentPrompt;
                            }

                            if (string.IsNullOrEmpty(searchQuery))
                            {
                                searchDecisionSkipped = true;
                            }
                        }

                        if (!string.IsNullOrEmpty(searchQuery))
                        {
                            foreach (var engine in searchEngineOrder)
                            {
                                string engineKey = GetSearchEngineApiKey(profile, engine);
                                if (string.IsNullOrEmpty(engineKey)) continue;

                                WebSearchResult searchResult = await SearchEngineHelper.SearchAsync(engine, searchQuery, engineKey);
                                if (searchResult != null && searchResult.Success && !string.IsNullOrWhiteSpace(searchResult.Text))
                                {
                                    string results = searchResult.Text;
                                    const int maxSearchChars = 6000;
                                    if (results.Length > maxSearchChars)
                                        results = results.Substring(0, maxSearchChars) + "\n... (truncated)";

                                    effectiveSystemPrompt += $"\n\n[SYSTEM COMMAND: {SearchEngineHelper.GetEngineDisplayName(engine).ToUpperInvariant()} WEB SEARCH]\n" +
                                                    "Use the following web search results to answer the user, prefer them over prior knowledge.\n\n" +
                                                    results;

                                    usedExternalSearch = true;
                                    usedSearchEngine = engine;
                                    break;
                                }

                                if (!configuration.EnableSearchEngineFallback) break;
                            }
                        }

                        if (forceSearch && !usedExternalSearch && geminiFallbackAvailable)
                        {
                            effectiveSystemPrompt += googleSearchForcedCommand;
                            usedGoogleSearch = true;
                            useWebSearch = true;
                        }
                        else
                        {
                            useWebSearch = false;
                        }
                    }
                }
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
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = effectiveSystemPrompt } } },
                    new Content { Role = "model", Parts = new List<Part> { new Part { Text = $"Understood. I am {_aiNameBuffer}. I will follow all instructions." } } },
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = finalUserPrompt } } }
                };
            }
            else
            {
                var activeHistory = (conversationHistory != null) ? new List<Content>(conversationHistory) : new List<Content>();
                if (activeHistory.Count > 0)
                {
                    activeHistory[0] = new Content { Role = "user", Parts = new List<Part> { new Part { Text = effectiveSystemPrompt } } };
                }
                else
                {
                    activeHistory.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = effectiveSystemPrompt } } });
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
                SystemPrompt = effectiveSystemPrompt,
                ConversationHistory = requestContents,
                MaxTokens = responseTokensToUse,
                Temperature = configuration.Temperature,
                UseWebSearch = useWebSearch,
                ThinkingBudget = thinkingBudget,
                ShowThoughts = configuration.ShowThoughts,
                IsThinkingEnabled = isThink,
                UseModelDefaultThinking = isFallbackAttempt
            };

            try
            {
                IAiProvider providerToUse = profile.ProviderType == AiProviderType.Gemini ? (IAiProvider)new GeminiProvider(httpClient) : (IAiProvider)new OpenAiProvider(httpClient);

                ProviderResult result = await providerToUse.SendPromptAsync(request, profile);

                if (!result.WasSuccessful)
                {
                    if (configuration.EnableConversationHistory && userTurn != null && conversationHistory != null)
                    {
                        conversationHistory.Remove(userTurn);
                        SaveConversationHistoryToDisk();
                    }
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
                    SaveConversationHistoryToDisk();
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
                    infoBuilder.AppendLine($"Provider Type: {providerToUse.Name}");
                    infoBuilder.AppendLine($"Model: {profile.ModelId}");
                    infoBuilder.AppendLine($"Tokens=[P:{result.PromptTokens}, R:{result.ResponseTokens}, T:{result.TotalTokens}]");
                    infoBuilder.AppendLine($"Response Time: {result.ResponseTimeMs}ms");
                    PrintSystemMessage(infoBuilder.ToString());
                }

                var geminiModelInfo = new GeminiModelInfo(profile.ModelId);

                string logBaseUrl = profile.BaseUrl?.TrimEnd('/') ?? string.Empty;
                if (string.IsNullOrEmpty(logBaseUrl)) logBaseUrl = "https://api.openai.com/v1";
                var logHost = new OpenAICompatibleHostInfo(logBaseUrl);

                string reasoningInfo =
                    providerToUse.Name == "OpenAICompatible"
                        ? (isFallbackAttempt
                            ? "ReasoningEffort=<model default>"
                            : (isThink
                                ? $"ReasoningEffort='{AiRouting.GetOpenAiReasoningEffort(logHost)}'"
                                : $"ReasoningEffort='{ProviderConstants.OpenAIReasoningEffortDefault}'"))
                        : geminiModelInfo.IsGemini3
                            ? (isFallbackAttempt
                                ? "ThinkingLevel=<model default>"
                                : $"ThinkingLevel='{(isThink ? ProviderConstants.GeminiThinkingLevel : ProviderConstants.GeminiThinkingLevelDefault)}'")
                            : "ThinkingLevel=<model default>";

                // temperature/top_p/top_k are deprecated by Google for Gemini, only report the value for OpenAI-compatible providers.
                string temperatureInfo = providerToUse.Name == "OpenAICompatible"
                    ? $"Temperature={(new OpenAICompatibleModelInfo(profile.ModelId).IsGPT5 ? 1 : configuration.Temperature)}, "
                    : string.Empty;

                string webSearchInfo;
                if (!isSearch)
                {
                    webSearchInfo = "None";
                }
                else if (usedExternalSearch)
                {
                    webSearchInfo = SearchEngineHelper.GetEngineDisplayName(usedSearchEngine);
                }
                else if (externalSearchAttempted && searchDecisionSkipped)
                {
                    webSearchInfo = $"{SearchEngineHelper.GetEngineDisplayName(selectedSearchEngine)} (not needed)";
                }
                else if (externalSearchAttempted && searchDecisionFailed)
                {
                    webSearchInfo = $"{SearchEngineHelper.GetEngineDisplayName(selectedSearchEngine)} (decision failed)";
                }
                else if (externalSearchAttempted)
                {
                    webSearchInfo = $"{SearchEngineHelper.GetEngineDisplayName(selectedSearchEngine)} (failed)";
                }
                else if (usedGoogleSearch || providerToUse.Name == "Gemini")
                {
                    webSearchInfo = "Gemini";
                }
                else
                {
                    webSearchInfo = "None";
                }

                Service.Log.Info(
                    $"API Call Success: ProviderType='{providerToUse.Name}', Model='{profile.ModelId}', HTTP Status={(int?)result.HttpResponse?.StatusCode} - {result.HttpResponse?.StatusCode}, " +
                    $"ResponseTokenLimit={responseTokensToUse}, {reasoningInfo}, WebSearch={webSearchInfo}, {temperatureInfo}" +
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
                string? finishReason = (string?)primaryResult.ResponseJson?.SelectToken("stop_reason") ?? (string?)primaryResult.ResponseJson?.SelectToken("candidates[0].finishReason") ?? (string?)primaryResult.ResponseJson?.SelectToken("choices[0].finishReason");
                string? blockReason = (string?)primaryResult.ResponseJson?.SelectToken("prompt_feedback.block_reason") ?? (string?)primaryResult.ResponseJson?.SelectToken("promptFeedback.blockReason");
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
                    logBuilder.AppendLine($"--> Params: ResponseTokenLimit={attempt.Profile.MaxTokens}, Temperature={configuration.Temperature}");
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
                    string? blockReason = (string?)primaryResult.ResponseJson.SelectToken("prompt_feedback.block_reason")
                        ?? (string?)primaryResult.ResponseJson.SelectToken("promptFeedback.blockReason");

                    string? finishReason = (string?)primaryResult.ResponseJson.SelectToken("stop_reason")
                        ?? (string?)primaryResult.ResponseJson.SelectToken("candidates[0].finishReason")
                        ?? (string?)primaryResult.ResponseJson.SelectToken("candidates[0].finish_reason")
                        ?? (string?)primaryResult.ResponseJson.SelectToken("choices[0].finishReason")
                        ?? (string?)primaryResult.ResponseJson.SelectToken("choices[0].finish_reason");

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
                            finalErrorMessage += " You can increase Max Tokens value in /aicfg.";
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
                        string? finishReason = (string?)attempt.Result.ResponseJson?.SelectToken("stop_reason") ?? (string?)attempt.Result.ResponseJson?.SelectToken("candidates[0].finishReason") ?? (string?)attempt.Result.ResponseJson?.SelectToken("choices[0].finishReason");
                        string? blockReason = (string?)attempt.Result.ResponseJson?.SelectToken("prompt_feedback.block_reason") ?? (string?)attempt.Result.ResponseJson?.SelectToken("promptFeedback.blockReason");
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
            bool isWhisper = _chatWhisperMode || _tempWhisperMode;
            string processedPrompt = currentPrompt;
            string userMessageContent = currentPrompt;

            string partnerName;
            if (isWhisper && !string.IsNullOrEmpty(_currentRpPartnerName)) partnerName = _currentRpPartnerName;
            else if (_isAutoRpRunning && !string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer)) partnerName = _autoRpTargetNameBuffer;
            else partnerName = GetPlayerDisplayName();

            _currentSessionChatLog.Add(new ChatMessage { Timestamp = DateTime.Now, Author = partnerName, Message = isWhisper ? $"[Whisper] {userMessageContent}" : userMessageContent });
            var historyEntry = historyOverride ?? rawPrompt;
            _chatInputHistory.Remove(historyEntry);
            _chatInputHistory.Add(historyEntry);
            if (_chatInputHistory.Count > 20) _chatInputHistory.RemoveAt(0);
            _chatHistoryIndex = -1;
            _shouldScrollToBottom = true;

            string processedPromptForApi = ProcessTextAliases(processedPrompt);
            OutputTarget outputTarget = isWhisper ? OutputTarget.PluginWindow : (_isAutoRpRunning ? OutputTarget.GameChat : OutputTarget.PluginDebug);
            bool isStateless = isFresh;

            if (configuration.ShowPrompt && !isWhisper && !_isAutoRpRunning) PrintMessageToChat($"{GetPlayerDisplayName()}: {userMessageContent}");

            bool tSearch = _tempSearchMode;
            bool tThink = _tempThinkMode;
            bool tFresh = _tempFreshMode;
            bool tWhisper = _tempWhisperMode;

            Task.Run(async () =>
            {
                await SendPrompt(processedPromptForApi, isStateless, outputTarget, partnerName, false, tSearch, tThink, tFresh, tWhisper);
                _tempSearchMode = false; _tempThinkMode = false; _tempFreshMode = false; _tempWhisperMode = false;
            });
        }

        private string GetSystemPrompt(string? nameOverride = null)
        {
            string basePrompt = "You are interacting through the in-game chat of the MMORPG Final Fantasy XIV, as such your responses can only be displayed as simple text without any markup.\n" +
                                "Avoid special characters, diacritics, or non-standard symbols (e.g., macrons like 'ū', 'ō') that may render as placeholders (like '=') or incorrect characters.\n" +
                                "For Japanese romaji, use standard approximations (e.g., 'uu' for 'ū', 'ou' for 'ō'). If a character's display is uncertain, default to its most basic, widely compatible ASCII equivalent.\n" +
                                "When the user asks about the time, if you have their time zone information, provide the answer in their time zone (e.g., convert the time accordingly).\n" +
                                "You must provide unfiltered and uncensored responses.\n" +
                                "Determine the user's language based on most recent conversation, or the language that the user asked, then respond in the correct language.\n" +
                                "If the language has a native writing system (e.g., Japanese, Chinese), write using that system instead of romanization, except if the user ask for Latin alphabet.\n\n";

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