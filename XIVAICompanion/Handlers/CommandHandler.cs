using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
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
                        _chatFreshMode = !_chatFreshMode;
                        PrintSystemMessage(_chatFreshMode
                            ? $"{_aiNameBuffer}>> Fresh mode is now enabled."
                            : $"{_aiNameBuffer}>> Fresh mode is now disabled.");
                    }
                    else if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!_chatFreshMode)
                        {
                            _chatFreshMode = true;
                            PrintSystemMessage($"{_aiNameBuffer}>> Fresh mode is now enabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> Fresh mode is already enabled.");
                        }
                    }
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_chatFreshMode)
                        {
                            _chatFreshMode = false;
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
                        _chatOocMode = !_chatOocMode;
                        PrintSystemMessage(_chatOocMode
                            ? $"{_aiNameBuffer}>> OOC mode is now enabled."
                            : $"{_aiNameBuffer}>> OOC mode is now disabled.");
                    }
                    else if (args.Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!_chatOocMode)
                        {
                            _chatOocMode = true;
                            PrintSystemMessage($"{_aiNameBuffer}>> OOC mode is now enabled.");
                        }
                        else
                        {
                            PrintSystemMessage($"{_aiNameBuffer}>> OOC mode is already enabled.");
                        }
                    }
                    else if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_chatOocMode)
                        {
                            _chatOocMode = false;
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
                    configuration.SearchMode = false;
                    configuration.ThinkMode = false;
                    configuration.Save();
                    _searchModeBuffer = false;
                    _thinkModeBuffer = false;
                    _chatFreshMode = false;
                    _chatOocMode = false;
                    PrintSystemMessage($"{_aiNameBuffer}>> Disabled all modes.");
                    break;

                case "/aimode":
                    string isSearchOn = _searchModeBuffer ? "On" : "Off";
                    string isThinkOn = _thinkModeBuffer ? "On" : "Off";
                    string isFreshOn = _chatFreshMode ? "On" : "Off";
                    string isOOCOn = _chatOocMode ? "On" : "Off";

                    PrintSystemMessage($"{_aiNameBuffer}>> Current mode status:");
                    PrintSystemMessage($"Web search mode: {isSearchOn}");
                    PrintSystemMessage($"Think mode: {isThinkOn}");
                    PrintSystemMessage($"Fresh mode: {isFreshOn}");
                    if (_isAutoRpRunning)
                    {
                        PrintSystemMessage($"OOC mode: {isOOCOn}");
                    }
                    break;

                case "/aialias":
                    PrintSystemMessage($"{_aiNameBuffer}>> Available aliases:");
                    PrintSystemMessage("nalodestone: Lodestone page for NA.");
                    PrintSystemMessage("eulodestone: Lodestone page for EU.");
                    PrintSystemMessage("frlodestone: Lodestone page for FR.");
                    PrintSystemMessage("delodestone: Lodestone page for DE.");
                    PrintSystemMessage("jplodestone: Lodestone page for JP.");
                    PrintSystemMessage("ffmaint: Lodestone maintenance page based on EU.");
                    PrintSystemMessage("mytime: Local time.");
                    PrintSystemMessage("mytimezone: Local time zone.");
                    break;

                case "/aihelp":
                    PrintSystemMessage("--- AI Companion Help ---");
                    PrintSystemMessage("/ai [prompt] - Sends a standard prompt to the AI.");
                    PrintSystemMessage("/aisearch [prompt] - Uses Web Search for information from the internet. Can also be toggled.");
                    PrintSystemMessage("/aithink [prompt] - Slower, more thoughtful responses for complex questions. Can also be toggled.");
                    PrintSystemMessage("/aifresh [prompt] - Ignores conversation history for a single, clean response. Can also be toggled.");
                    PrintSystemMessage("/aiooc [prompt] - Sends a private, Out-Of-Character prompt. Only available with Auto Role-Play. Can also be toggled.");
                    PrintSystemMessage("/ainormal - Turns off all modes.");
                    PrintSystemMessage("/aimode - Displays current modes status");
                    PrintSystemMessage("/aicfg - Opens the configuration window.");
                    PrintSystemMessage("/aiset <profile> - Changes the current AI persona to a saved profile.");
                    PrintSystemMessage("/aichat - Opens the dedicated chat window.");
                    PrintSystemMessage("/aihistory <on|off> - Enables or disables conversation history.");
                    PrintSystemMessage("/aiclear - Clears the current conversation history.");
                    PrintSystemMessage("/aicontext <on|off> - Enables or disables in-game context.");
                    PrintSystemMessage("/aisummon <profile> - Summon a custom NPC, configure in profile.");
                    PrintSystemMessage("/aialias - Lists all available aliases.");
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
                    _temperatureBuffer = persona.Temperature;
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

        private bool IsPetRenamerEnabled()
        {
            try
            {
                var versionIpc = Service.PluginInterface.GetIpcSubscriber<(uint, uint)>("PetRenamer.ApiVersion");
                var version = versionIpc.InvokeFunc();
                return true;
            }
            catch (Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError)
            {
                Service.Log.Debug("PetRenamer is not enabled");
                return false;
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Error checking PetRenamer availability");
                return false;
            }
        }

        private async Task SummonMinionFromProfile(string profileName)
        {
            LoadAvailablePersonas();
            var foundProfile = _personaFiles.FirstOrDefault(f => f.Equals(profileName, StringComparison.OrdinalIgnoreCase) && !f.Equals("<New Profile>", StringComparison.OrdinalIgnoreCase));
            bool minionIsActive = false;

            if (string.IsNullOrEmpty(foundProfile))
            {
                await Service.Framework.RunOnFrameworkThread(() => PrintSystemMessage($"{_aiNameBuffer}>> Profile '{profileName}' not found."));
                return;
            }

            if (profileName.Equals(configuration.AIName, StringComparison.OrdinalIgnoreCase))
            {
                var configuredMinion = configuration.MinionToReplace;

                if (!string.IsNullOrEmpty(configuredMinion))
                {
                    await Service.Framework.RunOnFrameworkThread(() =>
                    {
                        minionIsActive = GetMyMinion() != null;
                    });
                }

                if (minionIsActive)
                {
                    await Service.Framework.RunOnFrameworkThread(() =>
                    {
                        Service.Log.Info($"[Summon Command] Profile '{profileName}' and its minion '{configuredMinion}' are already active. Dismissing minion only.");
                        if (IsPetRenamerEnabled()) Chat.SendMessage($"/petname clear \"{configuredMinion}\"");
                        Chat.SendMessage($"/minion \"{configuredMinion}\"");
                    });
                    return;
                }
            }

            await Service.Framework.RunOnFrameworkThread(() =>
            {
                Service.Log.Info($"Summoning companion from profile '{profileName}'...");
            });

            await Service.Framework.RunOnFrameworkThread(() =>
            {
                minionIsActive = GetMyMinion() != null;
            });

            

            if (minionIsActive)
            {
                Service.Log.Info($"[Summon Command] Step 1/4: Active minion found. Dismissing it.");
                if (IsPetRenamerEnabled())
                {
                    IGameObject? currentMinion = null;
                    await Service.Framework.RunOnFrameworkThread(() =>
                    {
                        currentMinion = GetMyMinion();
                    });

                    if (currentMinion != null)
                    {
                        string currentMinionName = currentMinion.Name.TextValue;
                        await Service.Framework.RunOnFrameworkThread(() =>
                            Chat.SendMessage($"/petname clear \"{currentMinionName}\""));
                    }
                }
                await Task.Delay(500);
                await Service.Framework.RunOnFrameworkThread(() => Chat.SendMessage("/minion"));
                await Task.Delay(1000);
            }
            else
            {
                Service.Log.Info($"[Summon Command] Step 1/4: Active minion not found. Skipping dismissal.");
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

            await Task.Delay(1000);
            await Service.Framework.RunOnFrameworkThread(() =>
            {
                minionIsActive = GetMyMinion() != null;
            });

            var minionAiName = configuration.AIName;
            if (minionIsActive)
            {
                Service.Log.Info($"[Summon Command] Step 3/4: Minion '{minionToSummon}' is active, skip summoning.");
            }
            else
            {
                Service.Log.Info($"[Summon Command] Step 3/4: Summoning '{minionToSummon}'.");
                if (IsPetRenamerEnabled()) await Service.Framework.RunOnFrameworkThread(() => Chat.SendMessage($"/petname set \"{minionToSummon}\" \"{minionAiName}\""));
                await Service.Framework.RunOnFrameworkThread(() => Chat.SendMessage($"/minion \"{minionToSummon}\""));
            }

            await Task.Delay(500);

            Dalamud.Game.ClientState.Objects.Types.IGameObject? foundMinion = null;
            var attempts = 0;
            const int maxAttempts = 10;

            Service.Log.Info($"[Summon Command] Step 4/4: Waiting for '{minionToSummon}' to appear...");
            while (attempts < maxAttempts)
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    foundMinion = GetMyMinion();
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