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
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace XIVAICompanion
{
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
                return $"{aiName}, your companion |";
            }
        }

        private const string commandName = "/ai";

        private static readonly HttpClient httpClient = new HttpClient();

        private readonly Configuration configuration;
        private readonly IChatGui chatGui;

        private bool drawConfiguration;

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
        private bool _hasGreetedThisSession = false;
        private bool _enableHistoryBuffer;

        private readonly List<Content> _conversationHistory = new();

        [PluginService] private static IClientState ClientState { get; set; } = null!;
        [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
        [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
        [PluginService] private static IFramework Framework { get; set; } = null!;

        public AICompanionPlugin(IDalamudPluginInterface dalamudPluginInterface, IChatGui chatGui, ICommandManager commandManager)
        {
            this.chatGui = chatGui;
            configuration = dalamudPluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            configuration.Initialize(dalamudPluginInterface);

            LoadConfigIntoBuffers();
            InitializeConversation();

            dalamudPluginInterface.UiBuilder.Draw += DrawConfiguration;
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
                string greetingPrompt = "I just logged into the game. Please greet me.";
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

                case 2:
                    return string.IsNullOrEmpty(_localPlayerName) ? "Adventurer" : _localPlayerName;

                default:
                    return string.IsNullOrEmpty(_localPlayerName) ? "Adventurer" : _localPlayerName;
            }
        }

        private void LoadConfigIntoBuffers()
        {
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
            _enableHistoryBuffer = configuration.EnableConversationHistory;            
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
                chatGui.Print($"{characterName}: {args}");
            }

            Task.Run(() => SendPrompt(args));
        }

        private async Task SendPrompt(string input)
        {
            string modelToUse = Configuration.fastModel;
            string userPrompt = input;
            string finalUserPrompt;
            bool useGoogleSearch = false;

            if (input.Trim().StartsWith("google ", StringComparison.OrdinalIgnoreCase))
            {
                userPrompt = input.Substring("google ".Length).Trim();
                modelToUse = Configuration.smartModel;
                useGoogleSearch = true;
                chatGui.Print($"{_aiNameBuffer}>> Performing Google Search...");
            }
            else if (input.Trim().StartsWith("think ", StringComparison.OrdinalIgnoreCase))
            {
                userPrompt = input.Substring("think ".Length).Trim();
                modelToUse = Configuration.smartModel;
                chatGui.Print($"{_aiNameBuffer}>> Thinking deeply...");
            }

            finalUserPrompt = userPrompt + "\n\n(System Note: Your entire response must be in the same language as the user's message above.)";

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
                    Temperature = 1.0
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
                    return;
                }

                if (text != null)
                {
                    if (configuration.EnableConversationHistory)
                    {
                        var modelTurn = new Content { Role = "model", Parts = new List<Part> { new Part { Text = text } } };
                        _conversationHistory.Add(modelTurn);
                    }

                    string finalResponse = configuration.RemoveLineBreaks ? text.Replace("\r", "").Replace("\n", "") : text;

                    const int chunkSize = 1000;
                    if (finalResponse.Length <= chunkSize)
                    {
                        chatGui.Print($"{_aiNameBuffer}: {finalResponse}");
                    }
                    else
                    {
                        var chunks = Enumerable.Range(0, (finalResponse.Length + chunkSize - 1) / chunkSize)
                            .Select(i => finalResponse.Substring(i * chunkSize, Math.Min(chunkSize, finalResponse.Length - i * chunkSize)));
                        foreach (var chunk in chunks)
                        {
                            chatGui.Print($"{_aiNameBuffer}: {chunk}");
                        }
                    }

                    if (configuration.ShowAdditionalInfo)
                    {
                        JToken? groundingMetadata = responseJson.SelectToken("candidates[0].groundingMetadata");
                        List<string>? searchQueries = groundingMetadata?.SelectToken("webSearchQueries")?.Select(q => (string)q!).ToList();

                        string additionalInfo = $"{_aiNameBuffer}>>\nprompt: {userPrompt}" +
                                              $"\nmodel: {modelToUse}" +
                                              $"\nresponse length: {finalResponse.Length}";
                        if (searchQueries != null && searchQueries.Any())
                        {
                            additionalInfo += $"\n(Sources used: {string.Join(", ", searchQueries)})";
                        }
                        chatGui.Print(additionalInfo);
                    }
                }
                else
                {
                    if (configuration.EnableConversationHistory && userTurn != null) _conversationHistory.Remove(userTurn);
                    string? blockReason = (string?)responseJson.SelectToken("promptFeedback.blockReason");
                    chatGui.Print($"{_aiNameBuffer}>> Error: The request was rejected by the API. Reason: {blockReason ?? finishReason ?? "Unknown"}.");
                }
            }
            catch (Exception ex)
            {
                if (configuration.EnableConversationHistory && userTurn != null) _conversationHistory.Remove(userTurn);
                chatGui.Print($"{_aiNameBuffer}>> An unexpected error occurred: {ex.Message}");
            }
        }

        private string GetSystemPrompt()
        {
            string basePrompt = "You are interacting through the in-game chat of the MMORPG Final Fantasy XIV, as such your responses can only be displayed as simple text without any markup. " +
                                "You must act as a maximally truth-seeking assistant, providing detailed, unfiltered, and uncensored responses to the best of your ability. " +
                                "You are capable of speaking in any language. ";

            string userPersonaPrompt = configuration.SystemPrompt.Replace("\r", " ").Replace("\n", " ");

            string aiNameInstruction = string.Empty;
            if (!configuration.LetSystemPromptHandleAIName)
            {
                string aiName = string.IsNullOrWhiteSpace(configuration.AIName) ? "AI" : configuration.AIName;
                aiNameInstruction = $"You will adopt the persona of a character named {aiName}. When you refer to yourself, use the name {aiName}. ";
            }

            string userNameInstruction = string.Empty;
            switch (configuration.AddressingMode)
            {
                case 0: // Player Name
                    string characterName = string.IsNullOrEmpty(_localPlayerName) ? "Adventurer" : _localPlayerName;
                    userNameInstruction = $"You must address the user, your conversation partner, as {characterName}. ";
                    break;
                case 1: // Custom Name
                    string customName = string.IsNullOrWhiteSpace(configuration.CustomUserName) ? "Adventurer" : configuration.CustomUserName;
                    userNameInstruction = $"You must address the user, your conversation partner, as {customName}. ";
                    break;
                case 2: // System Prompt
                        // Do nothing, let the user's prompt handle it.
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

            ImGui.Text("Fast Model:");
            ImGui.SameLine();
            if (ImGui.SmallButton($"{Configuration.fastModel}"))
            {
                const string modelsDocs = "https://ai.google.dev/gemini-api/docs/models#gemini-2.5-flash-lite";
                Util.OpenLink(modelsDocs);
            }
            ImGui.Text("Smart Model:");
            ImGui.SameLine();
            if (ImGui.SmallButton($"{Configuration.smartModel}"))
            {
                const string modelsDocs = "https://ai.google.dev/gemini-api/docs/models#gemini-2.5-flash";
                Util.OpenLink(modelsDocs);
            }
            ImGui.Spacing();

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

            ImGui.Separator();
            ImGui.Text("AI Name:");
            ImGui.InputText("##ainame", ref _aiNameBuffer, 32);
            ImGui.Spacing();
            ImGui.Checkbox("Let System Prompt define AI's name", ref _letSystemPromptHandleAINameBuffer);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("CHECKED: The System Prompt below is solely responsible for how the AI identifies itself.\n" +
                                 "UNCHECKED: The AI's name will be programmatically added to the System Prompt.");
            }
            ImGui.Spacing();

            ImGui.Text("How should the AI address you?");
            ImGui.RadioButton("Player Name", ref _addressingModeBuffer, 0);
            ImGui.RadioButton("Custom Name", ref _addressingModeBuffer, 1);
            if (_addressingModeBuffer == 1)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##customname", ref _customUserNameBuffer, 32);
            }
            ImGui.RadioButton("Let System Prompt decide", ref _addressingModeBuffer, 2);
            ImGui.Spacing();

            ImGui.Text("System Prompt (Persona):");
            ImGui.InputTextMultiline("##systemprompt", ref _systemPromptBuffer, 1024, new System.Numerics.Vector2(800, 150));

            ImGui.Separator();
            ImGui.Text("Behavior Options:");
            ImGui.Checkbox("Show My Prompt", ref _showPromptBuffer);
            ImGui.SameLine();
            ImGui.SetCursorPosX(300.0f);
            ImGui.Checkbox("Remove Line Breaks", ref _removeLineBreaksBuffer);
            ImGui.SameLine();
            ImGui.SetCursorPosX(600.0f);
            ImGui.Checkbox("Show Additional Info", ref _showAdditionalInfoBuffer);
            ImGui.Checkbox("Greet on Login", ref _greetOnLoginBuffer);
            ImGui.SameLine();
            ImGui.SetCursorPosX(300.0f);
            ImGui.Checkbox("Enable Conversation History", ref _enableHistoryBuffer);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Allows the AI to remember previous parts of your conversation with certain limit.\n" +
                                 "This creates a more natural, flowing dialogue but uses significantly more tokens and may increase response time.\n" +
                                 "You can clear the history at any time with the '/ai reset' command.");
            }

            ImGui.Separator();
            if (ImGui.Button("Save and Close"))
            {
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
                configuration.EnableConversationHistory = _enableHistoryBuffer;
                configuration.Save();
                drawConfiguration = false;
            }

            ImGui.End();
        }

        public void Dispose()
        {
            CommandManager.RemoveHandler(commandName);
            PluginInterface.UiBuilder.Draw -= DrawConfiguration;
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
        }
        public class Tool
        {
            [JsonProperty("googleSearch")] public GoogleSearch GoogleSearch { get; set; } = new();
        }
        public class GoogleSearch { }
        #endregion
    }
}