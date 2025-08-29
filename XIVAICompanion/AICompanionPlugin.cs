using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XIVAICompanion.Configurations;
using XIVAICompanion.Emoting;
using XIVAICompanion.Managers;
using XIVAICompanion.Models;

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
    public partial class AICompanionPlugin : IDalamudPlugin
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
                return $"{aiName}";
            }
        }

        private const string commandName = "/ai";
        private const int minResponseTokens = 512;
        private const int maxResponseTokens = 8192;
        private const int minimumThinkingBudget = 512; // minResponseTokens >= minimumThinkingBudget <= maxResponseTokens

        private static readonly HttpClient httpClient = new HttpClient();

        [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;

        private readonly Configuration configuration = null!;

        private bool _drawConfigWindow;

        // Configuration Buffers
        private string _apiKeyBuffer = string.Empty;
        private int _maxTokensBuffer;
        private readonly string[] _availableModels = { "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-flash-lite" };
        private int _selectedModelIndex = -1;
        private const int thinkingModelIndex = 0;
        private const int greetingModelIndex = 2;

        private string _aiNameBuffer = string.Empty;
        private bool _letSystemPromptHandleAINameBuffer;
        private int _addressingModeBuffer;
        private string _localPlayerName = string.Empty;
        private string _customUserNameBuffer = string.Empty;
        private string _systemPromptBuffer = string.Empty;
        private string _minionToReplaceBuffer = string.Empty;
        private string _npcGlamourerDesignGuidBuffer = string.Empty;
        private List<string> _glamourerDesigns = new();
        private int _selectedGlamourerDesignIndex = -1;
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
        private readonly Dictionary<string, List<Content>> _conversationCache = new();
        private readonly List<string> _conversationCacheLru = new();
        private const int MaxConversationCacheSize = 10;
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

        // Chat Mode Toggles
        private bool _searchModeBuffer = false;
        private bool _thinkModeBuffer = false;
        private bool _freshModeBuffer = false;
        private bool _oocModeBuffer = false;

        // Temporary variables for single-use mode states
        private bool _tempSearchMode = false;
        private bool _tempThinkMode = false;
        private bool _tempFreshMode = false;
        private bool _tempOocMode = false;

        private int _chatModeSelection = 0;
        private bool _chatFreshMode = false;
        private bool _chatOocMode = false;

        // Auto RP Stuff
        private bool _drawAutoRpWindow;
        private bool _isAutoRpRunning = false;
        private string _autoRpTargetNameBuffer = "";
        private bool _autoRpAutoTargetBuffer = false;
        private float _autoRpInitialDelayBuffer;
        private float _autoRpDelayBuffer = 1.5f;
        private bool _autoRpReplyInChannelBuffer;
        private bool _autoRpReplyInSpecificChannelBuffer;
        private int _autoRpSpecificReplyChannelBuffer;

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
        private string _currentRpPartnerName = string.Empty;
        private readonly Queue<string> _chatMessageQueue = new();
        private DateTime _lastQueuedMessageSentTimestamp = DateTime.MinValue;

        // Dev Mode Stuff
        private bool _isDevModeEnabled = false;
        private bool _autoReplyToAllTellsBuffer;

        private bool _openListenerModeBuffer;
        private bool _openListenerListenSayBuffer;
        private bool _openListenerListenTellBuffer;
        private bool _openListenerListenShoutBuffer;
        private bool _openListenerListenYellBuffer;
        private bool _openListenerListenPartyBuffer;
        private bool _openListenerListenCrossPartyBuffer;
        private bool _openListenerListenAllianceBuffer;
        private bool _openListenerListenFreeCompanyBuffer;
        private bool _openListenerListenNoviceNetworkBuffer;
        private bool _openListenerListenPvPTeamBuffer;
        private readonly bool[] _openListenerListenLsBuffers = new bool[8];
        private readonly bool[] _openListenerListenCwlsBuffers = new bool[8];

        // Minion Stuff
        public ulong _glamouredMinionObjectId;
        private Guid _lastAppliedDesignGuid = Guid.Empty;
        private bool _isWaitingForGlamourer = false;
        private readonly GlamourerManager _glamourerManager;
        private readonly MinionNamingManager _minionNamingManager;
        private readonly EmoteMimickingManager _emoteMimickingManager;

        public AICompanionPlugin(IDalamudPluginInterface dalamudPluginInterface)
        {
            Service.Initialize(dalamudPluginInterface);

            ECommonsMain.Init(Service.PluginInterface, this);
            _glamourerManager = new GlamourerManager(Service.PluginInterface);
            _minionNamingManager = new MinionNamingManager(Service.NamePlateGui, Service.ObjectTable, configuration);
            _emoteMimickingManager = new EmoteMimickingManager(this, Service.InteropProvider, Service.ClientState, Service.ObjectTable, Service.DataManager, SigScanner);

            configuration = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            configuration.Initialize(Service.PluginInterface);

            LoadAutoRpConfigIntoBuffers();
            LoadConfigIntoBuffers();
            InitializeConversation();

            if (!string.IsNullOrEmpty(configuration.MinionToReplace) && !string.IsNullOrEmpty(configuration.AIName))
            {
                _minionNamingManager.UpdateNamingConfiguration(configuration.MinionToReplace, configuration.AIName);
            }

            _personaFolder = new DirectoryInfo(Path.Combine(Service.PluginInterface.GetPluginConfigDirectory(), "Personas"));
            _chatLogsFolder = new DirectoryInfo(Path.Combine(Service.PluginInterface.GetPluginConfigDirectory(), "ChatLogs"));
            LoadAvailablePersonas();
            LoadHistoricalLogs(configuration.AIName);
            DeleteOldLogs();

            Service.PluginInterface.UiBuilder.Draw += DrawConfiguration;
            Service.PluginInterface.UiBuilder.Draw += DrawChatWindow;
            Service.PluginInterface.UiBuilder.Draw += DrawAutoRpWindow;
            Service.PluginInterface.UiBuilder.OpenMainUi += OpenChatWindow;
            Service.PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;

            Service.CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Talk to your AI companion. Use /aihelp for commands.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aicfg", new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the configuration window.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aiset", new CommandInfo(OnCommand)
            {
                HelpMessage = "Changes the current AI persona to a saved profile.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aichat", new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the dedicated chat window.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aiclear", new CommandInfo(OnCommand)
            {
                HelpMessage = "Clears the current conversation memory.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aicontext", new CommandInfo(OnCommand)
            {
                HelpMessage = "Enables or disables conversation memory.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aisummon", new CommandInfo(OnCommand)
            {
                HelpMessage = "Summon a custom NPC, configure in profile.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aisearch", new CommandInfo(OnCommand)
            {
                HelpMessage = "Uses Web Search for information from the internet.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aithink", new CommandInfo(OnCommand)
            {
                HelpMessage = "Slower, more thoughtful responses for complex questions.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aifresh", new CommandInfo(OnCommand)
            {
                HelpMessage = "Ignores conversation history for a single, clean response.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aiooc", new CommandInfo(OnCommand)
            {
                HelpMessage = "Sends a private, Out-Of-Character prompt. Only available with Auto Role-Play.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aidev", new CommandInfo(OnCommand)
            {
                HelpMessage = "Enables or disables developer mode.",
                ShowInHelp = false
            });

            Service.CommandManager.AddHandler("/ainormal", new CommandInfo(OnCommand)
            {
                HelpMessage = "Turns off both search and think modes.",
                ShowInHelp = true
            });

            Service.CommandManager.AddHandler("/aihelp", new CommandInfo(OnCommand)
            {
                HelpMessage = "Displays help for AI Companion commands.",
                ShowInHelp = true
            });

            Service.ClientState.Login += OnLogin;
            Service.ClientState.Logout += OnLogout;

            Service.Framework.RunOnFrameworkThread(() =>
            {
                if (Service.ClientState.IsLoggedIn && Service.ClientState.LocalPlayer != null)
                {
                    OnLogin();
                }
            });

            Service.Framework.Update += OnFrameworkUpdate;

            Service.PluginInterface.UiBuilder.DisableAutomaticUiHide = _isDevModeEnabled;
            Service.PluginInterface.UiBuilder.DisableCutsceneUiHide = _isDevModeEnabled;
            Service.PluginInterface.UiBuilder.DisableGposeUiHide = _isDevModeEnabled;
            Service.PluginInterface.UiBuilder.DisableUserUiHide = _isDevModeEnabled;
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
            if (Service.ClientState.LocalPlayer == null)
            {
                RevertTrackedMinion();
                _isWaitingForGlamourer = false;
            }
            else if (!string.IsNullOrEmpty(configuration.MinionToReplace)
                && !string.IsNullOrEmpty(configuration.NpcGlamourerDesignGuid)
                && Guid.TryParse(configuration.NpcGlamourerDesignGuid, out var desiredDesignGuid)
                && desiredDesignGuid != Guid.Empty)
            {
                var targetCompanion = Service.ObjectTable.FirstOrDefault(o => o.ObjectKind == ObjectKind.Companion && o.Name.TextValue.Contains(configuration.MinionToReplace));
                var glamouredCompanion = (_glamouredMinionObjectId != 0) ? Service.ObjectTable.FirstOrDefault(o => o.GameObjectId == _glamouredMinionObjectId) : null;

                if (glamouredCompanion != null && glamouredCompanion != targetCompanion)
                {
                    RevertTrackedMinion();
                    glamouredCompanion = null;
                }

                if (targetCompanion != null)
                {
                    if (targetCompanion.GameObjectId != _glamouredMinionObjectId || desiredDesignGuid != _lastAppliedDesignGuid)
                    {
                        if (_glamourerManager.IsApiAvailable)
                        {
                            Service.Log.Info($"Reconciling state for '{targetCompanion.Name.TextValue}'. Applying glamour.");
                            _glamourerManager.ApplyDesign(desiredDesignGuid, targetCompanion);
                            _glamouredMinionObjectId = targetCompanion.GameObjectId;
                            _lastAppliedDesignGuid = desiredDesignGuid;
                            _isWaitingForGlamourer = false;
                        }
                        else if (!_isWaitingForGlamourer)
                        {
                            Service.Log.Info($"Found '{targetCompanion.Name.TextValue}', but Glamourer is not ready. Waiting.");
                            _isWaitingForGlamourer = true;
                        }
                    }
                }
                else if (glamouredCompanion != null)
                {
                    RevertTrackedMinion();
                }
            }
            else
            {
                RevertTrackedMinion();
                _isWaitingForGlamourer = false;
            }

            if (_isWaitingForGlamourer)
            {
                _glamourerManager.RecheckApiAvailability();
                if (_glamourerManager.IsApiAvailable)
                {
                    Service.Log.Info("Glamourer API is now available. Attempting to apply pending glamour.");
                    if (Guid.TryParse(configuration.NpcGlamourerDesignGuid, out var designGuidToApply))
                    {
                        var companionToGlamour = Service.ObjectTable.FirstOrDefault(o =>
                            o.ObjectKind == ObjectKind.Companion &&
                            o.Name.TextValue.Contains(configuration.MinionToReplace));

                        if (companionToGlamour != null)
                        {
                            _glamourerManager.ApplyDesign(designGuidToApply, companionToGlamour);
                            _glamouredMinionObjectId = companionToGlamour.GameObjectId;
                            _lastAppliedDesignGuid = designGuidToApply;
                        }
                    }
                    _isWaitingForGlamourer = false;
                }
            }

            if (_isAutoRpRunning && _autoRpAutoTargetBuffer)
            {
                var currentTargetId = Service.TargetManager.Target?.GameObjectId ?? 0;
                if (currentTargetId != _lastTargetId)
                {
                    _lastTargetId = currentTargetId;
                    HandleTargetChange();
                }
            }

            if (_chatMessageQueue.Count == 0) return;

            var requiredChunkCooldownMs = Math.Max((int)(configuration.AutoRpConfig.InitialResponseDelaySeconds * 1000), 1000);

            if ((DateTime.Now - _lastQueuedMessageSentTimestamp).TotalMilliseconds >= requiredChunkCooldownMs)
            {
                var message = _chatMessageQueue.Dequeue();
                Chat.SendMessage(message);

                _lastQueuedMessageSentTimestamp = DateTime.Now;
            }
        }

        private void OnLogin()
        {
            if (Service.ClientState.LocalPlayer != null)
            {
                _localPlayerName = Service.ClientState.LocalPlayer.Name.ToString();
            }

            LoadHistoricalLogs(configuration.AIName);

            if (configuration.GreetOnLogin && !_hasGreetedThisSession)
            {
                _hasGreetedThisSession = true;
                string greetingPrompt = configuration.LoginGreetingPrompt;
                Service.Log.Info("Sending login greeting. Prompt: {Prompt}", greetingPrompt);
                Task.Run(() => SendPrompt(greetingPrompt, isStateless: true, outputTarget: OutputTarget.PluginDebug, partnerName: GetPlayerDisplayName(), isGreeting: true));
            }
        }

        private void OnLogout(int type, int code)
        {
            EndSession();
        }

        private void EndSession()
        {
            Service.Log.Info("Ending session logic initiated (Logout/Character Change).");
            SaveCurrentSessionLog();

            _currentSessionChatLog.Clear();
            _historicalChatLog.Clear();

            InitializeConversation();
            Service.Log.Info("Conversation history has been reset for the new session.");

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
            _autoRpInitialDelayBuffer = rpConfig.InitialResponseDelaySeconds;
            _autoRpDelayBuffer = rpConfig.ResponseDelay;
            _autoRpReplyInChannelBuffer = rpConfig.ReplyInOriginalChannel;
            _autoRpReplyInSpecificChannelBuffer = rpConfig.ReplyInSpecificChannel;
            _autoRpSpecificReplyChannelBuffer = rpConfig.SpecificReplyChannel;
            if (_autoRpSpecificReplyChannelBuffer == -1)
            {
                _autoRpSpecificReplyChannelBuffer = 0;
            }
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

            _openListenerModeBuffer = rpConfig.IsOpenListenerModeEnabled;
            _openListenerListenSayBuffer = rpConfig.OpenListenerListenSay;
            _openListenerListenTellBuffer = rpConfig.OpenListenerListenTell;
            _openListenerListenShoutBuffer = rpConfig.OpenListenerListenShout;
            _openListenerListenYellBuffer = rpConfig.OpenListenerListenYell;
            _openListenerListenPartyBuffer = rpConfig.OpenListenerListenParty;
            _openListenerListenCrossPartyBuffer = rpConfig.OpenListenerListenCrossParty;
            _openListenerListenAllianceBuffer = rpConfig.OpenListenerListenAlliance;
            _openListenerListenFreeCompanyBuffer = rpConfig.OpenListenerListenFreeCompany;
            _openListenerListenNoviceNetworkBuffer = rpConfig.OpenListenerListenNoviceNetwork;
            _openListenerListenPvPTeamBuffer = rpConfig.OpenListenerListenPvPTeam;
            for (int i = 0; i < 8; i++)
            {
                _openListenerListenLsBuffers[i] = rpConfig.OpenListenerListenLs[i];
                _openListenerListenCwlsBuffers[i] = rpConfig.OpenListenerListenCwls[i];
            }
        }

        private void LoadConfigIntoBuffers()
        {
            _selectedModelIndex = Array.IndexOf(_availableModels, configuration.AImodel);
            if (_selectedModelIndex == -1)
            {
                _selectedModelIndex = 1;
            }
            _apiKeyBuffer = configuration.ApiKey;
            _maxTokensBuffer = configuration.MaxTokens > 0 ? configuration.MaxTokens : 1024;
            _aiNameBuffer = configuration.AIName;
            _letSystemPromptHandleAINameBuffer = configuration.LetSystemPromptHandleAIName;
            _addressingModeBuffer = configuration.AddressingMode;
            _customUserNameBuffer = configuration.CustomUserName;
            _systemPromptBuffer = configuration.SystemPrompt;
            _minionToReplaceBuffer = configuration.MinionToReplace;
            _npcGlamourerDesignGuidBuffer = configuration.NpcGlamourerDesignGuid;
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

            _searchModeBuffer = configuration.SearchMode;
            _thinkModeBuffer = configuration.ThinkMode;
            _freshModeBuffer = configuration.FreshMode;
            _oocModeBuffer = configuration.OocMode;

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
                _minionToReplaceBuffer = defaultPersona.MinionToReplace;
                _npcGlamourerDesignGuidBuffer = defaultPersona.NpcGlamourerDesignGuid;

                _saveAsNameBuffer = "AI";

                UpdateGlamourerDesigns();

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
                    _minionToReplaceBuffer = persona.MinionToReplace;
                    _npcGlamourerDesignGuidBuffer = persona.NpcGlamourerDesignGuid;

                    UpdateGlamourerDesigns();

                    PrintSystemMessage($"{_aiNameBuffer}>> Profile '{profileName}' loaded into config window. Press 'Save and Close' to apply.");
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, $"Failed to load persona file: {profileName}");
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
                SystemPrompt = _systemPromptBuffer,
                MinionToReplace = _minionToReplaceBuffer,
                NpcGlamourerDesignGuid = _npcGlamourerDesignGuidBuffer
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
                Service.Log.Error(ex, $"Failed to save persona file: {fileName}");
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

        private List<Content> GetHistoryForPlayer(string playerName)
        {
            if (_conversationCache.TryGetValue(playerName, out var history))
            {
                _conversationCacheLru.Remove(playerName);
                _conversationCacheLru.Add(playerName);
                return history;
            }

            if (_conversationCache.Count >= MaxConversationCacheSize)
            {
                var lruPlayer = _conversationCacheLru[0];

                _conversationCache.Remove(lruPlayer);
                _conversationCacheLru.RemoveAt(0);
                Service.Log.Info($"Conversation cache full. Evicting history for '{lruPlayer}'.");
            }

            var newHistory = new List<Content>
            {
                new Content { Role = "user", Parts = new List<Part> { new Part { Text = GetSystemPrompt(playerName) } } },
                new Content { Role = "model", Parts = new List<Part> { new Part { Text = $"Understood. I am {_aiNameBuffer}. I will follow all instructions." } } }
            };

            _conversationCache[playerName] = newHistory;
            _conversationCacheLru.Add(playerName);

            Service.Log.Info($"No history found for '{playerName}'. Created a new conversation cache entry.");

            return newHistory;
        }

        private void InitializeConversation()
        {
            _conversationCache.Clear();
            _conversationCacheLru.Clear();
            _currentRpPartnerName = string.Empty;
            Service.Log.Info("All conversation histories have been reset.");
        }

        private void PrintMessageToChat(string message)
        {
            var seStringBuilder = new SeStringBuilder();
            var foreground = configuration.ForegroundColor;

            if (!configuration.UseCustomColors || configuration.ForegroundColor.W < 0.05f)
            {
                seStringBuilder.AddUiForeground($"{message}", 576);
            }
            else
            {
                seStringBuilder.Add(new Utils.ColorPayload(new Vector3(foreground.X, foreground.Y, foreground.Z)));
                seStringBuilder.AddText(message);
                seStringBuilder.Add(new Utils.ColorEndPayload());
            }

            Service.ChatGui.Print(seStringBuilder.Build());
        }

        private void PrintSystemMessage(string message)
        {
            var seStringBuilder = new SeStringBuilder();

            seStringBuilder.AddUiForeground($"{message}", 62);

            Service.ChatGui.Print(seStringBuilder.Build());
        }

        private void OpenChatWindow()
        {
            _drawChatWindow = true;
        }

        private void RevertTrackedMinion()
        {
            if (_glamouredMinionObjectId != 0)
            {
                var oldMinion = Service.ObjectTable.FirstOrDefault(o => o.GameObjectId == _glamouredMinionObjectId);
                if (oldMinion != null)
                {
                    _glamourerManager.Revert(oldMinion);
                }
                _glamouredMinionObjectId = 0;
                _lastAppliedDesignGuid = Guid.Empty;
            }
        }

        public void Dispose()
        {
            RevertTrackedMinion();

            _emoteMimickingManager?.Dispose();
            _minionNamingManager?.Dispose();

            ECommonsMain.Dispose();
            Service.CommandManager.RemoveHandler(commandName);
            Service.CommandManager.RemoveHandler("/aicfg");
            Service.CommandManager.RemoveHandler("/aiset");
            Service.CommandManager.RemoveHandler("/aichat");
            Service.CommandManager.RemoveHandler("/aiclear");
            Service.CommandManager.RemoveHandler("/aicontext");
            Service.CommandManager.RemoveHandler("/aisummon");
            Service.CommandManager.RemoveHandler("/aisearch");
            Service.CommandManager.RemoveHandler("/aithink");
            Service.CommandManager.RemoveHandler("/aifresh");
            Service.CommandManager.RemoveHandler("/aiooc");
            Service.CommandManager.RemoveHandler("/aidev");
            Service.CommandManager.RemoveHandler("/aihelp");
            Service.CommandManager.RemoveHandler("/ainormal");

            Service.PluginInterface.UiBuilder.Draw -= DrawConfiguration;
            Service.PluginInterface.UiBuilder.Draw -= DrawChatWindow;
            Service.PluginInterface.UiBuilder.Draw -= DrawAutoRpWindow;
            Service.PluginInterface.UiBuilder.OpenMainUi -= OpenChatWindow;
            Service.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;

            Service.Framework.Update -= OnFrameworkUpdate;

            Service.ClientState.Login -= OnLogin;
            Service.ClientState.Logout -= OnLogout;
            Service.ChatGui.ChatMessage -= OnChatMessage;

            _drawConfigWindow = false;
            _drawChatWindow = false;
            _drawAutoRpWindow = false;

            SaveCurrentSessionLog();

            Service.PluginInterface.UiBuilder.DisableAutomaticUiHide = false;
            Service.PluginInterface.UiBuilder.DisableCutsceneUiHide = false;
            Service.PluginInterface.UiBuilder.DisableGposeUiHide = false;
            Service.PluginInterface.UiBuilder.DisableUserUiHide = false;
        }
    }
}