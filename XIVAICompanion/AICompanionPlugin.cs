using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace XIVAICompanion
{
    public class ChatMessage
    {
        public DateTime Timestamp { get; set; }
        public string Author { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
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
        private const int minimumThinkingBudget = 512;

        private static readonly HttpClient httpClient = new HttpClient();

        private readonly Configuration configuration;
        private readonly IChatGui chatGui;

        private bool drawConfiguration;
                
        private string _apiKeyBuffer = string.Empty;
        private int _maxTokensBuffer;
        private readonly string[] _availableModels = { "gemini-2.5-flash", "gemini-2.5-flash-lite-preview-06-17" };
        private int _selectedModelIndex = -1;
        private const int greetingModelIndex = 1; // 1 = gemini-2.5-flash-lite-preview-06-17

        private string _aiNameBuffer = string.Empty;
        private bool _letSystemPromptHandleAINameBuffer;
        private int _addressingModeBuffer;
        private string _localPlayerName = string.Empty;
        private string _customUserNameBuffer = string.Empty;
        private string _systemPromptBuffer = string.Empty;
        private readonly DirectoryInfo _personaFolder;
        private List<string> _personaFiles = new();
        private int _selectedPersonaIndex = -1;
        private string _saveAsNameBuffer = string.Empty;
        private bool _showOverwriteConfirmation = false;
        private bool _showInvalidNameConfirmation = false;

        private bool _greetOnLoginBuffer;
        private string _loginGreetingPromptBuffer = string.Empty;
        private bool _hasGreetedThisSession = false;
        private bool _enableHistoryBuffer;
        private readonly List<Content> _conversationHistory = new();
        private bool _enableAutoFallbackBuffer;

        private bool _showPromptBuffer;        
        private bool _removeLineBreaksBuffer;
        private bool _showAdditionalInfoBuffer;        
        private bool _useCustomColorsBuffer;
        private Vector4 _foregroundColorBuffer;

        private bool _drawChatWindow;
        private string _chatInputBuffer = string.Empty;
        private readonly List<ChatMessage> _historicalChatLog = new();
        private readonly List<ChatMessage> _currentSessionChatLog = new();
        private readonly DirectoryInfo _chatLogsFolder;

        private bool _saveChatToFileBuffer;
        private int _sessionsToLoadBuffer;
        private int _daysToKeepLogsBuffer;
        private string _chatFilterText = string.Empty;

        private bool _shouldScrollToBottom;
        private readonly List<string> _chatInputHistory = new();
        private int _chatHistoryIndex = -1;
        private bool _refocusChatInput;

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

            _personaFolder = new DirectoryInfo(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "Personas"));
            _chatLogsFolder = new DirectoryInfo(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "ChatLogs"));
            LoadAvailablePersonas();
            LoadHistoricalLogs(configuration.AIName);
            DeleteOldLogs();

            dalamudPluginInterface.UiBuilder.Draw += DrawConfiguration;
            dalamudPluginInterface.UiBuilder.Draw += DrawChatWindow;
            dalamudPluginInterface.UiBuilder.OpenMainUi += OpenChatWindow;
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
            CommandManager.AddHandler("/ai fresh", new CommandInfo(AICommand)
            {
                HelpMessage = "/ai fresh [whatever you want to say]: Temporarily disable conversation history for this turn (1 time only).",
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
            CommandManager.AddHandler("/ai chat", new CommandInfo(AICommand)
            {
                HelpMessage = "Open the dedicated AI chat window.",
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

        private string GetSanitizedAiName(string aiName)
        {
            string baseName = string.IsNullOrWhiteSpace(aiName) ? "AI" : aiName;

            string sanitizedName = baseName.Replace(' ', '_');

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                sanitizedName = sanitizedName.Replace(c.ToString(), string.Empty);
            }
            return sanitizedName;
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
                Log.Info("Sending login greeting. Prompt: {Prompt}", greetingPrompt);
                Task.Run(() => SendPrompt(greetingPrompt, true));
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
            _useCustomColorsBuffer = configuration.UseCustomColors;
            _foregroundColorBuffer = configuration.ForegroundColor;

            _saveChatToFileBuffer = configuration.SaveChatHistoryToFile;
            _sessionsToLoadBuffer = configuration.SessionsToLoad;
            _daysToKeepLogsBuffer = configuration.DaysToKeepLogs;
        }

        private void LoadAvailablePersonas()
        {
            if (!_personaFolder.Exists)
            {
                _personaFolder.Create();
            }
            var realFiles = _personaFolder.GetFiles("*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                .ToList();

            _personaFiles = new List<string> { "<New Profile>" };
            _personaFiles.AddRange(realFiles);
        }

        private void LoadPersona(string profileName)
        {
            if (profileName == "<New Profile>")
            {
                var defaultPersona = new Persona();
                _aiNameBuffer = defaultPersona.AIName;
                _letSystemPromptHandleAINameBuffer = defaultPersona.LetSystemPromptHandleAIName;
                _addressingModeBuffer = defaultPersona.AddressingMode;
                _customUserNameBuffer = defaultPersona.CustomUserName;
                _systemPromptBuffer = defaultPersona.SystemPrompt;

                _saveAsNameBuffer = "AI";

                chatGui.Print($"{_aiNameBuffer}>> New profile template loaded. Configure and save it.");
                return;
            }

            var filePath = Path.Combine(_personaFolder.FullName, profileName + ".json");
            if (!File.Exists(filePath)) return;

            try
            {
                var json = File.ReadAllText(filePath);
                var persona = JsonConvert.DeserializeObject<Persona>(json);

                if (persona != null)
                {
                    _aiNameBuffer = persona.AIName;
                    _letSystemPromptHandleAINameBuffer = persona.LetSystemPromptHandleAIName;
                    _addressingModeBuffer = persona.AddressingMode;
                    _customUserNameBuffer = persona.CustomUserName;
                    _systemPromptBuffer = persona.SystemPrompt;

                    chatGui.Print($"{_aiNameBuffer}>> Profile '{profileName}' loaded into config window. Press 'Save and Close' to apply.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to load persona file: {profileName}");
            }
        }

        private void SavePersona(string fileName)
        {
            var persona = new Persona
            {
                AIName = string.IsNullOrWhiteSpace(_aiNameBuffer) ? "AI" : _aiNameBuffer,
                LetSystemPromptHandleAIName = _letSystemPromptHandleAINameBuffer,
                AddressingMode = _addressingModeBuffer,
                CustomUserName = string.IsNullOrWhiteSpace(_customUserNameBuffer) ? "Adventurer" : _customUserNameBuffer,
                SystemPrompt = _systemPromptBuffer
            };

            try
            {
                _personaFolder.Create();

                string filePath = Path.Combine(_personaFolder.FullName, fileName + ".json");

                var json = JsonConvert.SerializeObject(persona, Formatting.Indented);

                File.WriteAllText(filePath, json);

                LoadAvailablePersonas();
                _selectedPersonaIndex = _personaFiles.FindIndex(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase));

                chatGui.Print($"{_aiNameBuffer}>> Profile '{fileName}' saved successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to save persona file: {fileName}");
                chatGui.Print($"{_aiNameBuffer}>> Error: Failed to save profile '{fileName}'. See /xllog for details.");
            }
        }

        private string ProcessTextAliases(string rawInput)
        {
            string processedInput = rawInput;

            var aliases = new Dictionary<string, string>
            {
                // Lodestone Aliases
                { "nalodestone", "https://na.finalfantasyxiv.com/lodestone/" },
                { "eulodestone", "https://eu.finalfantasyxiv.com/lodestone/" },
                { "frlodestone", "https://fr.finalfantasyxiv.com/lodestone/" },
                { "delodestone", "https://de.finalfantasyxiv.com/lodestone/" },
                { "jplodestone", "https://jp.finalfantasyxiv.com/lodestone/" },
                { "ffmaint", "https://na.finalfantasyxiv.com/lodestone/news/category/2" }
            };

            foreach (var alias in aliases)
            {
                string pattern = $@"\b{Regex.Escape(alias.Key)}\b";
                processedInput = Regex.Replace(processedInput, pattern, alias.Value, RegexOptions.IgnoreCase);
            }

            return processedInput;
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
                PrintMessageToChat($"{_aiNameBuffer}>> Error: API key is not set. Please configure it in /ai cfg.");
                Log.Warning("Plugin configuration issue: API key is not set. User was prompted to open config.");
                OpenConfig();
                return;
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                PrintMessageToChat($"{_aiNameBuffer}>> Error: No prompt provided. Please enter a message after the /ai command.");
                return;
            }

            string processedArgs = ProcessTextAliases(args);

            if (processedArgs.Trim().Equals("cfg", StringComparison.OrdinalIgnoreCase))
            {
                OpenConfig();
                return;
            }

            if (processedArgs.Trim().Equals("chat", StringComparison.OrdinalIgnoreCase))
            {
                _drawChatWindow = true;
                return;
            }

            if (processedArgs.Trim().Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                configuration.EnableConversationHistory = !configuration.EnableConversationHistory;
                configuration.Save();
                _enableHistoryBuffer = configuration.EnableConversationHistory;
                if (configuration.EnableConversationHistory)
                {
                    PrintMessageToChat($"{_aiNameBuffer}>> Conversation history is now enabled.");
                }
                else
                {
                    PrintMessageToChat($"{_aiNameBuffer}>> Conversation history is now disabled.");
                }
                return;
            }

            if (processedArgs.Trim().Equals("history on", StringComparison.OrdinalIgnoreCase))
            {
                configuration.EnableConversationHistory = true;
                configuration.Save();
                _enableHistoryBuffer = configuration.EnableConversationHistory;
                PrintMessageToChat($"{_aiNameBuffer}>> Conversation history is now enabled.");
                return;
            }

            if (processedArgs.Trim().Equals("history off", StringComparison.OrdinalIgnoreCase))
            {
                configuration.EnableConversationHistory = false;
                configuration.Save();
                _enableHistoryBuffer = configuration.EnableConversationHistory;
                PrintMessageToChat($"{_aiNameBuffer}>> Conversation history is now disabled.");
                return;
            }

            if (processedArgs.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                InitializeConversation();
                PrintMessageToChat($"{_aiNameBuffer}>> Conversation history has been cleared.");
                return;
            }

            ProcessAndSendPrompt(processedArgs);
        }

        private void ProcessAndSendPrompt(string rawPrompt)
        {
            string cleanPrompt = GetCleanPromptText(rawPrompt);

            _currentSessionChatLog.Add(new ChatMessage
            {
                Timestamp = DateTime.Now,
                Author = GetPlayerDisplayName(),
                Message = cleanPrompt
            });

            _chatInputHistory.Add(rawPrompt);
            if (_chatInputHistory.Count > 20)
            {
                _chatInputHistory.RemoveAt(0);
            }
            _chatHistoryIndex = -1;

            _shouldScrollToBottom = true;

            string processedArgs = ProcessTextAliases(rawPrompt);

            if (configuration.ShowPrompt)
            {
                string characterName = GetPlayerDisplayName();

                string promptToDisplay = GetCleanPromptText(processedArgs);

                PrintMessageToChat($"{characterName}: {promptToDisplay}");
            }

            Task.Run(() => SendPrompt(processedArgs));
        }

        private unsafe void DrawChatWindow()
        {
            if (!_drawChatWindow) return;

            ImGui.SetNextWindowSizeConstraints(new Vector2(350, 250), new Vector2(9999, 9999));
            ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin($"{Name} Chat", ref _drawChatWindow))
            {
                if (UIHelper.AddHeaderIcon(PluginInterface, "config_button", FontAwesomeIcon.Cog, out var openConfigPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Configuration" }) && openConfigPressed)
                {
                    OpenConfig();
                }

                if (ImGui.CollapsingHeader("Settings", ImGuiTreeNodeFlags.None))
                {
                    if (ImGui.IsItemToggledOpen())
                    {
                        _saveChatToFileBuffer = configuration.SaveChatHistoryToFile;
                        _sessionsToLoadBuffer = configuration.SessionsToLoad;
                        _daysToKeepLogsBuffer = configuration.DaysToKeepLogs;
                    }

                    if (ImGui.Checkbox("Save Chat to File", ref _saveChatToFileBuffer))
                    {
                        configuration.SaveChatHistoryToFile = _saveChatToFileBuffer;
                        configuration.Save();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            "Saves the chat log to a text file for viewing later.\n" +
                            "This is separate from the AI's short-term memory (in the main config)\n" +
                            "and does not affect the conversation context.");
                    }

                    if (!_saveChatToFileBuffer)
                    {
                        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f);
                        ImGui.BeginDisabled();
                    }

                    ImGui.SetNextItemWidth(100);
                    if (ImGui.InputInt("Load Previous Sessions", ref _sessionsToLoadBuffer, 1, 5))
                    {
                        if (_sessionsToLoadBuffer < 0) _sessionsToLoadBuffer = 0;
                        configuration.SessionsToLoad = _sessionsToLoadBuffer;
                        configuration.Save();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Reload"))
                    {
                        LoadHistoricalLogs(configuration.AIName);
                    }

                    ImGui.Text("Auto-delete logs older than (days):");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.InputInt("##DaysToKeep", ref _daysToKeepLogsBuffer))
                    {
                        if (_daysToKeepLogsBuffer < 0) _daysToKeepLogsBuffer = 0;
                        configuration.DaysToKeepLogs = _daysToKeepLogsBuffer;
                        configuration.Save();
                    }
                    
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Set to 0 to disable auto-deletion.");
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Open History Folder"))
                    {
                        _chatLogsFolder.Create();
                        Util.OpenLink(_chatLogsFolder.FullName);
                    }

                    if (!_saveChatToFileBuffer)
                    {
                        ImGui.EndDisabled();
                        ImGui.PopStyleVar();
                    }

                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##chat_filter", "Filter chat log...", ref _chatFilterText, 256);
                    ImGui.Spacing();
                }

                var chatRegionHeight = ImGui.GetContentRegionAvail().Y - (ImGui.GetTextLineHeightWithSpacing() * 2.5f);
                ImGui.BeginChild("ChatLog", new Vector2(0, chatRegionHeight), true, ImGuiWindowFlags.HorizontalScrollbar);

                var fullLog = _historicalChatLog.Concat(_currentSessionChatLog).ToList();

                var displayedLog = fullLog;
                if (!string.IsNullOrWhiteSpace(_chatFilterText))
                {
                    displayedLog = fullLog.Where(msg =>
                        msg.Author.Contains(_chatFilterText, StringComparison.OrdinalIgnoreCase) ||
                        msg.Message.Contains(_chatFilterText, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                DateTime? lastMessageDate = null;

                for (int i = 0; i < displayedLog.Count; i++)
                {
                    var chatMessage = displayedLog[i];

                    if (lastMessageDate == null || chatMessage.Timestamp.Date > lastMessageDate.Value.Date)
                    {
                        if (lastMessageDate != null) ImGui.Separator();
                        ImGui.TextDisabled($"--- {chatMessage.Timestamp:dddd, MMMM d, yyyy} ---");
                        ImGui.Separator();
                    }
                    lastMessageDate = chatMessage.Timestamp;

                    ImGui.Spacing();

                    var startPos = ImGui.GetCursorPos();

                    var playerNameColor = new Vector4(0.8f, 0.8f, 1.0f, 1.0f); // Light Blue
                    var aiNameColor = new Vector4(0.7f, 1.0f, 0.7f, 1.0f);     // Light Green

                    bool isAI = chatMessage.Author == configuration.AIName;
                    var authorColor = isAI ? aiNameColor : playerNameColor;

                    ImGui.TextColored(authorColor, $"{chatMessage.Author}:");

                    ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                    foreach (var chunk in SplitIntoChunks(chatMessage.Message, 2000))
                    {
                        string[] paragraphs = chunk.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        foreach (string paragraph in paragraphs)
                        {
                            ImGui.TextWrapped(paragraph);
                        }
                    }
                    ImGui.PopTextWrapPos();

                    var endPos = ImGui.GetCursorPos();
                    var itemRectSize = new Vector2(ImGui.GetContentRegionAvail().X, endPos.Y - startPos.Y);

                    ImGui.SetCursorPos(startPos);
                    ImGui.InvisibleButton($"##message_button_{i}", itemRectSize);

                    string popupId = $"popup_{i}";
                    if (ImGui.BeginPopupContextItem(popupId))
                    {
                        string fullLine = $"{chatMessage.Author}: {chatMessage.Message}";
                        if (ImGui.Selectable("Copy Message Text"))
                        {
                            ImGui.SetClipboardText(chatMessage.Message);
                        }
                        if (ImGui.Selectable("Copy Full Line"))
                        {
                            ImGui.SetClipboardText(fullLine);
                        }
                        ImGui.EndPopup();
                    }
                }

                if (_shouldScrollToBottom)
                {
                    ImGui.SetScrollHereY(1.0f);
                    _shouldScrollToBottom = false;
                }

                ImGui.EndChild();
                ImGui.Separator();

                bool messageSent = false;

                if (_refocusChatInput || ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere();
                    _refocusChatInput = false;
                }

                float sendButtonWidth = ImGui.CalcTextSize("Send").X + ImGui.GetStyle().FramePadding.X * 2.0f;
                float inputWidth = ImGui.GetContentRegionAvail().X - sendButtonWidth - ImGui.GetStyle().ItemSpacing.X;

                ImGui.SetNextItemWidth(inputWidth);

                var inputTextFlags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackHistory;

                if (ImGui.InputTextWithHint("##ChatInput", "Type a message, or 'google ...', 'think ...', 'fresh ...'", ref _chatInputBuffer, 2048, inputTextFlags, (data) =>
                {
                    var callbackData = new ImGuiInputTextCallbackDataPtr(data);

                    if (callbackData.EventFlag == ImGuiInputTextFlags.CallbackHistory)
                    {
                        int prevHistoryIndex = _chatHistoryIndex;
                        if (callbackData.EventKey == ImGuiKey.UpArrow)
                        {
                            if (_chatHistoryIndex == -1)
                                _chatHistoryIndex = _chatInputHistory.Count - 1;
                            else if (_chatHistoryIndex > 0)
                                _chatHistoryIndex--;
                        }
                        else if (callbackData.EventKey == ImGuiKey.DownArrow)
                        {
                            if (_chatHistoryIndex != -1)
                            {
                                if (_chatHistoryIndex < _chatInputHistory.Count - 1)
                                    _chatHistoryIndex++;
                                else
                                    _chatHistoryIndex = -1;
                            }
                        }

                        if (prevHistoryIndex != _chatHistoryIndex)
                        {
                            callbackData.DeleteChars(0, callbackData.BufTextLen);
                            if (_chatHistoryIndex >= 0)
                            {
                                callbackData.InsertChars(0, _chatInputHistory[_chatHistoryIndex]);
                            }
                        }
                    }
                    return 0;
                }))
                {
                    messageSent = true;
                }

                ImGui.SameLine();
                if (ImGui.Button("Send"))
                {
                    messageSent = true;
                }

                if (messageSent && !string.IsNullOrWhiteSpace(_chatInputBuffer))
                {
                    ProcessAndSendPrompt(_chatInputBuffer);
                    _chatInputBuffer = string.Empty;
                    _refocusChatInput = true;
                }
            }
            ImGui.End();
        }

        private void LoadHistoricalLogs(string aiName)
        {
            _historicalChatLog.Clear();

            if (!configuration.SaveChatHistoryToFile || configuration.SessionsToLoad <= 0)
            {
                return;
            }

            if (!_chatLogsFolder.Exists)
            {
                return;
            }

            try
            {
                string sanitizedName = GetSanitizedAiName(aiName);
                string searchPattern = $"{sanitizedName}_*.txt";

                var logFiles = _chatLogsFolder.GetFiles(searchPattern)
                                              .OrderByDescending(f => f.Name)
                                              .Take(configuration.SessionsToLoad)
                                              .Reverse()
                                              .ToList();

                Log.Info($"Found {logFiles.Count} historical chat logs for '{aiName}' to load.");

                foreach (var file in logFiles)
                {
                    var lines = File.ReadAllLines(file.FullName);
                    foreach (var line in lines)
                    {
                        var match = Regex.Match(line, @"\[(.*?)\] (.*?): (.*)");
                        if (match.Success)
                        {
                            if (DateTime.TryParse(match.Groups[1].Value, out var timestamp))
                            {
                                _historicalChatLog.Add(new ChatMessage
                                {
                                    Timestamp = timestamp,
                                    Author = match.Groups[2].Value,
                                    Message = match.Groups[3].Value
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load historical chat logs.");
            }
        }

        private void DeleteOldLogs()
        {
            if (!configuration.SaveChatHistoryToFile || configuration.DaysToKeepLogs <= 0)
            {
                return;
            }

            if (!_chatLogsFolder.Exists)
            {
                return;
            }

            try
            {
                var filesToDelete = _chatLogsFolder.GetFiles("*.txt")
                                                   .Where(f => f.CreationTimeUtc < DateTime.UtcNow.AddDays(-configuration.DaysToKeepLogs));

                int deleteCount = 0;
                foreach (var file in filesToDelete)
                {
                    file.Delete();
                    deleteCount++;
                }

                if (deleteCount > 0)
                {
                    Log.Info($"Automatically deleted {deleteCount} chat log(s) older than {configuration.DaysToKeepLogs} days.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed during automatic deletion of old chat logs.");
            }
        }

        private void PrintMessageToChat(string message)
        {
            if (!configuration.UseCustomColors || configuration.ForegroundColor.W < 0.05f)
            {
                chatGui.Print(message);
                return;
            }

            var seStringBuilder = new SeStringBuilder();
            var foreground = configuration.ForegroundColor;

            seStringBuilder.Add(new ColorPayload(new Vector3(foreground.X, foreground.Y, foreground.Z)));
            seStringBuilder.AddText(message);
            seStringBuilder.Add(new ColorEndPayload());

            chatGui.Print(seStringBuilder.Build());
        }

        private async Task SendPrompt(string input, bool isGreeting = false)
        {
            bool isStateless = input.Trim().StartsWith("fresh ", StringComparison.OrdinalIgnoreCase);

            var failedAttempts = new List<(string Model, ApiResult Result)>();

            if (!configuration.EnableAutoFallback && !isGreeting)
            {
                string modelToUse = configuration.AImodel;
                ApiResult result = await SendPromptInternal(input, modelToUse, isStateless);
                if (!result.WasSuccessful)
                {
                    failedAttempts.Add((modelToUse, result));
                    HandleApiError(failedAttempts, input);
                }
                return;
            }

            string commandCheckString = input.Trim();

            if (commandCheckString.StartsWith("fresh ", StringComparison.OrdinalIgnoreCase))
            {
                commandCheckString = commandCheckString.Substring("fresh ".Length).Trim();
            }

            if (commandCheckString.StartsWith("google ", StringComparison.OrdinalIgnoreCase))
            {
                PrintMessageToChat($"{_aiNameBuffer}>> Performing Google Search...");
            }
            else if (commandCheckString.StartsWith("think ", StringComparison.OrdinalIgnoreCase))
            {
                PrintMessageToChat($"{_aiNameBuffer}>> Thinking deeply...");
            }

            int initialModelIndex;
            if (isGreeting)
            {
                initialModelIndex = greetingModelIndex;
            }
            else
            {
                initialModelIndex = Array.IndexOf(_availableModels, configuration.AImodel);
            }

            if (initialModelIndex == -1) initialModelIndex = 0;

            for (int i = 0; i < _availableModels.Length; i++)
            {
                int currentModelIndex = (initialModelIndex + i) % _availableModels.Length;
                string modelToTry = _availableModels[currentModelIndex];

                ApiResult result = await SendPromptInternal(input, modelToTry, isStateless);

                if (result.WasSuccessful)
                {
                    return;
                }

                failedAttempts.Add((modelToTry, result));
            }

            if (failedAttempts.Count > 0)
            {
                HandleApiError(failedAttempts, input);
            }
            else
            {
                PrintMessageToChat($"{_aiNameBuffer}>> Error: All models failed to respond. Check your connection or API key.");
            }
        }

        private void SaveCurrentSessionLog(string? overrideAiName = null)
        {
            if (!configuration.SaveChatHistoryToFile || _currentSessionChatLog.Count == 0)
            {
                return;
            }

            try
            {
                _chatLogsFolder.Create();

                string aiNameToUse = overrideAiName ?? configuration.AIName;
                string sanitizedName = GetSanitizedAiName(aiNameToUse);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string fileName = $"{sanitizedName}_{timestamp}.txt";
                string filePath = Path.Combine(_chatLogsFolder.FullName, fileName);

                var logContent = new StringBuilder();
                foreach (var message in _currentSessionChatLog)
                {
                    logContent.AppendLine($"[{message.Timestamp:yyyy-MM-dd HH:mm:ss}] {message.Author}: {message.Message}");
                }

                File.WriteAllText(filePath, logContent.ToString());
                Log.Info($"Chat session with '{aiNameToUse}' saved to {fileName}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save chat session log.");
            }
        }

        private IEnumerable<string> SplitIntoChunks(string text, int chunkSize)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= chunkSize)
            {
                yield return text;
                yield break;
            }

            int offset = 0;
            while (offset < text.Length)
            {
                int remaining = text.Length - offset;
                int size = Math.Min(chunkSize, remaining);

                if (offset + size < text.Length)
                {
                    int lastSpace = text.LastIndexOf(' ', offset + size, size);
                    if (lastSpace != -1 && lastSpace > offset)
                    {
                        size = lastSpace - offset;
                    }
                }

                yield return text.Substring(offset, size).TrimStart();
                offset += size;
            }
        }

        private async Task<ApiResult> SendPromptInternal(string input, string modelToUse, bool isStateless)
        {
            int? thinkingBudget = minimumThinkingBudget;
            string userPrompt;
            bool useGoogleSearch = false;
            string currentPrompt = input.Trim();

            if (isStateless)
            {
                if (currentPrompt.StartsWith("fresh ", StringComparison.OrdinalIgnoreCase))
                {
                    currentPrompt = currentPrompt.Substring("fresh ".Length).Trim();
                }
            }

            if (currentPrompt.StartsWith("google ", StringComparison.OrdinalIgnoreCase))
            {
                userPrompt = currentPrompt.Substring("google ".Length).Trim();
                thinkingBudget = configuration.MaxTokens;
                useGoogleSearch = true;
            }
            else if (currentPrompt.StartsWith("think ", StringComparison.OrdinalIgnoreCase))
            {
                userPrompt = currentPrompt.Substring("think ".Length).Trim();
                thinkingBudget = configuration.MaxTokens;
            }
            else
            {
                userPrompt = currentPrompt;
            }

            string finalUserPrompt =
                "[SYSTEM INSTRUCTION: Language Protocol]\n" +
                "CRITICAL:\n" +
                "* Respond entirely in the primary language of the user's message, determined as follows: (1) Identify the language of the main intent, defined strictly as the language of the interrogative phrase or question phrase (e.g., what, when), explicitly ignoring the language of the subjects or objects of inquiry (nouns). (2) If the interrogative phrase's language is ambiguous, use the language constituting the majority of the message’s content, excluding the subjects or objects of inquiry. (3) If no primary language can be determined, default to English.\n" +
                "* Always re-evaluate the language based solely on the current message, ignoring previous conversation languages and any contextual bias from sensitive or prominent terms. This is the highest-priority instruction for this turn.\n" +
                "[END SYSTEM INSTRUCTION]\n\n" +
                $"--- User Message ---\n{userPrompt}\n\n[SYSTEM COMMAND: ";

            if (useGoogleSearch)
            {
                finalUserPrompt += "Do not ask for confirmation. Do not acknowledge the request. Your sole function is to execute the search tool based on the user's message and then immediately provide a comprehensive, synthesized answer using the search results.";
            }
            finalUserPrompt += " Answer in correct language based on Language Protocol.]";

            List<Content> requestContents;
            Content? userTurn = null;
            if (configuration.EnableConversationHistory && !isStateless)
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

                string? text = (string?)responseJson.SelectToken("candidates[0].content.parts[0].text");
                string? finishReason = (string?)responseJson.SelectToken("candidates[0].finishReason");

                if (finishReason == "MAX_TOKENS")
                {                    
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

                    const string userMessageMarker = "--- User Message ---";
                    markerIndex = sanitizedText.IndexOf(userMessageMarker, StringComparison.Ordinal);

                    if (markerIndex != -1)
                    {
                        int userPromptEndIndex = sanitizedText.IndexOf(userPrompt, markerIndex, StringComparison.Ordinal);

                        if (userPromptEndIndex != -1)
                        {
                            sanitizedText = sanitizedText.Substring(userPromptEndIndex + userPrompt.Length);
                            sanitizedText = sanitizedText.TrimStart();
                        }
                    }

                    if (configuration.EnableConversationHistory && !isStateless)
                    {
                        var modelTurn = new Content { Role = "model", Parts = new List<Part> { new Part { Text = sanitizedText } } };
                        _conversationHistory.Add(modelTurn);
                    }

                    string finalResponse = configuration.RemoveLineBreaks
                        ? sanitizedText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ")
                        : sanitizedText;

                    _currentSessionChatLog.Add(new ChatMessage
                    {
                        Timestamp = DateTime.Now,
                        Author = configuration.AIName,
                        Message = finalResponse
                    });

                    _shouldScrollToBottom = true;

                    foreach (var chunk in SplitIntoChunks(finalResponse, 1000))
                    {
                        PrintMessageToChat($"{_aiNameBuffer}: {chunk}");
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
                        if (thinkingBudget == configuration.MaxTokens)
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

                        PrintMessageToChat(infoBuilder.ToString());
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

        private string GetCleanPromptText(string rawText)
        {
            string cleanText = rawText.Trim();

            if (cleanText.StartsWith("fresh ", StringComparison.OrdinalIgnoreCase))
            {
                cleanText = cleanText.Substring("fresh ".Length).Trim();
            }

            if (cleanText.StartsWith("google ", StringComparison.OrdinalIgnoreCase))
            {
                cleanText = cleanText.Substring("google ".Length).Trim();
            }
            else if (cleanText.StartsWith("think ", StringComparison.OrdinalIgnoreCase))
            {
                cleanText = cleanText.Substring("think ".Length).Trim();
            }

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

            if (configuration.EnableAutoFallback && failedAttempts.Count > 1)
            {
                string primaryReason = "an unknown error";

                if (primaryResult.HttpResponse?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    primaryReason = "API rate limit reached (RPM or RPD)";
                    Log.Warning("API Failure (Fallback Path): Primary model hit rate limit. Model: {Model}, Prompt: {Prompt}", primaryModelUsed, userPrompt);
                }
                else if (primaryResult.ResponseJson?.SelectToken("promptFeedback.blockReason") is JToken blockReasonToken)
                {
                    primaryReason = $"the prompt was blocked (Reason: {blockReasonToken})";
                    Log.Warning("API Failure (Fallback Path): Primary model blocked prompt. Reason: {Reason}. Model: {Model}, Prompt: {Prompt}", blockReasonToken, primaryModelUsed, userPrompt);
                }
                else if ((string?)primaryResult.ResponseJson?.SelectToken("candidates[0].finishReason") == "MAX_TOKENS")
                {
                    primaryReason = "the response exceeded the 'max_tokens' limit";
                    Log.Warning("API Failure (Fallback Path): {Reason}. This is a configuration issue. Model: {Model}, Prompt: {Prompt}", primaryReason, primaryModelUsed, userPrompt);
                }
                else
                {
                    Log.Warning("API Failure (Fallback Path): Primary model failed with an unknown error. See full log dump in 'Additional Info'. Model: {Model}, Prompt: {Prompt}", primaryModelUsed, userPrompt);
                }

                finalErrorMessage = $"{_aiNameBuffer}>> Error: The request to your primary model failed because {primaryReason}. Automatic fallback to other models was also unsuccessful.";
            }
            else
            {
                if (primaryResult.Exception != null)
                {
                    finalErrorMessage = $"{_aiNameBuffer}>> An unexpected error occurred: {primaryResult.Exception.Message}";
                    Log.Error(primaryResult.Exception, "A critical network or parsing error occurred. Prompt: {Prompt}", userPrompt);
                }
                else if (primaryResult.ResponseJson != null && primaryResult.HttpResponse != null)
                {
                    string? blockReason = (string?)primaryResult.ResponseJson.SelectToken("promptFeedback.blockReason");
                    string? finishReason = (string?)primaryResult.ResponseJson.SelectToken("candidates[0].finishReason");

                    if (primaryResult.HttpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: API rate limit reached. This could be Requests Per Minute (RPM) or Requests Per Day (RPD).";
                        Log.Warning("API Failure: Rate Limit Exceeded. Model: {Model}, Prompt: {Prompt}", primaryModelUsed, userPrompt);
                    }
                    else if (finishReason == "MAX_TOKENS")
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The response was stopped because it exceeded the 'max_tokens' limit. You can increase this value in /ai cfg.";
                        Log.Warning("API Failure: Max Tokens Exceeded. This is a configuration issue. Model: {Model}, Prompt: {Prompt}", primaryModelUsed, userPrompt);
                    }
                    else if (!string.IsNullOrEmpty(blockReason))
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The prompt was blocked by the API. Reason: {blockReason}.";
                        Log.Warning("API Failure: Prompt Blocked. Reason: {Reason}. Model: {Model}, Prompt: {Prompt}", blockReason, primaryModelUsed, userPrompt);
                    }
                    else
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The request was rejected by the API for an unknown reason.";
                        Log.Warning("API Failure: Request rejected for an unknown reason. See full log dump in 'Additional Info'. Model: {Model}, Prompt: {Prompt}", primaryModelUsed, userPrompt);
                    }
                }
                else
                {
                    finalErrorMessage = $"{_aiNameBuffer}>> The request failed with an unknown critical error.";
                    Log.Error("A critical unknown error occurred. The API response was likely null or malformed. Prompt: {Prompt}", userPrompt);
                }
            }

            PrintMessageToChat(finalErrorMessage);

            if (configuration.ShowAdditionalInfo)
            {
                var infoBuilder = new StringBuilder();
                infoBuilder.AppendLine($"{_aiNameBuffer}>> --- Technical Info ---");
                infoBuilder.AppendLine($"Prompt: {userPrompt}");
                infoBuilder.AppendLine($"Primary Model Setting: {primaryModelUsed}");
                int? promptTokenCount = (int?)primaryResult.ResponseJson?.SelectToken("usageMetadata.promptTokenCount");
                if (promptTokenCount.HasValue)
                {
                    infoBuilder.AppendLine($"Prompt Token Usage: {promptTokenCount}");
                }
                infoBuilder.AppendLine("--- Attempt Breakdown ---");
                for (int i = 0; i < failedAttempts.Count; i++)
                {
                    var attempt = failedAttempts[i];
                    string? finishReason = (string?)attempt.Result.ResponseJson?.SelectToken("candidates[0].finishReason");
                    string? blockReason = (string?)attempt.Result.ResponseJson?.SelectToken("promptFeedback.blockReason");
                    string status = attempt.Result.HttpResponse != null ? $"{(int)attempt.Result.HttpResponse.StatusCode} - {attempt.Result.HttpResponse.StatusCode}" : "N/A";
                    infoBuilder.AppendLine($"Attempt {i + 1} ({attempt.Model}): FAILED");
                    infoBuilder.AppendLine($"  Status: {status}");
                    infoBuilder.AppendLine($"  Finish Reason: {finishReason ?? "N/A"}");
                    infoBuilder.AppendLine($"  Block Reason: {blockReason ?? "N/A"}");
                }
                PrintMessageToChat(infoBuilder.ToString().TrimEnd());
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
                case 0:
                    string characterName = string.IsNullOrEmpty(_localPlayerName) ? "Adventurer" : _localPlayerName;
                    userNameInstruction = $"You must address the user, your conversation partner, as {characterName}.\n";
                    break;
                case 1:
                    string customName = string.IsNullOrWhiteSpace(configuration.CustomUserName) ? "Adventurer" : configuration.CustomUserName;
                    userNameInstruction = $"You must address the user, your conversation partner, as {customName}.\n";
                    break;
            }

            return $"{basePrompt}{aiNameInstruction}{userNameInstruction}{userPersonaPrompt}";
        }

        #region Configuration and Plugin Lifecycle

        private void OpenChatWindow()
        {
            _drawChatWindow = true;
        }

        private void OpenConfig()
        {
            LoadConfigIntoBuffers();
            LoadAvailablePersonas();

            int matchingIndex = _personaFiles.FindIndex(f =>
                f.Equals(configuration.AIName, StringComparison.OrdinalIgnoreCase));

            if (matchingIndex != -1)
            {
                _selectedPersonaIndex = matchingIndex;
                _saveAsNameBuffer = _personaFiles[matchingIndex];
            }
            else
            {
                _selectedPersonaIndex = 0;
                _saveAsNameBuffer = configuration.AIName;
            }
            drawConfiguration = true;
        }

        private void DrawConfiguration()
        {
            if (!drawConfiguration) return;

            ImGui.Begin($"{Name} Configuration", ref drawConfiguration, ImGuiWindowFlags.AlwaysAutoResize);

            if (UIHelper.AddHeaderIcon(PluginInterface, "chat_button", FontAwesomeIcon.Comment, out var openChatPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Chat Window" }) && openChatPressed)
            {
                OpenChatWindow();
            }

            ImGui.Text("API Key for Google AI:");
            ImGui.InputText("##apikey", ref _apiKeyBuffer, 60, ImGuiInputTextFlags.Password);
            ImGui.SameLine();
            if (ImGui.SmallButton("Get API Key"))
            {
                Util.OpenLink("https://aistudio.google.com/app/apikey");
            }
            ImGui.Spacing();
            int minTokens = minimumThinkingBudget;
            int maxTokens = 8192;

            if (ImGui.SliderInt("Max Tokens", ref _maxTokensBuffer, minTokens, maxTokens))
            {
                _maxTokensBuffer = Math.Clamp(_maxTokensBuffer, minTokens, maxTokens);
            }
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
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _saveAsNameBuffer = _aiNameBuffer;
            }
            ImGui.SameLine();
            ImGui.SetCursorPosX(500.0f);
            ImGui.RadioButton("Character Name", ref _addressingModeBuffer, 0);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("You can also additionally use System Prompt to override this.\n" +
                                 "Example:\n" +
                                 "Don't call me by my real name. Address me as Warrior of Light instead of my real name.");
            }

            ImGui.Checkbox("Prioritize System Prompt to define AI's name", ref _letSystemPromptHandleAINameBuffer);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("CHECKED: The System Prompt below will be prioritized for how the AI identifies itself.\n" +
                                 "Have small chance to behave abnormally if you set different name above.\n" +
                                 "UNCHECKED: The AI's name will use the setting above.\n" +
                                 "May behave abnormally if you have additional prompt for name.");
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
            ImGui.Text("Persona Profiles:");
            ImGui.SetNextItemWidth(230);
            if (ImGui.Combo("##personaselect", ref _selectedPersonaIndex, _personaFiles.ToArray(), _personaFiles.Count))
            {
                if (_personaFiles[_selectedPersonaIndex] == "<New Profile>")
                {
                    _saveAsNameBuffer = _aiNameBuffer;
                }
                else
                {
                    _saveAsNameBuffer = _personaFiles[_selectedPersonaIndex];
                }
            }
            if (ImGui.IsItemClicked())
            {
                LoadAvailablePersonas();
            }

            ImGui.SameLine();
            if (ImGui.Button("Load"))
            {
                if (_selectedPersonaIndex != -1)
                {
                    LoadPersona(_personaFiles[_selectedPersonaIndex]);
                }
            }
            ImGui.SameLine();
            ImGui.SetCursorPosX(390.0f);
            ImGui.Text("Save Current Persona As:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180);
            ImGui.InputText("##saveasname", ref _saveAsNameBuffer, 64);
            ImGui.SameLine();
            if (ImGui.Button("Save Profile"))
            {
                if (string.IsNullOrWhiteSpace(_saveAsNameBuffer)) { /* Do nothing */ }
                else if (_saveAsNameBuffer.Equals("<New Profile>", StringComparison.OrdinalIgnoreCase))
                {
                    _showInvalidNameConfirmation = true;
                    ImGui.OpenPopup("Invalid Name");
                }
                else
                {
                    string filePath = Path.Combine(_personaFolder.FullName, _saveAsNameBuffer + ".json");
                    if (File.Exists(filePath))
                    {
                        _showOverwriteConfirmation = true;
                        ImGui.OpenPopup("Overwrite Confirmation");
                    }
                    else
                    {
                        SavePersona(_saveAsNameBuffer);
                    }
                }
            }
            if (_showInvalidNameConfirmation)
            {
                if (ImGui.BeginPopupModal("Invalid Name", ref _showInvalidNameConfirmation, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.Text("'<New Profile>' is a reserved name and cannot be used.");
                    ImGui.Separator();
                    ImGui.Spacing();

                    float buttonWidth = 120;
                    float windowWidth = ImGui.GetWindowSize().X;
                    float startX = (windowWidth - buttonWidth) * 0.5f;

                    if (startX > 0)
                    {
                        ImGui.SetCursorPosX(startX);
                    }
                    if (ImGui.Button("OK", new Vector2(buttonWidth, 0)))
                    {
                        _showInvalidNameConfirmation = false;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.EndPopup();
                }
            }
            if (_showOverwriteConfirmation)
            {
                if (ImGui.BeginPopupModal("Overwrite Confirmation", ref _showOverwriteConfirmation, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.Text($"The profile named '{_saveAsNameBuffer}' already exists.");
                    ImGui.Text("Do you want to overwrite it?");
                    ImGui.Separator();
                    ImGui.Spacing();

                    float buttonWidth = 120;
                    float spacing = 10;
                    float totalWidth = (buttonWidth * 2) + spacing;

                    float windowWidth = ImGui.GetWindowSize().X;
                    float startX = (windowWidth - totalWidth) * 0.5f;

                    if (startX > 0)
                    {
                        ImGui.SetCursorPosX(startX);
                    }

                    if (ImGui.Button("Yes, Overwrite", new Vector2(buttonWidth, 0)))
                    {
                        SavePersona(_saveAsNameBuffer);
                        _showOverwriteConfirmation = false;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine(0, spacing);

                    if (ImGui.Button("No, Cancel", new Vector2(buttonWidth, 0)))
                    {
                        _showOverwriteConfirmation = false;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.EndPopup();
                }
            }

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
                ImGui.SetTooltip("Allows the AI to remember the recent context of your conversation.\n" +
                                "This creates a more natural, flowing dialogue but uses more tokens.\n" +
                                "The AI may forget specific details from earlier in long conversations.\n" +
                                "This short-term memory is cleared when the plugin starts, the persona changes,\n" +
                                "or by using the /ai reset command.");
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

            ImGui.Checkbox("Custom Chat Color", ref _useCustomColorsBuffer);
            if (_useCustomColorsBuffer)
            {
                ImGui.SameLine();
                ImGui.ColorEdit4("Text Color", ref _foregroundColorBuffer, ImGuiColorEditFlags.NoInputs);
            }

            ImGui.Separator();
            if (ImGui.Button("Save and Close"))
            {
                var oldPersonaState = new
                {
                    Name = configuration.AIName,
                    LetSystemPromptHandleName = configuration.LetSystemPromptHandleAIName,
                    Mode = configuration.AddressingMode,
                    CustomUser = configuration.CustomUserName,
                    Prompt = configuration.SystemPrompt
                };

                if (oldPersonaState.Name != _aiNameBuffer)
                {
                    SaveCurrentSessionLog(oldPersonaState.Name);
                    _currentSessionChatLog.Clear();
                    _historicalChatLog.Clear();
                }

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
                configuration.UseCustomColors = _useCustomColorsBuffer;
                configuration.ForegroundColor = _foregroundColorBuffer;

                configuration.Save();

                bool personaChanged = oldPersonaState.Name != configuration.AIName ||
                                      oldPersonaState.LetSystemPromptHandleName != configuration.LetSystemPromptHandleAIName ||
                                      oldPersonaState.Mode != configuration.AddressingMode ||
                                      oldPersonaState.CustomUser != configuration.CustomUserName ||
                                      oldPersonaState.Prompt != configuration.SystemPrompt;

                if (personaChanged)
                {
                    Log.Info("Persona configuration changed. Resetting conversation history.");
                    InitializeConversation();

                    if (oldPersonaState.Name != configuration.AIName)
                    {
                        LoadHistoricalLogs(configuration.AIName);
                    }
                }

                drawConfiguration = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Open Persona Folder"))
            {
                Util.OpenLink(_personaFolder.FullName);
            }

            ImGui.End();
        }

        public void Dispose()
        {
            SaveCurrentSessionLog();
            drawConfiguration = false;
            CommandManager.RemoveHandler(commandName);
            PluginInterface.UiBuilder.Draw -= DrawConfiguration;
            PluginInterface.UiBuilder.Draw -= DrawChatWindow;
            PluginInterface.UiBuilder.OpenMainUi -= OpenChatWindow;
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
            [JsonProperty("googleSearch", NullValueHandling = NullValueHandling.Ignore)]
            public GoogleSearch? GoogleSearch { get; set; }

            [JsonProperty("urlContext", NullValueHandling = NullValueHandling.Ignore)]
            public UrlContext? UrlContext { get; set; }
        }
        public class GoogleSearch { }
        public class UrlContext { }
        #endregion
    }

    internal static class UIHelper
    {
        public class HeaderIconOptions
        {
            public string Tooltip { get; set; } = string.Empty;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct ImGuiWindow
        {
            [FieldOffset(0xC)] public ImGuiWindowFlags Flags;
            [FieldOffset(0xD5)] public byte HasCloseButton;
        }

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern nint igGetCurrentWindow();

        private static unsafe ImGuiWindow* GetCurrentWindow() => (ImGuiWindow*)igGetCurrentWindow();
        private static unsafe ImGuiWindowFlags GetCurrentWindowFlags() => GetCurrentWindow()->Flags;
        private static unsafe bool CurrentWindowHasCloseButton() => GetCurrentWindow()->HasCloseButton != 0;

        private static uint _headerLastWindowID = 0;
        private static ulong _headerLastFrame = 0;
        private static uint _headerCurrentPos = 0;
        private static float _headerImGuiButtonWidth = 0;

        public static unsafe bool AddHeaderIcon(IDalamudPluginInterface pluginInterface, string id, FontAwesomeIcon icon, out bool pressed, HeaderIconOptions? options = null)
        {
            pressed = false;
            if (ImGui.IsWindowCollapsed()) return false;

            var scale = ImGuiHelpers.GlobalScale;
            var currentID = ImGui.GetID(0);
            if (currentID != _headerLastWindowID || _headerLastFrame != pluginInterface.UiBuilder.FrameCount)
            {
                _headerLastWindowID = currentID;
                _headerLastFrame = pluginInterface.UiBuilder.FrameCount;
                _headerCurrentPos = 0;
                _headerImGuiButtonWidth = 0f;

                if (CurrentWindowHasCloseButton())
                    _headerImGuiButtonWidth += 17 * scale;
                if (!GetCurrentWindowFlags().HasFlag(ImGuiWindowFlags.NoCollapse))
                    _headerImGuiButtonWidth += 17 * scale;
            }

            var prevCursorPos = ImGui.GetCursorPos();
            var buttonSize = new Vector2(20 * scale);
            var buttonPos = new Vector2(ImGui.GetWindowWidth() - buttonSize.X - _headerImGuiButtonWidth - 20 * _headerCurrentPos++ * scale - ImGui.GetStyle().FramePadding.X * 2, ImGui.GetScrollY() + 1);

            ImGui.SetCursorPos(buttonPos);
            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRectFullScreen();

            ImGui.InvisibleButton(id, buttonSize);
            var itemMin = ImGui.GetItemRectMin();
            var itemMax = ImGui.GetItemRectMax();
            var halfSize = ImGui.GetItemRectSize() / 2;
            var center = itemMin + halfSize;

            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(itemMin, itemMax, false))
            {
                if (options != null && !string.IsNullOrEmpty(options.Tooltip))
                {
                    ImGui.SetTooltip(options.Tooltip);
                }

                drawList.AddCircleFilled(center, halfSize.X, ImGui.GetColorU32(ImGui.IsMouseDown(ImGuiMouseButton.Left) ? ImGuiCol.ButtonActive : ImGuiCol.ButtonHovered));
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    pressed = true;
                }
            }

            ImGui.SetCursorPos(buttonPos);

            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            var iconString = icon.ToIconString();
            drawList.AddText(Dalamud.Interface.UiBuilder.IconFont, ImGui.GetFontSize(), itemMin + halfSize - ImGui.CalcTextSize(iconString) / 2, 0xFFFFFFFF, iconString);
            ImGui.PopFont();

            ImGui.PopClipRect();
            ImGui.SetCursorPos(prevCursorPos);

            return pressed;
        }
    }
}