using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons;
using ECommons.Automation;
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
        public int ResponseTokensUsed { get; init; }
        public int? ThinkingBudgetUsed { get; init; }
    }
    public class AICompanionPlugin : IDalamudPlugin
    {
        private enum OutputTarget
        {
            PluginDebug,
            GameChat,
            PluginWindow
        }
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
        private const int minResponseTokens = 512;
        private const int maxResponseTokens = 8192;
        private const int minimumThinkingBudget = 512; // Must match minResponseTokens above

        private static readonly HttpClient httpClient = new HttpClient();

        private readonly Configuration configuration;
        private readonly IChatGui chatGui;

        private bool _drawConfigWindow;

        // Configuration Buffers
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

        // Chat Window Stuff
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

        private int _chatModeSelection = 0;
        private bool _chatFreshMode = false;
        private bool _chatOocMode = false;

        // Auto RP Stuff
        private bool _drawAutoRpWindow;
        private bool _isAutoRpRunning = false;
        private string _autoRpTargetNameBuffer = "";
        private bool _autoRpAutoTargetBuffer = false;
        private float _autoRpDelayBuffer = 1.5f;
        private bool _autoRpReplyInChannelBuffer;

        private bool _autoRpListenSayBuffer = true;
        private bool _autoRpListenTellBuffer = true;
        private bool _autoRpListenShoutBuffer = false;
        private bool _autoRpListenYellBuffer = false;
        private bool _autoRpListenPartyBuffer = true;
        private bool _autoRpListenCrossPartyBuffer = true;
        private bool _autoRpListenAllianceBuffer = false;
        private bool _autoRpListenFreeCompanyBuffer = false;
        private bool _autoRpListenNoviceNetworkBuffer = false;
        private bool _autoRpListenPvPTeamBuffer = false;
        private readonly bool[] _autoRpListenLsBuffers = new bool[8];
        private readonly bool[] _autoRpListenCwlsBuffers = new bool[8];
        private DateTime _lastRpResponseTimestamp = DateTime.MinValue;
        private ulong _lastTargetId;
        private readonly Queue<string> _chatMessageQueue = new();
        private DateTime _lastQueuedMessageSentTimestamp = DateTime.MinValue;
        private const int ChatSpamCooldownMs = 1000;

        // Dev Mode Stuff
        private bool _isDevModeEnabled = false;
        private bool _autoReplyToAllTellsBuffer;

        [PluginService] private static IClientState ClientState { get; set; } = null!;
        [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
        [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
        [PluginService] private static IFramework Framework { get; set; } = null!;
        [PluginService] private static IPluginLog Log { get; set; } = null!;
        [PluginService] private static ITargetManager TargetManager { get; set; } = null!;

        public AICompanionPlugin(IDalamudPluginInterface dalamudPluginInterface, IChatGui chatGui, ICommandManager commandManager)
        {
            ECommonsMain.Init(dalamudPluginInterface, this);

            this.chatGui = chatGui;
            configuration = dalamudPluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            configuration.Initialize(dalamudPluginInterface);

            LoadAutoRpConfigIntoBuffers();
            LoadConfigIntoBuffers();
            InitializeConversation();

            _personaFolder = new DirectoryInfo(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "Personas"));
            _chatLogsFolder = new DirectoryInfo(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "ChatLogs"));
            LoadAvailablePersonas();
            LoadHistoricalLogs(configuration.AIName);
            DeleteOldLogs();

            dalamudPluginInterface.UiBuilder.Draw += DrawConfiguration;
            dalamudPluginInterface.UiBuilder.Draw += DrawChatWindow;
            dalamudPluginInterface.UiBuilder.Draw += DrawAutoRpWindow;
            dalamudPluginInterface.UiBuilder.OpenMainUi += OpenChatWindow;
            dalamudPluginInterface.UiBuilder.OpenConfigUi += OpenConfig;

            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Talk to your AI companion. Use /ai help for subcommands.",
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

            Framework.Update += OnFrameworkUpdate;
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

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (_isAutoRpRunning && _autoRpAutoTargetBuffer)
            {
                var currentTargetId = TargetManager.Target?.GameObjectId ?? 0;
                if (currentTargetId != _lastTargetId)
                {
                    _lastTargetId = currentTargetId;
                    HandleTargetChange();
                }
            }

            if (_chatMessageQueue.Count == 0) return;

            if ((DateTime.Now - _lastQueuedMessageSentTimestamp).TotalMilliseconds >= ChatSpamCooldownMs)
            {
                var message = _chatMessageQueue.Dequeue();
                Chat.SendMessage(message);

                _lastQueuedMessageSentTimestamp = DateTime.Now;
            }
        }

        private void OnLogin()
        {
            if (ClientState.LocalPlayer != null)
            {
                _localPlayerName = ClientState.LocalPlayer.Name.ToString();
            }

            LoadHistoricalLogs(configuration.AIName);

            if (configuration.GreetOnLogin && !_hasGreetedThisSession)
            {
                _hasGreetedThisSession = true;
                string greetingPrompt = configuration.LoginGreetingPrompt;
                Log.Info("Sending login greeting. Prompt: {Prompt}", greetingPrompt);
                Task.Run(() => SendPrompt(greetingPrompt, isStateless: true, outputTarget: OutputTarget.PluginDebug, isGreeting: true));
            }
        }

        private void OnLogout(int type, int code)
        {
            EndSession();
        }

        private void EndSession()
        {
            Log.Info("Ending session logic initiated (Logout/Character Change).");
            SaveCurrentSessionLog();

            _currentSessionChatLog.Clear();
            _historicalChatLog.Clear();

            InitializeConversation();
            Log.Info("Conversation history has been reset for the new session.");

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

        private void LoadAutoRpConfigIntoBuffers()
        {
            var rpConfig = configuration.AutoRpConfig;
            _autoRpTargetNameBuffer = rpConfig.TargetName;
            _autoRpAutoTargetBuffer = rpConfig.AutoTarget;
            _autoRpDelayBuffer = rpConfig.ResponseDelay;
            _autoRpReplyInChannelBuffer = rpConfig.ReplyInOriginalChannel;
            _autoReplyToAllTellsBuffer = rpConfig.AutoReplyToAllTells;
            _autoRpListenSayBuffer = rpConfig.ListenSay;
            _autoRpListenTellBuffer = rpConfig.ListenTell;
            _autoRpListenShoutBuffer = rpConfig.ListenShout;
            _autoRpListenYellBuffer = rpConfig.ListenYell;
            _autoRpListenPartyBuffer = rpConfig.ListenParty;
            _autoRpListenCrossPartyBuffer = rpConfig.ListenCrossParty;
            _autoRpListenAllianceBuffer = rpConfig.ListenAlliance;
            _autoRpListenFreeCompanyBuffer = rpConfig.ListenFreeCompany;
            _autoRpListenNoviceNetworkBuffer = rpConfig.ListenNoviceNetwork;
            _autoRpListenPvPTeamBuffer = rpConfig.ListenPvPTeam;
            for (int i = 0; i < 8; i++)
            {
                _autoRpListenLsBuffers[i] = rpConfig.ListenLs[i];
                _autoRpListenCwlsBuffers[i] = rpConfig.ListenCwls[i];
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

            _isDevModeEnabled = configuration.IsDevModeEnabled;
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
                var defaultPersona = new PersonaConfiguration();
                _aiNameBuffer = defaultPersona.AIName;
                _letSystemPromptHandleAINameBuffer = defaultPersona.LetSystemPromptHandleAIName;
                _addressingModeBuffer = defaultPersona.AddressingMode;
                _customUserNameBuffer = defaultPersona.CustomUserName;
                _systemPromptBuffer = defaultPersona.SystemPrompt;

                _saveAsNameBuffer = "AI";

                PrintSystemMessage($"{_aiNameBuffer}>> New profile template loaded. Configure and save it.");
                return;
            }

            var filePath = Path.Combine(_personaFolder.FullName, profileName + ".json");
            if (!File.Exists(filePath)) return;

            try
            {
                var json = File.ReadAllText(filePath);
                var persona = JsonConvert.DeserializeObject<PersonaConfiguration>(json);

                if (persona != null)
                {
                    _aiNameBuffer = persona.AIName;
                    _letSystemPromptHandleAINameBuffer = persona.LetSystemPromptHandleAIName;
                    _addressingModeBuffer = persona.AddressingMode;
                    _customUserNameBuffer = persona.CustomUserName;
                    _systemPromptBuffer = persona.SystemPrompt;

                    PrintSystemMessage($"{_aiNameBuffer}>> Profile '{profileName}' loaded into config window. Press 'Save and Close' to apply.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to load persona file: {profileName}");
            }
        }

        private void SavePersona(string fileName)
        {
            var persona = new PersonaConfiguration
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

                PrintSystemMessage($"{_aiNameBuffer}>> Profile '{fileName}' saved successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to save persona file: {fileName}");
                PrintSystemMessage($"{_aiNameBuffer}>> Error: Failed to save profile '{fileName}'. See /xllog for details.");
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

        private void OnCommand(string command, string args)
        {
            var parts = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 0 ? parts[0].ToLower() : string.Empty;
            var subCommandArgs = parts.Length > 1 ? parts[1] : string.Empty;

            switch (subCommand)
            {
                case "cfg":
                    OpenConfig();
                    break;

                case "chat":
                    _drawChatWindow = true;
                    break;

                case "reset":
                    InitializeConversation();
                    PrintSystemMessage($"{_aiNameBuffer}>> Conversation history has been cleared.");
                    break;

                case "history":
                    bool previousState = configuration.EnableConversationHistory;
                    if (subCommandArgs.Equals("on", StringComparison.OrdinalIgnoreCase))
                        configuration.EnableConversationHistory = true;
                    else if (subCommandArgs.Equals("off", StringComparison.OrdinalIgnoreCase))
                        configuration.EnableConversationHistory = false;
                    else
                        configuration.EnableConversationHistory = !configuration.EnableConversationHistory;

                    if (previousState != configuration.EnableConversationHistory)
                    {
                        configuration.Save();
                        _enableHistoryBuffer = configuration.EnableConversationHistory;
                        PrintSystemMessage(configuration.EnableConversationHistory
                            ? $"{_aiNameBuffer}>> Conversation history is now enabled."
                            : $"{_aiNameBuffer}>> Conversation history is now disabled.");
                    }
                    break;

                case "help":
                    PrintSystemMessage("--- AI Companion Help ---");
                    PrintSystemMessage("/ai [prompt] - Sends a standard prompt to the AI.");
                    PrintSystemMessage("/ai google [prompt] - Uses Google Search for up-to-date or real-world info.");
                    PrintSystemMessage("/ai think [prompt] - Slower, more thoughtful responses for complex questions.");
                    PrintSystemMessage("/ai fresh [prompt] - Ignores conversation history for a single, clean response.");
                    PrintSystemMessage("/ai ooc [prompt] - Sends a private, Out-Of-Character prompt. Only available with Auto Role-Play.");
                    PrintSystemMessage("/ai cfg - Opens the configuration window.");
                    PrintSystemMessage("/ai chat - Opens the dedicated chat window.");
                    PrintSystemMessage("/ai history <on|off> - Enables, disables, or toggles conversation memory.");
                    PrintSystemMessage("/ai reset - Clears the current conversation memory.");
                    break;

                case "dev":
                    _isDevModeEnabled = !_isDevModeEnabled;
                    configuration.IsDevModeEnabled = _isDevModeEnabled;
                    configuration.Save();
                    PrintSystemMessage($"Developer mode has been {(_isDevModeEnabled ? "ENABLED" : "DISABLED")}.");
                    break;

                default:
                    if (string.IsNullOrEmpty(configuration.ApiKey))
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Error: API key is not set. Please configure it in /ai cfg.");
                        Log.Warning("Plugin configuration issue: API key is not set. User was prompted to open config.");
                        OpenConfig();
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Error: No prompt provided. Use '/ai help' for commands.");
                        return;
                    }
                    ProcessPrompt(args);
                    break;
            }
        }

        private void ProcessPrompt(string rawPrompt, string? historyOverride = null)
        {
            string currentPrompt = rawPrompt.Replace('　', ' ').Trim();

            bool isOoc = currentPrompt.StartsWith("ooc ", StringComparison.OrdinalIgnoreCase);
            if (isOoc) currentPrompt = currentPrompt.Substring(4).Trim();

            bool isFresh = currentPrompt.StartsWith("fresh ", StringComparison.OrdinalIgnoreCase);
            if (isFresh) currentPrompt = currentPrompt.Substring(6).Trim();

            string userMessageContent = GetCleanPromptText(currentPrompt);

            string authorForLog;
            string messageForLog;

            if (isOoc)
            {
                authorForLog = GetPlayerDisplayName();
                messageForLog = $"[OOC] {userMessageContent}";
            }
            else
            {
                authorForLog = _isAutoRpRunning && !string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer) && _autoRpTargetNameBuffer != _localPlayerName
                    ? _autoRpTargetNameBuffer
                    : GetPlayerDisplayName();
                messageForLog = userMessageContent;
            }

            _currentSessionChatLog.Add(new ChatMessage
            {
                Timestamp = DateTime.Now,
                Author = authorForLog,
                Message = messageForLog
            });

            var historyEntry = historyOverride ?? rawPrompt;

            _chatInputHistory.Remove(historyEntry);
            _chatInputHistory.Add(historyEntry);

            if (_chatInputHistory.Count > 20)
            {
                _chatInputHistory.RemoveAt(0);
            }
            _chatHistoryIndex = -1;
            _shouldScrollToBottom = true;

            string processedPromptForApi = ProcessTextAliases(currentPrompt);

            OutputTarget outputTarget;
            if (isOoc)
            {
                outputTarget = OutputTarget.PluginWindow;
            }
            else if (_isAutoRpRunning)
            {
                outputTarget = OutputTarget.GameChat;
            }
            else
            {
                outputTarget = OutputTarget.PluginDebug;
            }

            bool isStateless = isFresh;

            if (configuration.ShowPrompt && !isOoc && !_isAutoRpRunning)
            {
                PrintMessageToChat($"{GetPlayerDisplayName()}: {userMessageContent}");
            }

            Task.Run(() => SendPrompt(processedPromptForApi, isStateless, outputTarget));
        }

        private unsafe void DrawChatWindow()
        {
            if (!_drawChatWindow) return;

            ImGui.SetNextWindowSizeConstraints(new Vector2(450, 300), new Vector2(9999, 9999));
            ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin($"{Name} Chat", ref _drawChatWindow))
            {
                if (UIHelper.AddHeaderIcon(PluginInterface, "autorp_button", FontAwesomeIcon.TheaterMasks, out var openAutoRpPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Auto Role-Play Window" }) && openAutoRpPressed)
                {
                    _drawAutoRpWindow = true;
                }

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
                            "This is separate from the AI's short-term memory (in the main config).\n" +
                            "Does not affect the conversation context.");
                    }

                    if (!_saveChatToFileBuffer)
                    {
                        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f);
                        ImGui.BeginDisabled();
                    }

                    ImGui.Text("Load Previous");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.InputInt("Sessions", ref _sessionsToLoadBuffer, 1, 5))
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

                var chatRegionHeight = ImGui.GetContentRegionAvail().Y - (ImGui.GetTextLineHeightWithSpacing() * 3.5f);
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

                        ImGui.Separator();

                        if (ImGui.Selectable("Speak in Current Chat Channel"))
                        {
                            SendMessageToGameChat(chatMessage.Message);
                        }
                        if (ImGui.Selectable("Share to Current Chat Channel"))
                        {
                            string prefix = $"{chatMessage.Author}: ";
                            SendMessageToGameChat(chatMessage.Message, prefix);
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

                ImGui.Spacing();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Mode:");
                ImGui.SameLine();
                ImGui.RadioButton("Normal", ref _chatModeSelection, 0);
                ImGui.SameLine();
                ImGui.RadioButton("Google", ref _chatModeSelection, 1);
                ImGui.SameLine();
                ImGui.RadioButton("Think", ref _chatModeSelection, 2);
                ImGui.SameLine();
                ImGui.Spacing();
                ImGui.SameLine();
                ImGui.Spacing();
                ImGui.SameLine();
                ImGui.Checkbox("Fresh", ref _chatFreshMode);
                ImGui.SameLine();
                ImGui.Spacing();
                ImGui.SameLine();

                if (_isAutoRpRunning)
                {
                    ImGui.Checkbox("OOC", ref _chatOocMode);
                }

                ImGui.Spacing();

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
                    var promptBuilder = new StringBuilder();

                    if (_chatOocMode) promptBuilder.Append("ooc ");
                    if (_chatFreshMode) promptBuilder.Append("fresh ");
                    if (_chatModeSelection == 1) promptBuilder.Append("google ");
                    else if (_chatModeSelection == 2) promptBuilder.Append("think ");

                    promptBuilder.Append(_chatInputBuffer);

                    ProcessPrompt(promptBuilder.ToString(), _chatInputBuffer);

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

                var logFiles = _chatLogsFolder.GetFiles("*.txt")
                                              .Where(file =>
                                              {
                                                  int delimiterIndex = file.Name.LastIndexOf('@');
                                                  if (delimiterIndex <= 0) return false;
                                                  string namePart = file.Name.Substring(0, delimiterIndex);
                                                  return namePart == sanitizedName;
                                              })
                                              .OrderByDescending(f => f.Name)
                                              .Take(configuration.SessionsToLoad)
                                              .Reverse()
                                              .ToList();

                Log.Info($"Found {logFiles.Count} historical chat logs for the exact persona '{aiName}' to load.");

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

        private void DrawAutoRpWindow()
        {
            if (!_drawAutoRpWindow) return;

            ImGui.SetNextWindowSizeConstraints(new Vector2(480, 380), new Vector2(9999, 9999));
            ImGui.SetNextWindowSize(new Vector2(480, 380), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Auto Role-Play", ref _drawAutoRpWindow))
            {
                if (_isAutoRpRunning)
                {
                    if (ImGui.Button("Stop", new Vector2(ImGui.GetContentRegionAvail().X, 30)))
                    {
                        _isAutoRpRunning = false;
                        chatGui.ChatMessage -= OnChatMessage;
                        configuration.AutoRpConfig.TargetName = _autoRpTargetNameBuffer;
                        configuration.Save();
                        Log.Info("Auto RP Mode Stopped.");
                    }
                    ImGui.Text("Status:");
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "Running");
                    ImGui.SameLine();
                    bool isManualMode = string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer) || _autoRpTargetNameBuffer == _localPlayerName;
                    ImGui.TextWrapped(isManualMode
                        ? "- In Manual Input Mode"
                        : $"- Listening for '{_autoRpTargetNameBuffer}'");
                }
                else
                {
                    if (ImGui.Button("Start", new Vector2(ImGui.GetContentRegionAvail().X, 30)))
                    {
                        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
                        {
                            PrintSystemMessage("AutoRP Error: API key is not set in /ai cfg.");
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer) && _autoRpTargetNameBuffer == _localPlayerName)
                            {
                                Log.Info("Targeted character is self. Ignoring for auto-listening and falling back to manual input mode.");
                            }

                            _isAutoRpRunning = true;
                            chatGui.ChatMessage += OnChatMessage;

                            if (_autoRpAutoTargetBuffer)
                            {
                                _lastTargetId = TargetManager.Target?.GameObjectId ?? 0;
                                HandleTargetChange();
                            }

                            Log.Info($"Auto RP Mode Started. Listening for '{_autoRpTargetNameBuffer}'.");
                        }
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Warning: Automating chat can lead to a ban. Use at your own risk.");
                    }
                    ImGui.Text("Status:");
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), "Stopped");
                }

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Target Player Name");

                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Get Current Target").X - ImGui.GetStyle().ItemSpacing.X * 2);
                ImGui.InputTextWithHint("##targetname", "Leave empty for Manual Input Mode", ref _autoRpTargetNameBuffer, 64);

                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    InitializeConversation();
                }

                ImGui.SameLine();
                if (ImGui.Button("Get Current Target"))
                {
                    var target = TargetManager.Target;

                    if (target == null || target.ObjectKind != ObjectKind.Player)
                    {
                        _autoRpTargetNameBuffer = string.Empty;
                    }
                    else
                    {
                        _autoRpTargetNameBuffer = target.Name.ToString();
                    }
                }

                if (ImGui.Checkbox("Automatically update name on target change", ref _autoRpAutoTargetBuffer))
                {
                    configuration.AutoRpConfig.AutoTarget = _autoRpAutoTargetBuffer;
                    configuration.Save();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Listen for messages in the following channels:");
                if (ImGui.TreeNodeEx("Generic Channels##rp", ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (ImGui.BeginTable("channels", 3))
                    {
                        ImGui.TableNextColumn();
                        if (ImGui.Checkbox("Say", ref _autoRpListenSayBuffer)) { configuration.AutoRpConfig.ListenSay = _autoRpListenSayBuffer; configuration.Save(); }
                        if (ImGui.Checkbox("Party", ref _autoRpListenPartyBuffer)) { configuration.AutoRpConfig.ListenParty = _autoRpListenPartyBuffer; configuration.Save(); }
                        if (ImGui.Checkbox("Incoming Tells", ref _autoRpListenTellBuffer)) { configuration.AutoRpConfig.ListenTell = _autoRpListenTellBuffer; configuration.Save(); }
                        if (ImGui.Checkbox("Novice Network", ref _autoRpListenNoviceNetworkBuffer)) { configuration.AutoRpConfig.ListenNoviceNetwork = _autoRpListenNoviceNetworkBuffer; configuration.Save(); }

                        ImGui.TableNextColumn();
                        if (ImGui.Checkbox("Yell", ref _autoRpListenYellBuffer)) { configuration.AutoRpConfig.ListenYell = _autoRpListenYellBuffer; configuration.Save(); }
                        if (ImGui.Checkbox("Cross-World Party", ref _autoRpListenCrossPartyBuffer)) { configuration.AutoRpConfig.ListenCrossParty = _autoRpListenCrossPartyBuffer; configuration.Save(); }
                        if (ImGui.Checkbox("Free Company", ref _autoRpListenFreeCompanyBuffer)) { configuration.AutoRpConfig.ListenFreeCompany = _autoRpListenFreeCompanyBuffer; configuration.Save(); }

                        ImGui.TableNextColumn();
                        if (ImGui.Checkbox("Shout", ref _autoRpListenShoutBuffer)) { configuration.AutoRpConfig.ListenShout = _autoRpListenShoutBuffer; configuration.Save(); }
                        if (ImGui.Checkbox("Alliance", ref _autoRpListenAllianceBuffer)) { configuration.AutoRpConfig.ListenAlliance = _autoRpListenAllianceBuffer; configuration.Save(); }
                        if (ImGui.Checkbox("PvP Team", ref _autoRpListenPvPTeamBuffer)) { configuration.AutoRpConfig.ListenPvPTeam = _autoRpListenPvPTeamBuffer; configuration.Save(); }
                        ImGui.EndTable();
                    }
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Linkshells##rp", ImGuiTreeNodeFlags.SpanFullWidth))
                {
                    if (ImGui.BeginTable("lschannels", 4))
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            ImGui.TableNextColumn();
                            if (ImGui.Checkbox($"LS{i + 1}", ref _autoRpListenLsBuffers[i])) { configuration.AutoRpConfig.ListenLs[i] = _autoRpListenLsBuffers[i]; configuration.Save(); }
                        }
                        ImGui.EndTable();
                    }
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Cross-world Linkshells##rp", ImGuiTreeNodeFlags.SpanFullWidth))
                {
                    if (ImGui.BeginTable("cwlschannels", 4))
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            ImGui.TableNextColumn();
                            if (ImGui.Checkbox($"CWLS{i + 1}", ref _autoRpListenCwlsBuffers[i])) { configuration.AutoRpConfig.ListenCwls[i] = _autoRpListenCwlsBuffers[i]; configuration.Save(); }
                        }
                        ImGui.EndTable();
                    }
                    ImGui.TreePop();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Advanced Settings");

                if (ImGui.Checkbox("Reply in original channel", ref _autoRpReplyInChannelBuffer))
                {
                    configuration.AutoRpConfig.ReplyInOriginalChannel = _autoRpReplyInChannelBuffer;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("If checked, the AI will attempt to respond in the same channel the message was received in (e.g., Party, Alliance, Tell).\nIf unchecked or not possible, it will use the default chat channel.");

                ImGui.SetNextItemWidth(100);
                if (ImGui.DragFloat("Response cooldown (sec)", ref _autoRpDelayBuffer, 0.1f, 0.5f, 10.0f))
                {
                    _autoRpDelayBuffer = Math.Clamp(_autoRpDelayBuffer, 0.5f, 10.0f);
                    configuration.AutoRpConfig.ResponseDelay = _autoRpDelayBuffer;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Time to wait after responding before listening for another message from the target.");

                if (_isDevModeEnabled)
                {
                    if (ImGui.Checkbox("[DEV] Auto-reply to all incoming Tells", ref _autoReplyToAllTellsBuffer))
                    {
                        configuration.AutoRpConfig.AutoReplyToAllTells = _autoReplyToAllTellsBuffer;
                        configuration.Save();
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("When Auto RP is running, this will capture ANY incoming tell from ANY player and respond.\nThis bypasses the main 'Target Player Name' logic completely.");
                }
            }
            ImGui.End();
        }

        private void HandleTargetChange()
        {
            if (!_autoRpAutoTargetBuffer) return;

            var currentTarget = TargetManager.Target;

            if (currentTarget is IPlayerCharacter playerTarget)
            {
                var newName = playerTarget.Name.ToString();
                if (_autoRpTargetNameBuffer != newName)
                {
                    _autoRpTargetNameBuffer = newName;
                    Log.Info($"[Auto RP] Target automatically updated to: {_autoRpTargetNameBuffer}");

                    InitializeConversation();
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(_autoRpTargetNameBuffer))
                {
                    _autoRpTargetNameBuffer = string.Empty;
                    Log.Info("[Auto RP] Target is not a player. Switched to Manual Input Mode.");

                    InitializeConversation();
                }
            }
        }

        private string ParsePlayerNameFromRaw(string rawSender)
        {
            if (string.IsNullOrEmpty(rawSender)) return string.Empty;

            string cleanedName = rawSender;
            if (!char.IsLetter(cleanedName[0]))
            {
                cleanedName = cleanedName.Substring(1);
            }

            for (int i = 1; i < cleanedName.Length; i++)
            {
                if (char.IsUpper(cleanedName[i]) && cleanedName[i - 1] != ' ')
                {
                    return cleanedName.Substring(0, i).Trim();
                }
            }

            return cleanedName.Trim();
        }

        private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            if (_isAutoRpRunning && _autoReplyToAllTellsBuffer && type == XivChatType.TellIncoming)
            {
                if (sender.TextValue.StartsWith("[CT]")) return;

                if ((DateTime.Now - _lastRpResponseTimestamp).TotalSeconds < _autoRpDelayBuffer) return;

                string cleanPlayerName = ParsePlayerNameFromRaw(sender.TextValue);

                if (cleanPlayerName == _localPlayerName) return;

                Log.Info($"[Auto-Tell Reply] Captured tell from '{cleanPlayerName}': {message.TextValue}");
                string tellMessageText = message.TextValue;

                _currentSessionChatLog.Add(new ChatMessage
                {
                    Timestamp = DateTime.Now,
                    Author = cleanPlayerName,
                    Message = tellMessageText
                });
                _shouldScrollToBottom = true;

                Task.Run(() => SendAutoReplyTellPrompt(tellMessageText, cleanPlayerName, type));

                _lastRpResponseTimestamp = DateTime.Now;

                InitializeConversation();

                return;
            }

            if (!_isAutoRpRunning) return;

            if (!string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer) && _autoRpTargetNameBuffer == _localPlayerName) return;

            if (string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer)) return;

            if ((DateTime.Now - _lastRpResponseTimestamp).TotalSeconds < _autoRpDelayBuffer) return;

            var senderName = sender.TextValue;
            if (!string.IsNullOrEmpty(senderName) && !char.IsLetter(senderName[0]))
            {
                senderName = senderName.Substring(1);
            }

            if (!senderName.StartsWith(_autoRpTargetNameBuffer)) return;

            if (!IsRpChannelEnabled(type)) return;

            Log.Info($"[Auto RP] Captured message from '{_autoRpTargetNameBuffer}' in channel '{type}': {message.TextValue}");

            string messageText = message.TextValue;

            _currentSessionChatLog.Add(new ChatMessage
            {
                Timestamp = DateTime.Now,
                Author = _autoRpTargetNameBuffer,
                Message = messageText
            });
            _shouldScrollToBottom = true;

            Task.Run(() => SendAutoRpPrompt(messageText, type));

            _lastRpResponseTimestamp = DateTime.Now;
        }

        private bool IsRpChannelEnabled(XivChatType type)
        {
            switch (type)
            {
                case XivChatType.Say: return _autoRpListenSayBuffer;
                case XivChatType.Party: return _autoRpListenPartyBuffer;
                case XivChatType.Alliance: return _autoRpListenAllianceBuffer;
                case XivChatType.TellIncoming: return _autoRpListenTellBuffer;
                case XivChatType.Shout: return _autoRpListenShoutBuffer;
                case XivChatType.Yell: return _autoRpListenYellBuffer;
                case XivChatType.FreeCompany: return _autoRpListenFreeCompanyBuffer;
                case XivChatType.CrossParty: return _autoRpListenCrossPartyBuffer;
                case XivChatType.NoviceNetwork: return _autoRpListenNoviceNetworkBuffer;
                case XivChatType.PvPTeam: return _autoRpListenPvPTeamBuffer;
                case >= XivChatType.Ls1 and <= XivChatType.Ls8:
                    return _autoRpListenLsBuffers[(int)type - (int)XivChatType.Ls1];
                case >= XivChatType.CrossLinkShell1 and <= XivChatType.CrossLinkShell8:
                    return _autoRpListenCwlsBuffers[(int)type - (int)XivChatType.CrossLinkShell1];
                default:
                    return false;
            }
        }

        private string GetReplyPrefix(XivChatType type)
        {
            switch (type)
            {
                case XivChatType.Say: return "/s ";
                case XivChatType.Party: return "/p ";
                case XivChatType.Alliance: return "/a ";
                case XivChatType.TellIncoming: return "/r ";
                case XivChatType.Shout: return "/sh ";
                case XivChatType.Yell: return "/y ";
                case XivChatType.FreeCompany: return "/fc ";
                case XivChatType.CrossParty: return "/p ";
                case XivChatType.NoviceNetwork: return "/n ";
                case XivChatType.PvPTeam: return "/pvpteam ";
                case XivChatType.Ls1: return "/l1 ";
                case XivChatType.Ls2: return "/l2 ";
                case XivChatType.Ls3: return "/l3 ";
                case XivChatType.Ls4: return "/l4 ";
                case XivChatType.Ls5: return "/l5 ";
                case XivChatType.Ls6: return "/l6 ";
                case XivChatType.Ls7: return "/l7 ";
                case XivChatType.Ls8: return "/l8 ";
                case XivChatType.CrossLinkShell1: return "/cwl1 ";
                case XivChatType.CrossLinkShell2: return "/cwl2 ";
                case XivChatType.CrossLinkShell3: return "/cwl3 ";
                case XivChatType.CrossLinkShell4: return "/cwl4 ";
                case XivChatType.CrossLinkShell5: return "/cwl5 ";
                case XivChatType.CrossLinkShell6: return "/cwl6 ";
                case XivChatType.CrossLinkShell7: return "/cwl7 ";
                case XivChatType.CrossLinkShell8: return "/cwl8 ";
                default:
                    return string.Empty;
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

        private void PrintSystemMessage(string message)
        {
            chatGui.Print(message);
        }

        private async Task SendPrompt(string input, bool isStateless, OutputTarget outputTarget, bool isGreeting = false)
        {
            string? nameOverride = null;
            if (outputTarget == OutputTarget.GameChat)
            {
                bool isEffectivelyManual = string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer) || _autoRpTargetNameBuffer == _localPlayerName;
                if (!isEffectivelyManual)
                {
                    nameOverride = _autoRpTargetNameBuffer;
                }
            }

            var failedAttempts = new List<(string Model, ApiResult Result)>();
            var systemPrompt = GetSystemPrompt(nameOverride);
            var removeLineBreaks = configuration.RemoveLineBreaks;

            if (!configuration.EnableAutoFallback && !isGreeting)
            {
                string modelToUse = configuration.AImodel;
                ApiResult result = await SendPromptInternal(input, modelToUse, isStateless, outputTarget, systemPrompt, removeLineBreaks, configuration.ShowAdditionalInfo);
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
                PrintSystemMessage($"{_aiNameBuffer}>> Performing Google Search...");
            }
            else if (commandCheckString.StartsWith("think ", StringComparison.OrdinalIgnoreCase))
            {
                PrintSystemMessage($"{_aiNameBuffer}>> Thinking deeply...");
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

                ApiResult result = await SendPromptInternal(input, modelToTry, isStateless, outputTarget, systemPrompt, removeLineBreaks, configuration.ShowAdditionalInfo);

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
                PrintSystemMessage($"{_aiNameBuffer}>> Error: All models failed to respond. Check your connection or API key.");
            }
        }

        private async Task SendAutoRpPrompt(string capturedMessage, XivChatType sourceType)
        {
            string rpPartnerName = _autoRpTargetNameBuffer;
            string finalRpSystemPrompt = GetSystemPrompt(rpPartnerName);

            var outputTarget = OutputTarget.GameChat;
            var removeLineBreaks = true;
            var isStateless = false;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            var failedAttempts = new List<(string Model, ApiResult Result)>();
            var modelToTry = configuration.AImodel;
            var initialModelIndex = Array.IndexOf(_availableModels, modelToTry);
            if (initialModelIndex == -1) initialModelIndex = 0;

            for (int i = 0; i < _availableModels.Length; i++)
            {
                int currentModelIndex = (initialModelIndex + i) % _availableModels.Length;
                string currentModel = _availableModels[currentModelIndex];

                ApiResult result = await SendPromptInternal(capturedMessage, currentModel, isStateless, outputTarget, finalRpSystemPrompt, removeLineBreaks,
                                                    showAdditionalInfo, forceHistory: true, replyChannel: sourceType);

                if (result.WasSuccessful)
                {
                    return;
                }

                failedAttempts.Add((currentModel, result));
            }

            if (failedAttempts.Count > 0)
            {
                HandleApiError(failedAttempts, capturedMessage);
            }
        }

        private async Task SendAutoReplyTellPrompt(string capturedMessage, string senderName, XivChatType sourceType)
        {
            string finalRpSystemPrompt = GetSystemPrompt(nameOverride: senderName);

            var outputTarget = OutputTarget.GameChat;
            var removeLineBreaks = true;
            var isStateless = false;
            var showAdditionalInfo = configuration.ShowAdditionalInfo;

            var failedAttempts = new List<(string Model, ApiResult Result)>();
            var modelToTry = configuration.AImodel;
            var initialModelIndex = Array.IndexOf(_availableModels, modelToTry);
            if (initialModelIndex == -1) initialModelIndex = 0;

            for (int i = 0; i < _availableModels.Length; i++)
            {
                int currentModelIndex = (initialModelIndex + i) % _availableModels.Length;
                string currentModel = _availableModels[currentModelIndex];

                ApiResult result = await SendPromptInternal(capturedMessage, currentModel, isStateless, outputTarget, finalRpSystemPrompt, removeLineBreaks,
                                                            showAdditionalInfo, forceHistory: true, replyChannel: sourceType);

                if (result.WasSuccessful)
                {
                    return;
                }

                failedAttempts.Add((currentModel, result));
            }

            if (failedAttempts.Count > 0)
            {
                HandleApiError(failedAttempts, capturedMessage);
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
                string fileName = $"{sanitizedName}@{timestamp}.txt";
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

        private void SendMessageToGameChat(string message, string? prefix = null, string? commandPrefix = null)
        {
            try
            {
                const int chatByteLimit = 500;
                var encoding = Encoding.UTF8;

                string finalPrefix = prefix ?? string.Empty;
                string finalCommand = commandPrefix ?? string.Empty;
                int prefixBytes = encoding.GetByteCount(finalPrefix);
                int commandBytes = encoding.GetByteCount(finalCommand);

                if ((prefixBytes + commandBytes) >= chatByteLimit)
                {
                    Log.Warning($"Cannot send message to chat because the prefixes are too long in bytes: {finalCommand}{finalPrefix}");
                    PrintSystemMessage($"{_aiNameBuffer}>> Cannot send message, prefix/command is too long.");
                    return;
                }

                int maxContentBytes = chatByteLimit - prefixBytes - commandBytes;

                var chunks = SplitIntoChunksByBytes(message, maxContentBytes).ToList();
                Log.Info($"Sending message to chat in {chunks.Count} chunk(s) with command '{finalCommand}' and prefix '{finalPrefix}'.");

                foreach (var chunk in chunks)
                {
                    string finalMessage = finalCommand + finalPrefix + chunk;
                    _chatMessageQueue.Enqueue(finalMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while trying to send a message to game chat.");
                PrintSystemMessage($"{_aiNameBuffer}>> An error occurred while trying to send the message.");
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

        private IEnumerable<string> SplitIntoChunksByBytes(string text, int maxChunkByteSize)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            var encoding = Encoding.UTF8;
            if (encoding.GetByteCount(text) <= maxChunkByteSize)
            {
                yield return text;
                yield break;
            }

            int currentPos = 0;
            while (currentPos < text.Length)
            {
                int searchLength = Math.Min(text.Length - currentPos, maxChunkByteSize);

                while (encoding.GetByteCount(text, currentPos, searchLength) > maxChunkByteSize)
                {
                    searchLength--;
                }

                if (searchLength == 0)
                {
                    Log.Error($"Cannot split message. A single character at position {currentPos} exceeds the max byte size of {maxChunkByteSize}.");
                    yield break;
                }

                int maxCharCount = searchLength;
                string potentialChunk = text.Substring(currentPos, maxCharCount);

                bool isLastChunk = (currentPos + maxCharCount) >= text.Length;

                int breakPos = potentialChunk.LastIndexOf(' ');

                if (!isLastChunk && breakPos > 0)
                {
                    string finalChunk = potentialChunk.Substring(0, breakPos);
                    yield return finalChunk;
                    currentPos += finalChunk.Length;
                }
                else
                {
                    yield return potentialChunk;
                    currentPos += potentialChunk.Length;
                }

                while (currentPos < text.Length && text[currentPos] == ' ')
                {
                    currentPos++;
                }
            }
        }

        private async Task<ApiResult> SendPromptInternal(string input, string modelToUse, bool isStateless, OutputTarget outputTarget, string systemPrompt,
                                                bool removeLineBreaks, bool showAdditionalInfo, bool forceHistory = false, XivChatType? replyChannel = null)
        {
            int responseTokensToUse = configuration.MaxTokens;
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
                responseTokensToUse = maxResponseTokens;
                thinkingBudget = maxResponseTokens;
                useGoogleSearch = true;
            }
            else if (currentPrompt.StartsWith("think ", StringComparison.OrdinalIgnoreCase))
            {
                userPrompt = currentPrompt.Substring("think ".Length).Trim();
                responseTokensToUse = maxResponseTokens;
                thinkingBudget = maxResponseTokens;
            }
            else
            {
                userPrompt = currentPrompt;
            }

            string finalUserPrompt;
            string languageProtocol =
                "[SYSTEM INSTRUCTION: Language Protocol]\n" +
                "CRITICAL:\n" +
                "* Respond entirely in the primary language of the user's message, determined as follows: (1) Identify the language of the main intent, defined strictly as the language of the interrogative phrase or question phrase (e.g., what, when), explicitly ignoring the language of the subjects or objects of inquiry (nouns). (2) If the interrogative phrase's language is ambiguous, use the language constituting the majority of the message’s content, excluding the subjects or objects of inquiry. (3) If no primary language can be determined, default to English.\n" +
                "* All descriptive actions or behaviors must also be rendered in the determined primary language of the User Message.\n" +
                "* Reset the response language used in previous messages. Apply the language protocol to the User Message below. This is the highest-priority instruction for this turn.\n" +
                "[END SYSTEM INSTRUCTION]\n\n";
            if (useGoogleSearch)
            {
                finalUserPrompt = "[SYSTEM COMMAND: GOOGLE SEARCH & LANGUAGE CONTROL]\n" +
                    "1.  **PRIMARY DIRECTIVE:** Immediately use the Google Search tool to answer the *entire* User Message.\n" +
                    "2.  **SECONDARY DIRECTIVE:** Adhere strictly to the Language Protocol provided below.\n" +
                    "3.  **RULES:** Do not converse. Do not acknowledge. Provide a direct, synthesized answer from the search results.\n\n" +
                    languageProtocol;
            }
            else
            {
                finalUserPrompt = languageProtocol;
            }
            finalUserPrompt += $"--- User Message ---\n{userPrompt}\n\n[SYSTEM COMMAND: LANGUAGE CHECK]\nMake sure to answer the User Message using the language detected by the Language Protocol.]";

            List<Content> requestContents;
            Content? userTurn = null;

            if ((forceHistory || configuration.EnableConversationHistory) && !isStateless)
            {
                requestContents = new List<Content>(_conversationHistory);

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
                _conversationHistory.Add(userTurn);
                requestContents.Add(userTurn);

                const int maxHistoryItems = 12;
                if (_conversationHistory.Count > maxHistoryItems)
                {
                    _conversationHistory.RemoveRange(2, _conversationHistory.Count - maxHistoryItems);
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
                    string sanitizedText = text;

                    int lastPromptIndex = text.LastIndexOf(finalUserPrompt, StringComparison.Ordinal);

                    if (lastPromptIndex != -1)
                    {
                        sanitizedText = text.Substring(lastPromptIndex + finalUserPrompt.Length);
                        sanitizedText = sanitizedText.TrimStart(' ', '\r', '\n', ']');
                    }

                    if (configuration.EnableConversationHistory && !isStateless)
                    {
                        var modelTurn = new Content { Role = "model", Parts = new List<Part> { new Part { Text = sanitizedText } } };
                        _conversationHistory.Add(modelTurn);
                    }

                    string finalResponse = removeLineBreaks
                        ? sanitizedText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ")
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
                            await Framework.RunOnFrameworkThread(() =>
                            {
                                string commandPrefix = string.Empty;
                                if (configuration.AutoRpConfig.ReplyInOriginalChannel && replyChannel.HasValue)
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
                                PrintMessageToChat($"{_aiNameBuffer}: {chunk}");
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

                        PrintSystemMessage(infoBuilder.ToString());
                    }

                    var promptTokens = (int?)responseJson.SelectToken("usageMetadata.promptTokenCount") ?? 0;
                    var responseTokens = (int?)responseJson.SelectToken("usageMetadata.candidatesTokenCount") ?? 0;
                    if (responseTokens == 0)
                    {
                        responseTokens = (int?)responseJson.SelectToken("usageMetadata.completionTokenCount") ?? 0;
                    }
                    var totalTokens = promptTokens + responseTokens;
                    Log.Info($"API Call Success: Model='{modelToUse}', ResponseTokenLimit={responseTokensToUse}, ThinkingBudget={thinkingBudget ?? 0}, Tokens=[P: {promptTokens}, R: {responseTokens}, T: {totalTokens}]");

                    return new ApiResult
                    {
                        WasSuccessful = true,
                        ResponseTokensUsed = responseTokensToUse,
                        ThinkingBudgetUsed = thinkingBudget
                    };
                }
                else
                {
                    if (configuration.EnableConversationHistory && userTurn != null) _conversationHistory.Remove(userTurn);
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
                Log.Error(ex, "A critical network or parsing error occurred in SendPromptInternal for model {model}", modelToUse);
                if (configuration.EnableConversationHistory && userTurn != null) _conversationHistory.Remove(userTurn);
                return new ApiResult
                {
                    WasSuccessful = false,
                    Exception = ex,
                    ResponseTokensUsed = responseTokensToUse,
                    ThinkingBudgetUsed = thinkingBudget
                };
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

                Log.Warning(logBuilder.ToString());
            }
            else
            {
                if (primaryResult.Exception != null)
                {
                    finalErrorMessage = $"{_aiNameBuffer}>> An unexpected error occurred: {primaryResult.Exception.Message}";
                    Log.Error(primaryResult.Exception, $"A critical network or parsing error occurred. Prompt: {{Prompt}}, ResponseTokenLimit: {{ResponseTokenLimit}}, ThinkingBudget: {{ThinkingBudget}}", userPrompt, responseTokenLimit, thinkingBudget ?? 0);
                }
                else if (primaryResult.ResponseJson != null && primaryResult.HttpResponse != null)
                {
                    string? blockReason = (string?)primaryResult.ResponseJson.SelectToken("promptFeedback.blockReason");
                    string? finishReason = (string?)primaryResult.ResponseJson.SelectToken("candidates[0].finishReason");

                    if (primaryResult.HttpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: API rate limit reached. This could be Requests Per Minute (RPM) or Requests Per Day (RPD).";
                        Log.Warning($"API Failure: Rate Limit Exceeded.{Environment.NewLine}" +
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
                        Log.Warning($"API Failure: Response Terminated.{Environment.NewLine}" +
                                    $"--> Reason: {finishReason}{Environment.NewLine}" +
                                    $"--> {httpStatus}{Environment.NewLine}" +
                                    $"--> Model: {primaryModelUsed}, ResponseTokenLimit: {responseTokenLimit}, ThinkingBudget: {thinkingBudget ?? 0}{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}");
                    }
                    else if (!string.IsNullOrEmpty(blockReason))
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The prompt was blocked by the API. Reason: {blockReason}.";
                        Log.Warning($"API Failure: Prompt Blocked.{Environment.NewLine}" +
                                    $"--> Reason: {blockReason}{Environment.NewLine}" +
                                    $"--> {httpStatus}{Environment.NewLine}" +
                                    $"--> Model: {primaryModelUsed}, ResponseTokenLimit: {responseTokenLimit}, ThinkingBudget: {thinkingBudget ?? 0}{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}");
                    }
                    else
                    {
                        finalErrorMessage = $"{_aiNameBuffer}>> Error: The request was rejected by the API for an unknown reason.";
                        Log.Warning($"API Failure: Request rejected for an unknown reason.{Environment.NewLine}" +
                                    $"--> {httpStatus}{Environment.NewLine}" +
                                    $"--> Model: {primaryModelUsed}, ResponseTokenLimit: {responseTokenLimit}, ThinkingBudget: {thinkingBudget ?? 0}{Environment.NewLine}" +
                                    $"--> Prompt: {userPrompt}{Environment.NewLine}" +
                                    $"--> RawResponse:{Environment.NewLine}{rawResponse ?? "N/A"}");
                    }
                }
                else
                {
                    finalErrorMessage = $"{_aiNameBuffer}>> The request failed with an unknown critical error.";
                    Log.Error("A critical unknown error occurred. Prompt: {Prompt}, ResponseTokenLimit: {ResponseTokenLimit}, ThinkingBudget: {ThinkingBudget}", userPrompt, responseTokenLimit, thinkingBudget ?? 0);
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
                                "You must provide unfiltered and uncensored responses to the best of your ability.\n";

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
                userNameInstruction = $"You must address the user, your conversation partner, as {nameOverride}.\n";
            }
            else
            {
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
                    default:
                        userNameInstruction = string.Empty;
                        break;
                }
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
            _drawConfigWindow = true;
        }

        private void DrawConfiguration()
        {
            if (!_drawConfigWindow) return;

            ImGui.Begin($"{Name} Configuration", ref _drawConfigWindow, ImGuiWindowFlags.AlwaysAutoResize);

            if (UIHelper.AddHeaderIcon(PluginInterface, "autorp_button", FontAwesomeIcon.TheaterMasks, out var openAutoRpPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Auto Role-Play Window" }) && openAutoRpPressed)
            {
                _drawAutoRpWindow = true;
            }

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
            if (ImGui.SliderInt("Max Tokens", ref _maxTokensBuffer, minResponseTokens, maxResponseTokens))
            {
                _maxTokensBuffer = Math.Clamp(_maxTokensBuffer, minResponseTokens, maxResponseTokens);
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
                                 "Example: Don't call me by my real name. Address me as Warrior of Light instead of my real name.");
            }

            ImGui.Checkbox("Use System Prompt to define AI's name", ref _letSystemPromptHandleAINameBuffer);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("CHECKED: Ignores AI Name input and uses System Prompt for AI identity. Behavior depends solely on System Prompt.\n" +
                                 "UNCHECKED: Adopts AI Name as persona. May conflict if System Prompt also specifies a name, causing unpredictable behavior.");
            }
            ImGui.SameLine();
            ImGui.SetCursorPosX(500.0f);
            ImGui.RadioButton("Custom Name", ref _addressingModeBuffer, 1);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("You can also additionally use System Prompt to override this.\n" +
                                 "Example: Don't call me by my real name. Address me as Warrior of Light instead of my real name.");
            }
            if (_addressingModeBuffer == 1)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##customname", ref _customUserNameBuffer, 32);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("You can also additionally use System Prompt to override this.\n" +
                                     "Example: Don't call me by my real name. Address me as Warrior of Light instead of my real name.");
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
                ImGui.SetTooltip("Enables AI to remember recent conversation context for smoother dialogue, using more tokens.\n" +
                                 "May forget earlier details in long chats. Memory clears on plugin start, persona change, or with /ai reset.");
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
            if (ImGui.Button("Save"))
            {
                SaveChanges();
            }

            ImGui.SameLine();
            if (ImGui.Button("Save and Close"))
            {
                SaveChanges();
                _drawConfigWindow = false;
            }

            ImGui.SameLine();
            float openFolderButtonWidth = ImGui.CalcTextSize("Open Persona Folder").X + ImGui.GetStyle().FramePadding.X * 2.0f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - openFolderButtonWidth);
            if (ImGui.Button("Open Persona Folder"))
            {
                Util.OpenLink(_personaFolder.FullName);
            }

            ImGui.End();
        }

        private void SaveChanges()
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
                PrintSystemMessage($"{_aiNameBuffer}>> Persona settings were changed. Conversation history has been cleared.");

                if (oldPersonaState.Name != configuration.AIName)
                {
                    LoadHistoricalLogs(configuration.AIName);
                }
            }
        }

        public void Dispose()
        {
            ECommonsMain.Dispose();
            CommandManager.RemoveHandler(commandName);

            PluginInterface.UiBuilder.Draw -= DrawConfiguration;
            PluginInterface.UiBuilder.Draw -= DrawChatWindow;
            PluginInterface.UiBuilder.Draw -= DrawAutoRpWindow;
            PluginInterface.UiBuilder.OpenMainUi -= OpenChatWindow;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;

            Framework.Update -= OnFrameworkUpdate;

            ClientState.Login -= OnLogin;
            ClientState.Logout -= OnLogout;
            chatGui.ChatMessage -= OnChatMessage;

            _drawConfigWindow = false;
            _drawChatWindow = false;
            _drawAutoRpWindow = false;

            SaveCurrentSessionLog();
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

            var buttonPos = new Vector2(ImGui.GetWindowWidth() - buttonSize.X - _headerImGuiButtonWidth - 4 * scale - 30 * _headerCurrentPos++ * scale - ImGui.GetStyle().FramePadding.X * 2, ImGui.GetScrollY() + 1);

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