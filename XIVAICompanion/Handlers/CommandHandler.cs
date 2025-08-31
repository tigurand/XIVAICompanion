using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.Automation;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XIVAICompanion.Configurations;

namespace XIVAICompanion
{
    public partial class AICompanionPlugin
    {
        private void OnCommand(string command, string args)
        {
            switch (command.ToLower())
            {
                case "/ai":
                    if (string.IsNullOrEmpty(configuration.ApiKey))
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Error: API key is not set. Please configure it in /aicfg.");
                        Service.Log.Warning("Plugin configuration issue: API key is not set. User was prompted to open config.");
                        OpenConfig();
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Error: No prompt provided. Use '/aihelp' for commands.");
                        return;
                    }
                    ProcessPrompt(args);
                    break;

                case "/aicfg":
                    OpenConfig();
                    break;

                case "/aiset":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Usage: /aiset <profile name>");

                        LoadAvailablePersonas();

                        var realProfiles = _personaFiles.Where(p => !p.Equals("<New Profile>", StringComparison.OrdinalIgnoreCase)).ToList();

                        if (realProfiles.Any())
                        {
                            if (realProfiles.Count == 1)
                            {
                                PrintSystemMessage("Available profile:");
                            }
                            else
                            {
                                PrintSystemMessage("Available profiles:");
                            }
                            foreach (var profile in realProfiles)
                            {
                                PrintSystemMessage($"{profile}");
                            }
                        }
                        else
                        {
                            PrintSystemMessage("No saved profiles found. You can create one in the config window (/aicfg).");
                        }
                        return;
                    }
                    SetProfile(args);
                    break;

                case "/aichat":
                    _drawChatWindow = true;
                    break;

                case "/aiclear":
                    InitializeConversation();
                    PrintSystemMessage($"{_aiNameBuffer}>> Conversation history has been cleared.");
                    break;

                case "/aihistory":
                    bool previousHistoryState = configuration.EnableConversationHistory;
                    if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                        configuration.EnableConversationHistory = true;
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                        configuration.EnableConversationHistory = false;
                    else
                        configuration.EnableConversationHistory = !configuration.EnableConversationHistory;

                    if (previousHistoryState != configuration.EnableConversationHistory)
                    {
                        configuration.Save();
                        _enableHistoryBuffer = configuration.EnableConversationHistory;
                        PrintSystemMessage(configuration.EnableConversationHistory
                            ? $"{_aiNameBuffer}>> Conversation history is now enabled."
                            : $"{_aiNameBuffer}>> Conversation history is now disabled.");
                    }
                    break;

                case "/aicontext":
                    bool previousContextState = configuration.EnableInGameContext;
                    if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                        configuration.EnableInGameContext = true;
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                        configuration.EnableInGameContext = false;
                    else
                        configuration.EnableInGameContext = !configuration.EnableInGameContext;

                    if (previousContextState != configuration.EnableInGameContext)
                    {
                        configuration.Save();
                        _enableInGameContextBuffer = configuration.EnableInGameContext;
                        PrintSystemMessage(configuration.EnableInGameContext
                            ? $"{_aiNameBuffer}>> In-game context is now enabled."
                            : $"{_aiNameBuffer}>> In-game context is now disabled.");
                    }
                    break;

                case "/aisummon":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Usage: /aisummon <profile name>");

                        LoadAvailablePersonas();
                        var summonableProfiles = new List<string>();

                        foreach (var profileName in _personaFiles.Where(p => !p.Equals("<New Profile>", StringComparison.OrdinalIgnoreCase)))
                        {
                            var filePath = Path.Combine(_personaFolder.FullName, profileName + ".json");
                            if (!File.Exists(filePath)) continue;

                            try
                            {
                                var json = File.ReadAllText(filePath);
                                var persona = JsonConvert.DeserializeObject<PersonaConfiguration>(json);

                                if (persona != null && !string.IsNullOrWhiteSpace(persona.MinionToReplace))
                                {
                                    summonableProfiles.Add(profileName);
                                }
                            }
                            catch (Exception ex)
                            {
                                Service.Log.Error(ex, $"Failed to read or parse persona file: {profileName}");
                            }
                        }

