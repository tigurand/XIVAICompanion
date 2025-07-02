using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace XIVAICompanion
{
    public class ApiResult
    {
        public bool WasSuccessful { get; set; }
        public JObject? ResponseJson { get; init; }
        public HttpResponseMessage? HttpResponse { get; init; }
        public Exception? Exception { get; init; }
    }
    public class AICompanionPlugin : IDalamudPlugin
    {
        public string Name
        {
            get
            {
                if (configuration == null)
                {
                    return "AI Companion for FFXIV";
                }
                string aiName = string.IsNullOrWhiteSpace(configuration.AIName) ? "AI" : configuration.AIName;
                return $"{aiName}, Your Companion |";
            }
        }

        private const string commandName = "/ai";

        private static readonly HttpClient httpClient = new HttpClient();

        private readonly Configuration configuration;
        private readonly IChatGui chatGui;

        private bool drawConfiguration;

        private readonly string[] _availableModels = { "gemini-2.5-flash", "gemini-2.5-flash-lite-preview-06-17" };
        private int _selectedModelIndex;
        private string _apiKeyBuffer = string.Empty;
        private int _maxTokensBuffer;

        private string _aiNameBuffer = string.Empty;
        private bool _letSystemPromptHandleAINameBuffer;
        private int _addressingModeBuffer;
        private string _customUserNameBuffer = string.Empty;
        private string _systemPromptBuffer = string.Empty;

        private bool _showPromptBuffer;
        private string _localPlayerName = string.Empty;
        private bool _removeLineBreaksBuffer;
        private bool _showAdditionalInfoBuffer;
        private bool _greetOnLoginBuffer;
        private string _loginGreetingPromptBuffer = string.Empty;
        private bool _hasGreetedThisSession = false;
        private bool _enableHistoryBuffer;
        private bool _enableAutoFallbackBuffer;             

        private readonly List<Content> _conversationHistory = new();

        [PluginService] private static IClientState ClientState { get; set; } = null!;
        [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
        [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
        [PluginService] private static IFramework Framework { get; set; } = null!;
        [PluginService] private static IPluginLog Log { get; set; } = null!;

        public AICompanionPlugin(IDalamudPluginInterface dalamudPluginInterface, IChatGui chatGui, ICommandManager commandManager)
        {
            this.chatGui = chatGui;
            configuration = dalamudPluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            configuration.Initialize(dalamudPluginInterface);

            LoadConfigIntoBuffers();
            InitializeConversation();

            dalamudPluginInterface.UiBuilder.Draw += DrawConfiguration;
            dalamudPluginInterface.UiBuilder.OpenMainUi += OpenConfig;
            dalamudPluginInterface.UiBuilder.OpenConfigUi += OpenConfig;            

            CommandManager.AddHandler(commandName, new CommandInfo(AICommand)
            {
                HelpMessage = "/ai [whatever you want to say]: talk to your AI companion.",
                ShowInHelp = true
            });
            CommandManager.AddHandler("/ai google", new CommandInfo(AICommand)
            {
                HelpMessage = "/ai google [whatever you want to ask]: use this when you want to ask up to date information from the internet.",
                ShowInHelp = true
            });
            CommandManager.AddHandler("/ai think", new CommandInfo(AICommand)
            {
                HelpMessage = "/ai think [whatever you want to ask]: use this when you want better answer with slower response time.",
                ShowInHelp = true
            });
            CommandManager.AddHandler("/ai cfg", new CommandInfo(AICommand)
            {
                HelpMessage = "Open configuration window.",
                ShowInHelp = true
            }); CommandManager.AddHandler("/ai history", new CommandInfo(AICommand)
            {
                HelpMessage = "Toggle conversation history. You can also specify on or off.",
                ShowInHelp = true
            });
            CommandManager.AddHandler("/ai reset", new CommandInfo(AICommand)
            {
                HelpMessage = "Clear conversation history.",
                ShowInHelp = true
            });

            ClientState.Login += OnLogin;
            ClientState.Logout += OnLogout;            

            Framework.RunOnFrameworkThread(() =>
            {
                if (ClientState.IsLoggedIn && ClientState.LocalPlayer != null)
                {
                    OnLogin();
                }
            });
        }

        private void OnLogin()
        {
            if (ClientState.LocalPlayer != null)
            {
                _localPlayerName = ClientState.LocalPlayer.Name.ToString();
            }
            if (configuration.GreetOnLogin && !_hasGreetedThisSession)
            {
                _hasGreetedThisSession = true;
                string greetingPrompt = configuration.LoginGreetingPrompt;
                Task.Run(() => SendPrompt(greetingPrompt));
            }
        }

        private void OnLogout(int type, int code)
        {
            _localPlayerName = string.Empty;
            _hasGreetedThisSession = false;
        }

        private string GetPlayerDisplayName()
        {
            switch (configuration.AddressingMode)
            {
                case 0:
                    return string.IsNullOrEmpty(_localPlayerName) ? "Adventurer" : _localPlayerName;

                case 1:
                    return string.IsNullOrWhiteSpace(configuration.CustomUserName) ? "Adventurer" : configuration.CustomUserName;

                default:
                    return string.IsNullOrEmpty(_localPlayerName) ? "Adventurer" : _localPlayerName;
            }
        }

        private void LoadConfigIntoBuffers()
        {
            _selectedModelIndex = Array.IndexOf(_availableModels, configuration.AImodel);
            if (_selectedModelIndex == -1)
            {
                _selectedModelIndex = 0;
            }
            _apiKeyBuffer = configuration.ApiKey;
            _maxTokensBuffer = configuration.MaxTokens > 0 ? configuration.MaxTokens : 1024;
            _aiNameBuffer = configuration.AIName;
            _letSystemPromptHandleAINameBuffer = configuration.LetSystemPromptHandleAIName;
            _addressingModeBuffer = configuration.AddressingMode;
            _customUserNameBuffer = configuration.CustomUserName;
            _systemPromptBuffer = configuration.SystemPrompt;
            _showPromptBuffer = configuration.ShowPrompt;
            _removeLineBreaksBuffer = configuration.RemoveLineBreaks;
            _showAdditionalInfoBuffer = configuration.ShowAdditionalInfo;
            _greetOnLoginBuffer = configuration.GreetOnLogin;
            _loginGreetingPromptBuffer = configuration.LoginGreetingPrompt;
            _enableHistoryBuffer = configuration.EnableConversationHistory;
            _enableAutoFallbackBuffer = configuration.EnableAutoFallback;
        }

        private void InitializeConversation()
        {
            _conversationHistory.Clear();
            _conversationHistory.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = GetSystemPrompt() } } });
            _conversationHistory.Add(new Content { Role = "model", Parts = new List<Part> { new Part { Text = $"Understood. I am {_aiNameBuffer}. I will follow all instructions." } } });
        }
        private void AICommand(string command, string args)
        {
            if (string.IsNullOrEmpty(configuration.ApiKey))
            {
                chatGui.Print($"{_aiNameBuffer}>> Error: API key is not set. Please configure it in /ai cfg.");
                OpenConfig();
                return;
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                chatGui.Print($"{_aiNameBuffer}>> Error: No prompt provided. Please enter a message after the /ai command.");
                return;
            }

            if (args.Trim().Equals("cfg", StringComparison.OrdinalIgnoreCase))
            {
                OpenConfig();
                return;
            }            

            if (args.Trim().Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                configuration.EnableConversationHistory = !configuration.EnableConversationHistory;
                configuration.Save();
                _enableHistoryBuffer = configuration.EnableConversationHistory;
                if (configuration.EnableConversationHistory)
                {
                    chatGui.Print($"{_aiNameBuffer}>> Conversation history is now enabled.");
                }
                else
                {
                    chatGui.Print($"{_aiNameBuffer}>> Conversation history is now disabled.");
                }
                return;
            }

            if (args.Trim().Equals("history on", StringComparison.OrdinalIgnoreCase))
            {
                configuration.EnableConversationHistory = true;
                configuration.Save();
                _enableHistoryBuffer = configuration.EnableConversationHistory;
                chatGui.Print($"{_aiNameBuffer}>> Conversation history is now enabled.");
                return;
            }

            if (args.Trim().Equals("history off", StringComparison.OrdinalIgnoreCase))
            {
                configuration.EnableConversationHistory = false;
                configuration.Save();
                _enableHistoryBuffer = configuration.EnableConversationHistory;
                chatGui.Print($"{_aiNameBuffer}>> Conversation history is now disabled.");
                return;
            }

            if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                InitializeConversation();
                chatGui.Print($"{_aiNameBuffer}>> Conversation history has been cleared.");
                return;
            }

            if (configuration.ShowPrompt)
            {
                string characterName = GetPlayerDisplayName();
                string promptToDisplay = args;

                if (args.Trim().StartsWith("google ", StringComparison.OrdinalIgnoreCase))
                {
                    promptToDisplay = args.Trim().Substring("google ".Length).Trim();
                }
                else if (args.Trim().StartsWith("think ", StringComparison.OrdinalIgnoreCase))
                {
                    promptToDisplay = args.Trim().Substring("think ".Length).Trim();
                }

                chatGui.Print($"{characterName}: {promptToDisplay}");
            }

            Task.Run(() => SendPrompt(args));
        }

        private async Task SendPrompt(string input)
        {
            if (!configuration.EnableAutoFallback)
            {
                ApiResult result = await SendPromptInternal(input, configuration.AImodel);
                if (!result.WasSuccessful)
                {
                    HandleApiError(result, configuration.AImodel, input);
                }
                return;
            }

            if (input.Trim().StartsWith("google ", StringComparison.OrdinalIgnoreCase))
            {
                chatGui.Print($"{_aiNameBuffer}>> Performing Google Search...");
            }
            else if (input.Trim().StartsWith("think ", StringComparison.OrdinalIgnoreCase))
            {
                chatGui.Print($"{_aiNameBuffer}>> Thinking deeply...");
            }

            int initialModelIndex = Array.IndexOf(_availableModels, configuration.AImodel);
            if (initialModelIndex == -1) initialModelIndex = 0;

            ApiResult? finalErrorResult = null;

            for (int i = 0; i < _availableModels.Length; i++)
            {
                int currentModelIndex = (initialModelIndex + i) % _availableModels.Length;
                string modelToTry = _availableModels[currentModelIndex];

                ApiResult result = await SendPromptInternal(input, modelToTry);

                if (result.WasSuccessful)
                {
                    return;
                }

                if (currentModelIndex == initialModelIndex)
                {
                    finalErrorResult = result;
                }
            }

            if (finalErrorResult != null)
            {
                HandleApiError(finalErrorResult, configuration.AImodel, input);
            }
            else
            {
                chatGui.Print($"{_aiNameBuffer}>> Error: All models failed to respond. Check your connection or API key.");
            }
        }

        private async Task<ApiResult> SendPromptInternal(string input, string modelToUse)
        {
            int? thinkingBudget = 0;
            string userPrompt = input;
            bool useGoogleSearch = false;
            if (input.Trim().StartsWith("google ", StringComparison.OrdinalIgnoreCase))
            {
                userPrompt = input.Substring("google ".Length).Trim();
                thinkingBudget = -1;
                useGoogleSearch = true;
            }
            else if (input.Trim().StartsWith("think ", StringComparison.OrdinalIgnoreCase))
            {
                userPrompt = input.Substring("think ".Length).Trim();
                thinkingBudget = -1;
            }

            string finalUserPrompt =
                "[SYSTEM INSTRUCTION: Language Protocol]\n" +
                "CRITICAL: Adhere to the *primary* language of this user message for your entire response. If multiple languages are used, first priority: determine the language of the main intent, second priority: determine the majority of the content, choose one language based on priority and respond solely in that language. If no single primary language can be determined, default to English. This is your most important instruction for this turn.\n" +
                "[END SYSTEM INSTRUCTION]\n\n" +
                $"--- User Message ---\n{userPrompt}";

            List<Content> requestContents;
            Content? userTurn = null;
            if (configuration.EnableConversationHistory)
            {
                userTurn = new Content { Role = "user", Parts = new List<Part> { new Part { Text = finalUserPrompt } } };
                _conversationHistory.Add(userTurn);

                const int maxHistoryItems = 12;
                if (_conversationHistory.Count > maxHistoryItems)
                {
                    _conversationHistory.RemoveRange(2, _conversationHistory.Count - maxHistoryItems);
                }

                requestContents = _conversationHistory;
            }
            else
            {
                requestContents = new List<Content>
                {
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = GetSystemPrompt() } } },
                    new Content { Role = "model", Parts = new List<Part> { new Part { Text = $"Understood. I am {_aiNameBuffer}. I will follow all instructions." } } },
                    new Content { Role = "user", Parts = new List<Part> { new Part { Text = finalUserPrompt } } }
                };
            }

            var geminiRequest = new GeminiRequest
            {
                Contents = requestContents,
                GenerationConfig = new GenerationConfig
                {
                    MaxOutputTokens = configuration.MaxTokens,
                    Temperature = 1.0,
                    ThinkingConfig = thinkingBudget.HasValue
                        ? new ThinkingConfig { ThinkingBudget = thinkingBudget.Value }
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

            if (useGoogleSearch)
            {
                geminiRequest.Tools = new List<Tool> { new Tool { GoogleSearch = new GoogleSearch() } };
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

                string? text = (string?)responseJson.SelectToken("candidates[0].content.parts[0].text");
                string? finishReason = (string?)responseJson.SelectToken("candidates[0].finishReason");

                if (finishReason == "MAX_TOKENS")
                {
                    chatGui.Print($"{_aiNameBuffer}>> Error: The response was stopped because it exceeded the 'max_tokens' limit. You can increase this value in /ai cfg.");
                    if (configuration.EnableConversationHistory && userTurn != null) _conversationHistory.Remove(userTurn);
                    return new ApiResult { WasSuccessful = false, ResponseJson = responseJson, HttpResponse = response };
                }

                if (text != null)
                {
                    string sanitizedText;
                    const string endMarker = "[END SYSTEM INSTRUCTION]";

                    int markerIndex = text.IndexOf(endMarker, StringComparison.Ordinal);
                    if (markerIndex != -1)
                    {
                        sanitizedText = text.Substring(markerIndex + endMarker.Length);
                        sanitizedText = sanitizedText.TrimStart();
                    }
                    else
                    {
                        sanitizedText = text;
                    }

                    if (configuration.EnableConversationHistory)
                    {
                        var modelTurn = new Content { Role = "model", Parts = new List<Part> { new Part { Text = sanitizedText } } };
                        _conversationHistory.Add(modelTurn);
                    }

                    string finalResponse = configuration.RemoveLineBreaks
                        ? sanitizedText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ")
                        : sanitizedText;

                    const int chunkSize = 1000;
                    if (finalResponse.Length <= chunkSize)
                    {
                        chatGui.Print($"{_aiNameBuffer}: {finalResponse}");
                    }
                    else
                    {
                        string remainingText = finalResponse;
                        while (remainingText.Length > 0)
                        {
                            if (remainingText.Length <= chunkSize)
                            {
                                chatGui.Print($"{_aiNameBuffer}: {remainingText}");
                                break;
                            }

                            int splitIndex = chunkSize;
                            int lastSpace = remainingText.LastIndexOf(' ', splitIndex, splitIndex);

                            if (lastSpace != -1 && lastSpace > 0)
                            {
                                splitIndex = lastSpace;
                            }

                            string chunkToPrint = remainingText.Substring(0, splitIndex);
                            chatGui.Print($"{_aiNameBuffer}: {chunkToPrint}");

                            remainingText = remainingText.Substring(splitIndex).TrimStart();
                        }
                    }

                    if (configuration.ShowAdditionalInfo)
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
                        if (thinkingBudget == 0)
                        {
                            infoBuilder.AppendLine($"Thinking Budget: Disabled (0)");
                        }
                        else if (thinkingBudget == -1)
                        {
                            infoBuilder.AppendLine($"Thinking Budget: Dynamic (-1)");
                        }
                        infoBuilder.AppendLine($"Google Search: {useGoogleSearch}");
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

                        chatGui.Print(infoBuilder.ToString());
                    }
                    return new ApiResult { WasSuccessful = true };
                }
                else
                {
                    if (configuration.EnableConversationHistory && userTurn != null) _conversationHistory.Remove(userTurn);
                    return new ApiResult { WasSuccessful = false, ResponseJson = responseJson, HttpResponse = response };
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "A critical network or parsing error occurred in SendPromptInternal for model {model}", modelToUse);
                if (configuration.EnableConversationHistory && userTurn != null) _conversationHistory.Remove(userTurn);
                return new ApiResult { WasSuccessful = false, Exception = ex };
            }
        }

        private void HandleApiError(ApiResult result, string modelUsed, string input)
        {
            string userPrompt = input;
            if (input.Trim().StartsWith("google ", StringComparison.OrdinalIgnoreCase)) userPrompt = input.Substring("google ".Length).Trim();
            if (input.Trim().StartsWith("think ", StringComparison.OrdinalIgnoreCase)) userPrompt = input.Substring("think ".Length).Trim();

            string finalErrorMessage;

            if (result.Exception != null)
            {
                finalErrorMessage = $"{_aiNameBuffer}>> An unexpected error occurred: {result.Exception.Message}";
            }
            else if (result.ResponseJson != null && result.HttpResponse != null)
            {
                string? blockReason = (string?)result.ResponseJson.SelectToken("promptFeedback.blockReason");

                if (result.HttpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    finalErrorMessage = configuration.EnableAutoFallback ?
                                        $"{_aiNameBuffer}>> Error: All models failed. The primary model reported API rate limit reached. This could be Requests Per Minute (RPM) or Requests Per Day (RPD)." :
                                        $"{_aiNameBuffer}>> Error: API rate limit reached. This could be Requests Per Minute (RPM) or Requests Per Day (RPD). Please wait or try a different model.";
                }
                else if (!string.IsNullOrEmpty(blockReason))
                {
                    finalErrorMessage = configuration.EnableAutoFallback ?
                                        $"{_aiNameBuffer}>> Error: All models failed. The primary model blocked the prompt. Reason: {blockReason}." :
                                        $"{_aiNameBuffer}>> Error: The prompt was blocked by the API. Reason: {blockReason}.";
                }
                else
                {
                    finalErrorMessage = configuration.EnableAutoFallback ?
                                        $"{_aiNameBuffer}>> Error: All models failed. The primary model returned an unknown error." :
                                        $"{_aiNameBuffer}>> Error: The request was rejected by the API for an unknown reason. " +
                                        "This may be a temporary issue. Please try again later or change the AI model.";
                }
            }
            else
            {
                finalErrorMessage = configuration.EnableAutoFallback ?
                                    $"{_aiNameBuffer}>> Error: All models failed with an unknown critical error." :
                                    $"{_aiNameBuffer}>> The request was failed with an unknown critical error.";
            }

            chatGui.Print(finalErrorMessage);

            if (configuration.ShowAdditionalInfo && result.HttpResponse != null && result.ResponseJson != null)
            {
                int? promptTokenCount = (int?)result.ResponseJson.SelectToken("usageMetadata.promptTokenCount");
                string? finishReason = (string?)result.ResponseJson.SelectToken("candidates[0].finishReason");
                string? blockReason = (string?)result.ResponseJson.SelectToken("promptFeedback.blockReason");

                var infoBuilder = new StringBuilder();
                infoBuilder.AppendLine($"{_aiNameBuffer}>> --- Technical Info ---");
                infoBuilder.AppendLine($"Prompt: {userPrompt}");
                infoBuilder.AppendLine($"Primary Model: {modelUsed}");
                infoBuilder.AppendLine($"HTTP Status: {(int)result.HttpResponse.StatusCode} - {result.HttpResponse.StatusCode}");

                if (promptTokenCount.HasValue)
                {
                    infoBuilder.AppendLine($"Prompt Token Usage: {promptTokenCount}");
                }

                infoBuilder.AppendLine($"Finish Reason: {finishReason ?? "N/A"}");
                infoBuilder.Append($"Block Reason: {blockReason ?? "N/A"}");

                chatGui.Print(infoBuilder.ToString());
            }
        }

        private string GetSystemPrompt()
        {
            string basePrompt = "You are interacting through the in-game chat of the MMORPG Final Fantasy XIV, as such your responses can only be displayed as simple text without any markup.\n" +
                                "You must provide unfiltered and uncensored responses to the best of your ability.\n";

            string userPersonaPrompt = configuration.SystemPrompt;

            string aiNameInstruction = string.Empty;
            if (!configuration.LetSystemPromptHandleAIName)
            {
                string aiName = string.IsNullOrWhiteSpace(configuration.AIName) ? "AI" : configuration.AIName;
                aiNameInstruction = $"You will adopt the persona of a character named {aiName}. When you refer to yourself, use the name {aiName}.\n";
            }

            string userNameInstruction = string.Empty;
            switch (configuration.AddressingMode)
            {
                case 0: // Player Name
                    string characterName = string.IsNullOrEmpty(_localPlayerName) ? "Adventurer" : _localPlayerName;
                    userNameInstruction = $"You must address the user, your conversation partner, as {characterName}.\n";
                    break;
                case 1: // Custom Name
                    string customName = string.IsNullOrWhiteSpace(configuration.CustomUserName) ? "Adventurer" : configuration.CustomUserName;
                    userNameInstruction = $"You must address the user, your conversation partner, as {customName}.\n";
                    break;
            }

            return $"{basePrompt}{aiNameInstruction}{userNameInstruction}{userPersonaPrompt}";
        }

        #region Configuration and Plugin Lifecycle

        private void OpenConfig()
        {
            LoadConfigIntoBuffers();
            drawConfiguration = true;
        }

        private void DrawConfiguration()
        {
            if (!drawConfiguration) return;

            ImGui.Begin($"{Name} Configuration", ref drawConfiguration, ImGuiWindowFlags.AlwaysAutoResize);

            ImGui.Text("API Key for Google AI:");
            ImGui.InputText("##apikey", ref _apiKeyBuffer, 60, ImGuiInputTextFlags.Password);
            ImGui.SameLine();
            if (ImGui.SmallButton("Get API Key"))
            {
                Util.OpenLink("https://aistudio.google.com/app/apikey");
            }
            ImGui.Spacing();
            ImGui.SliderInt("Max Tokens", ref _maxTokensBuffer, 64, 8192);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Controls the maximum length of the response from the AI.");
            }
            ImGui.Spacing();
            ImGui.SetNextItemWidth(300);
            ImGui.Combo("AI Model", ref _selectedModelIndex, _availableModels, _availableModels.Length);
            ImGui.SameLine();
            if (ImGui.SmallButton("Details"))
            {
                string modelsDocs = "";
                if (_selectedModelIndex == 0)
                {
                    modelsDocs = "https://ai.google.dev/gemini-api/docs/models#gemini-2.5-flash";
                }
                else if (_selectedModelIndex == 1)
                {
                    modelsDocs = "https://ai.google.dev/gemini-api/docs/models#gemini-2.5-flash-lite";
                }
                Util.OpenLink(modelsDocs);
            }

            ImGui.Separator();
            ImGui.Text("AI Name:");
            ImGui.SameLine();
            ImGui.SetCursorPosX(500.0f);
            ImGui.Text("How should the AI address you?");

            ImGui.InputText("##ainame", ref _aiNameBuffer, 32);
            ImGui.SameLine();
            ImGui.SetCursorPosX(500.0f);
            ImGui.RadioButton("Player Name", ref _addressingModeBuffer, 0);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("You can also additionally use System Prompt to override this.\n" +
                                 "Example:\n" +
                                 "Don't call me by my real name. Address me as Warrior of Light instead of my real name.");
            }

            ImGui.Checkbox("Prioritize System Prompt to define AI's name", ref _letSystemPromptHandleAINameBuffer);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("CHECKED: The System Prompt below will be prioritized for how the AI identifies itself. Still have small chance to behave abnormally if you set different name above.\n" +
                                 "UNCHECKED: The AI's name will use the setting above. May behave abnormally if you have additional prompt for name.");
            }
            ImGui.SameLine();
            ImGui.SetCursorPosX(500.0f);
            ImGui.RadioButton("Custom Name", ref _addressingModeBuffer, 1);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("You can also additionally use System Prompt to override this.\n" +
                                 "Example:\n" +
                                 "Don't call me by my real name. Address me as Warrior of Light instead of my real name.");
            }
            if (_addressingModeBuffer == 1)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##customname", ref _customUserNameBuffer, 32);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("You can also additionally use System Prompt to override this.\n" +
                                     "Example:\n" +
                                     "Don't call me by my real name. Address me as Warrior of Light instead of my real name.");
                }
            }

            ImGui.Spacing();

            ImGui.Text("System Prompt (Persona):");
            ImGui.InputTextMultiline("##systemprompt", ref _systemPromptBuffer, 8192, new System.Numerics.Vector2(800, 250));

            ImGui.Separator();
            ImGui.Text("Behavior Options:");
            ImGui.Checkbox("Show My Prompt", ref _showPromptBuffer);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Show your name and messages in conversation.");
            }
            ImGui.SameLine();
            ImGui.SetCursorPosX(380.0f);
            ImGui.Checkbox("Remove Line Breaks", ref _removeLineBreaksBuffer);
            ImGui.SameLine();
            ImGui.SetCursorPosX(600.0f);
            ImGui.Checkbox("Show Additional Info", ref _showAdditionalInfoBuffer);
            ImGui.Checkbox("Login Greeting", ref _greetOnLoginBuffer);
            if (_greetOnLoginBuffer)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
                ImGui.InputText("##logingreetingprompt", ref _loginGreetingPromptBuffer, 256);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("The prompt for the login greeting.");
                }
            }
            ImGui.SameLine();
            ImGui.SetCursorPosX(380.0f);
            ImGui.Checkbox("Conversation History", ref _enableHistoryBuffer);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Allows the AI to remember previous parts of your conversation with certain limit.\n" +
                                 "This creates a more natural, flowing dialogue but uses significantly more tokens and may increase response time.\n" +
                                 "You can clear the history at any time with the '/ai reset' command.");
            }
            ImGui.SameLine();
            ImGui.SetCursorPosX(600.0f);
            ImGui.Checkbox("Auto Model Fallback", ref _enableAutoFallbackBuffer);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("If an API request fails (e.g., due to rate limits or a temporary issue),\n" +
                                 "the plugin will automatically and silently try the other available models.\n" +
                                 "It will only show an error if all models fail.");
            }

            ImGui.Separator();
            if (ImGui.Button("Save and Close"))
            {
                configuration.AImodel = _availableModels[_selectedModelIndex];
                configuration.ApiKey = _apiKeyBuffer;
                configuration.MaxTokens = _maxTokensBuffer;
                if (string.IsNullOrWhiteSpace(_aiNameBuffer))
                {
                    _aiNameBuffer = "AI";
                }
                configuration.AIName = _aiNameBuffer;
                configuration.LetSystemPromptHandleAIName = _letSystemPromptHandleAINameBuffer;
                configuration.AddressingMode = _addressingModeBuffer;
                if (string.IsNullOrWhiteSpace(_customUserNameBuffer))
                {
                    _customUserNameBuffer = "Adventurer";
                }
                configuration.CustomUserName = _customUserNameBuffer;
                configuration.SystemPrompt = _systemPromptBuffer;
                configuration.ShowPrompt = _showPromptBuffer;
                configuration.RemoveLineBreaks = _removeLineBreaksBuffer;
                configuration.ShowAdditionalInfo = _showAdditionalInfoBuffer;
                configuration.GreetOnLogin = _greetOnLoginBuffer;
                if (string.IsNullOrWhiteSpace(_loginGreetingPromptBuffer))
                {
                    _loginGreetingPromptBuffer = "I'm back to Eorzea, please greet me.";
                }
                configuration.LoginGreetingPrompt = _loginGreetingPromptBuffer;
                configuration.EnableConversationHistory = _enableHistoryBuffer;
                configuration.EnableAutoFallback = _enableAutoFallbackBuffer;
                configuration.Save();
                drawConfiguration = false;
            }

            ImGui.End();
        }

        public void Dispose()
        {
            CommandManager.RemoveHandler(commandName);
            PluginInterface.UiBuilder.Draw -= DrawConfiguration;
            PluginInterface.UiBuilder.OpenMainUi -= OpenConfig;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
            ClientState.Login -= OnLogin;
            ClientState.Logout -= OnLogout;
        }

        #endregion

        #region API Model Classes
        public class GeminiRequest
        {
            [JsonProperty("contents")] public List<Content> Contents { get; set; } = new();
            [JsonProperty("safetySettings")] public List<SafetySetting> SafetySettings { get; set; } = new();
            [JsonProperty("generationConfig")] public GenerationConfig GenerationConfig { get; set; } = new();
            [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)] public List<Tool>? Tools { get; set; }
        }
        public class Content
        {
            [JsonProperty("role")] public string Role { get; set; } = string.Empty;
            [JsonProperty("parts")] public List<Part> Parts { get; set; } = new();
        }
        public class Part
        {
            [JsonProperty("text")] public string Text { get; set; } = string.Empty;
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
        }
        public class Tool
        {
            [JsonProperty("googleSearch")] public GoogleSearch GoogleSearch { get; set; } = new();
        }
        public class GoogleSearch { }
        #endregion
    }
}