                        if (summonableProfiles.Any())
                        {
                            if (summonableProfiles.Count == 1)
                            {
                                PrintSystemMessage("Available summonable profile:");
                            }
                            else
                            {
                                PrintSystemMessage("Available summonable profiles:");
                            }
                            foreach (var profile in summonableProfiles)
                            {
                                PrintSystemMessage($"{profile}");
                            }
                        }
                        else
                        {
                            PrintSystemMessage("No profiles with a configured companion found.");
                        }
                        return;
                    }
                    Task.Run(() => SummonMinionFromProfile(args));
                    break;

                case "/aisearch":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        if (!configuration.SearchMode)
                        {
                            if (configuration.ThinkMode)
                            {
                                configuration.ThinkMode = false;
                                _thinkModeBuffer = false;
                            }
                            configuration.SearchMode = true;
                            _searchModeBuffer = true;
                            PrintSystemMessage($"{_aiNameBuffer}>> Search mode is now enabled.");
                        }
                        else
                        {
                            configuration.SearchMode = false;
                            _searchModeBuffer = false;
                            PrintSystemMessage($"{_aiNameBuffer}>> Search mode is now disabled.");
                        }
                        configuration.Save();
                    }
                    else if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!configuration.SearchMode)
                        {
                            if (configuration.ThinkMode)
                            {
                                configuration.ThinkMode = false;
                                _thinkModeBuffer = false;
                            }
                            configuration.SearchMode = true;
                            _searchModeBuffer = true;
                            configuration.Save();
                            PrintSystemMessage($"{_aiNameBuffer}>> Search mode is now enabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Search mode is already enabled.");
                        }
                    }
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        if (configuration.SearchMode)
                        {
                            configuration.SearchMode = false;
                            _searchModeBuffer = false;
                            configuration.Save();
                            PrintSystemMessage($"{_aiNameBuffer}>> Search mode is now disabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Search mode is already disabled.");
                        }
                    }
                    else
                    {
                        _tempSearchMode = true;
                        ProcessPrompt(args);
                    }
                    break;

                case "/aithink":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        if (!configuration.ThinkMode)
                        {
                            if (configuration.SearchMode)
                            {
                                configuration.SearchMode = false;
                                _searchModeBuffer = false;
                            }
                            configuration.ThinkMode = true;
                            _thinkModeBuffer = true;
                            PrintSystemMessage($"{_aiNameBuffer}>> Think mode is now enabled.");
                        }
                        else
                        {
                            configuration.ThinkMode = false;
                            _thinkModeBuffer = false;
                            PrintSystemMessage($"{_aiNameBuffer}>> Think mode is now disabled.");
                        }
                        configuration.Save();
                    }
                    else if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!configuration.ThinkMode)
                        {
                            if (configuration.SearchMode)
                            {
                                configuration.SearchMode = false;
                                _searchModeBuffer = false;
                            }
                            configuration.ThinkMode = true;
                            _thinkModeBuffer = true;
                            configuration.Save();
                            PrintSystemMessage($"{_aiNameBuffer}>> Think mode is now enabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Think mode is already enabled.");
                        }
                    }
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        if (configuration.ThinkMode)
                        {
                            configuration.ThinkMode = false;
                            _thinkModeBuffer = false;
                            configuration.Save();
                            PrintSystemMessage($"{_aiNameBuffer}>> Think mode is now disabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Think mode is already disabled.");
                        }
                    }
                    else
                    {
                        _tempThinkMode = true;
                        ProcessPrompt(args);
                    }
                    break;

                case "/aifresh":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        configuration.FreshMode = !configuration.FreshMode;
                        configuration.Save();
                        _freshModeBuffer = configuration.FreshMode;
                        PrintSystemMessage(configuration.FreshMode
                            ? $"{_aiNameBuffer}>> Fresh mode is now enabled."
                            : $"{_aiNameBuffer}>> Fresh mode is now disabled.");
                    }
                    else if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!configuration.FreshMode)
                        {
                            configuration.FreshMode = true;
                            configuration.Save();
                            _freshModeBuffer = true;
                            PrintSystemMessage($"{_aiNameBuffer}>> Fresh mode is now enabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Fresh mode is already enabled.");
                        }
                    }
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        if (configuration.FreshMode)
                        {
                            configuration.FreshMode = false;
                            configuration.Save();
                            _freshModeBuffer = false;
                            PrintSystemMessage($"{_aiNameBuffer}>> Fresh mode is now disabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Fresh mode is already disabled.");
                        }
                    }
                    else
                    {
                        _tempFreshMode = true;
                        ProcessPrompt(args);
                    }
                    break;

                case "/aiooc":
                    if (!_isAutoRpRunning)
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> OOC mode can only be used when Auto Role-Play is enabled.");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(args))
                    {
                        configuration.OocMode = !configuration.OocMode;
                        configuration.Save();
                        _oocModeBuffer = configuration.OocMode;
                        PrintSystemMessage(configuration.OocMode
                            ? $"{_aiNameBuffer}>> OOC mode is now enabled."
                            : $"{_aiNameBuffer}>> OOC mode is now disabled.");
                    }
                    else if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!configuration.OocMode)
                        {
                            configuration.OocMode = true;
                            configuration.Save();
                            _oocModeBuffer = true;
                            PrintSystemMessage($"{_aiNameBuffer}>> OOC mode is now enabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> OOC mode is already enabled.");
                        }
                    }
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        if (configuration.OocMode)
                        {
                            configuration.OocMode = false;
                            configuration.Save();
                            _oocModeBuffer = false;
                            PrintSystemMessage($"{_aiNameBuffer}>> OOC mode is now disabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> OOC mode is already disabled.");
                        }
                    }
                    else
                    {
                        _tempOocMode = true;
                        ProcessPrompt(args);
                    }
                    break;

                case "/aidev":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        _isDevModeEnabled = !_isDevModeEnabled;
                        configuration.IsDevModeEnabled = _isDevModeEnabled;
                        configuration.Save();
                        PrintSystemMessage($"{_aiNameBuffer}>> Developer mode has been {(_isDevModeEnabled ? "ENABLED" : "DISABLED")}.");
                    }
                    else if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!_isDevModeEnabled)
                        {
                            _isDevModeEnabled = true;
                            configuration.IsDevModeEnabled = true;
                            configuration.Save();
                            PrintSystemMessage($"{_aiNameBuffer}>> Developer mode has been ENABLED.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Developer mode is already ENABLED.");
                        }
                    }
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_isDevModeEnabled)
                        {
                            _isDevModeEnabled = false;
                            configuration.IsDevModeEnabled = false;
                            configuration.Save();
                            PrintSystemMessage($"{_aiNameBuffer}>> Developer mode has been DISABLED.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Developer mode is already DISABLED.");
                        }
                    }
                    else
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Invalid argument. Use '/aidev', '/aidev on', or '/aidev off'.");
                        return;
                    }

                    Service.PluginInterface.UiBuilder.DisableAutomaticUiHide = _isDevModeEnabled;
                    Service.PluginInterface.UiBuilder.DisableCutsceneUiHide = _isDevModeEnabled;
                    Service.PluginInterface.UiBuilder.DisableGposeUiHide = _isDevModeEnabled;
                    Service.PluginInterface.UiBuilder.DisableUserUiHide = _isDevModeEnabled;
                    break;

                case "/ainormal":
                    if (configuration.SearchMode || configuration.ThinkMode)
                    {
                        configuration.SearchMode = false;
                        configuration.ThinkMode = false;
                        configuration.Save();
                        _searchModeBuffer = false;
                        _thinkModeBuffer = false;
                        PrintSystemMessage($"{_aiNameBuffer}>> Normal mode is now enabled.");
                    }
                    else
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Already in normal mode.");
                    }
                    break;

                case "/aihelp":
                    PrintSystemMessage("--- AI Companion Help ---");
                    PrintSystemMessage("/ai [prompt] - Sends a standard prompt to the AI.");
                    PrintSystemMessage("/aisearch [prompt] - Uses Web Search for information from the internet. Can also be toggled.");
                    PrintSystemMessage("/aithink [prompt] - Slower, more thoughtful responses for complex questions. Can also be toggled.");
                    PrintSystemMessage("/aifresh [prompt] - Ignores conversation history for a single, clean response. Can also be toggled.");
                    PrintSystemMessage("/aiooc [prompt] - Sends a private, Out-Of-Character prompt. Only available with Auto Role-Play. Can also be toggled.");
                    PrintSystemMessage("/ainormal - Turns off both search and think modes.");
                    PrintSystemMessage("/aicfg - Opens the configuration window.");
                    PrintSystemMessage("/aiset <profile> - Changes the current AI persona to a saved profile.");
                    PrintSystemMessage("/aichat - Opens the dedicated chat window.");
                    PrintSystemMessage("/aihistory <on|off> - Enables or disables conversation history.");
                    PrintSystemMessage("/aiclear - Clears the current conversation history.");
                    PrintSystemMessage("/aicontext <on|off> - Enables or disables in-game context.");
                    PrintSystemMessage("/aisummon <profile> - Summon a custom NPC, configure in profile.");
                    break;

                default:
                    if (command.ToLower() == "/ai")
                    {
                        if (string.IsNullOrEmpty(configuration.ApiKey))
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Error: API key is not set. Please configure it in /aicfg.");
                            Service.Log.Warning("Plugin configuration issue: API key is not set. User was prompted to open config.");
                            OpenConfig();
                            return;
                        }
                        if (string.IsNullOrWhiteSpace(args))
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Error: No prompt provided. Use '/aihelp' for commands.");
                            return;
                        }
                        ProcessPrompt(args);
                    }
                    else
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Unknown command. Use '/aihelp' for available commands.");
                    }
                    break;
            }
        }

        private void SetProfile(string profileName)
        {
            LoadAvailablePersonas();

            var foundProfile = _personaFiles.FirstOrDefault(f => f.Equals(profileName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(foundProfile) || foundProfile.Equals("<New Profile>", StringComparison.OrdinalIgnoreCase))
            {
                PrintSystemMessage($"{_aiNameBuffer}>> Profile '{profileName}' not found.");
                return;
            }

            var filePath = Path.Combine(_personaFolder.FullName, foundProfile + ".json");
            if (!File.Exists(filePath))
            {
                PrintSystemMessage($"{_aiNameBuffer}>> Error: Profile file for '{foundProfile}' does not exist.");
                return;
            }

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

                    SaveChanges();
                    UpdateGlamourerDesigns();

                    PrintSystemMessage($"{_aiNameBuffer}>> Profile successfully changed to '{foundProfile}'.");
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, $"Failed to load persona file via command: {profileName}");
                PrintSystemMessage($"{_aiNameBuffer}>> Error: Failed to load profile '{profileName}'. See /xllog for details.");
            }
        }

        private async Task SummonMinionFromProfile(string profileName)
        {
            LoadAvailablePersonas();
            var foundProfile = _personaFiles.FirstOrDefault(f => f.Equals(profileName, StringComparison.OrdinalIgnoreCase) && !f.Equals("<New Profile>", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(foundProfile))
            {
                await Service.Framework.RunOnFrameworkThread(() => PrintSystemMessage($"{_aiNameBuffer}>> Profile '{profileName}' not found."));
                return;
            }

            if (profileName.Equals(configuration.AIName, StringComparison.OrdinalIgnoreCase))
            {
                var configuredMinion = configuration.MinionToReplace;
                bool minionIsActive = false;

                if (!string.IsNullOrEmpty(configuredMinion))
                {
                    await Service.Framework.RunOnFrameworkThread(() =>
                    {
                        minionIsActive = Service.ObjectTable.Any(o => o.ObjectKind == ObjectKind.Companion && o.Name.TextValue.Contains(configuredMinion));
                    });
                }

                if (minionIsActive)
                {
                    await Service.Framework.RunOnFrameworkThread(() =>
                    {
                        Service.Log.Info($"[Summon Command] Profile '{profileName}' and its minion '{configuredMinion}' are already active. Dismissing minion only.");
                        Chat.SendMessage("/minion");
                    });
                    return;
                }
            }

            await Service.Framework.RunOnFrameworkThread(() =>
            {
                Service.Log.Info($"Summoning companion from profile '{profileName}'...");
            });

            bool isMinionSummoned = false;
            await Service.Framework.RunOnFrameworkThread(() =>
            {
                isMinionSummoned = Service.ObjectTable.Any(o => o.ObjectKind == ObjectKind.Companion);
            });

            if (isMinionSummoned)
            {
                Service.Log.Info($"[Summon Command] Step 1/4: Active minion found. Dismissing it.");
                await Service.Framework.RunOnFrameworkThread(() => Chat.SendMessage("/minion"));
                await Task.Delay(500);
            }
            else
            {
                Service.Log.Info($"[Summon Command] Step 1/4: No minion summoned. Skipping dismissal.");
            }

            Service.Log.Info($"[Summon Command] Step 2/4: Applying profile '{profileName}'.");
            await Service.Framework.RunOnFrameworkThread(() => SetProfile(profileName));
            await Task.Delay(500);

            var minionToSummon = configuration.MinionToReplace;
            if (string.IsNullOrWhiteSpace(minionToSummon))
            {
                await Service.Framework.RunOnFrameworkThread(() => PrintSystemMessage($"{_aiNameBuffer}>> Profile '{profileName}' does not have a minion configured."));
                return;
            }

            Service.Log.Info($"[Summon Command] Step 3/4: Summoning '{minionToSummon}'.");
            await Service.Framework.RunOnFrameworkThread(() => Chat.SendMessage($"/minion \"{minionToSummon}\""));

            await Task.Delay(500);

            Dalamud.Game.ClientState.Objects.Types.IGameObject? foundMinion = null;
            var attempts = 0;
            const int maxAttempts = 10;

            Service.Log.Info($"[Summon Command] Step 4/4: Waiting for '{minionToSummon}' to appear...");
            while (attempts < maxAttempts)
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    foundMinion = Service.ObjectTable.FirstOrDefault(o => o.ObjectKind == ObjectKind.Companion && o.Name.TextValue.Contains(minionToSummon));
                });

                if (foundMinion != null)
                {
                    Service.Log.Info($"[Summon Command] Minion found after {attempts * 500}ms.");
                    break;
                }

                attempts++;
                await Task.Delay(500);
            }

            if (foundMinion != null)
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    Service.Log.Info($"[Summon Command] Applying glamour design to found minion.");
                    if (Guid.TryParse(configuration.NpcGlamourerDesignGuid, out var designToApply) && designToApply != Guid.Empty)
                    {
                        _glamourerManager.ApplyDesign(designToApply, foundMinion);

                        _glamouredMinionObjectId = foundMinion.GameObjectId;
                        _lastAppliedDesignGuid = designToApply;
                        Service.Log.Info($"{_aiNameBuffer}>> Summon complete.");
                    }
                    else
                    {
                        PrintSystemMessage($"{_aiNameBuffer}>> Error: Could not find a valid glamour design in profile '{profileName}'.");
                    }
                });
            }
            else
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    Service.Log.Error($"[Summon Command] Failed to find summoned minion '{minionToSummon}' after {maxAttempts * 500}ms.");
                    PrintSystemMessage($"{_aiNameBuffer}>> Error: Failed to find summoned minion in time. The glamour may not have applied correctly.");
                });
            }
        }
    }
}