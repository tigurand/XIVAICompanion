using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Utility;
using ECommons;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using XIVAICompanion.Configurations;
using XIVAICompanion.Models;
using XIVAICompanion.Providers;
using XIVAICompanion.Utils;

namespace XIVAICompanion
{
    public partial class AICompanionPlugin
    {
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

            _glamourerDesignFilter = string.Empty;
            _personaFileFilter = string.Empty;

            _drawConfigWindow = true;
            _glamourerManager.RecheckApiAvailability();
            UpdateGlamourerDesigns();
        }

        private void UpdateGlamourerDesigns()
        {
            if (!_glamourerManager.IsApiAvailable)
            {
                _glamourerDesigns = new List<string> { "Glamourer not available" };
                _selectedGlamourerDesignIndex = 0;
                return;
            }

            var designs = _glamourerManager.GetDesigns();
            _glamourerDesigns = designs.Values.OrderBy(name => name).ToList();
            _glamourerDesigns.Insert(0, "<None>");

            if (!string.IsNullOrEmpty(_npcGlamourerDesignGuidBuffer) && Guid.TryParse(_npcGlamourerDesignGuidBuffer, out var currentGuid))
            {
                if (designs.TryGetValue(currentGuid, out var currentName))
                {
                    _selectedGlamourerDesignIndex = _glamourerDesigns.IndexOf(currentName);
                }
                else
                {
                    _selectedGlamourerDesignIndex = 0;
                }
            }
            else
            {
                _selectedGlamourerDesignIndex = 0;
            }
        }

        private void DrawConfiguration()
        {
            if (!_drawConfigWindow) return;

            bool wasVisible = _drawConfigWindow;

            ImGui.SetWindowSize($"{Name} | Configuration", new Vector2(400, 300), ImGuiCond.Appearing);
            ImGui.Begin($"{Name} | Configuration", ref _drawConfigWindow, ImGuiWindowFlags.AlwaysAutoResize);

            if (UIHelper.AddHeaderIcon(Service.PluginInterface, "autorp_button", FontAwesomeIcon.Heart, out var kofiPressed, new UIHelper.HeaderIconOptions { Tooltip = "Support on Ko-fi" }) && kofiPressed)
            {
                GenericHelpers.ShellStart("https://ko-fi.com/lucillebagul");
            }

            if (UIHelper.AddHeaderIcon(Service.PluginInterface, "autorp_button", FontAwesomeIcon.TheaterMasks, out var openAutoRpPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Auto Role-Play Window" }) && openAutoRpPressed)
            {
                _drawAutoRpWindow = true;
            }

            if (UIHelper.AddHeaderIcon(Service.PluginInterface, "chat_button", FontAwesomeIcon.Comment, out var openChatPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Chat Window" }) && openChatPressed)
            {
                OpenChatWindow();
            }

            if (ImGui.BeginTabBar("ConfigTabs"))
            {
                if (ImGui.BeginTabItem("AI Provider"))
                {
                    ImGui.Text("Model Profiles Management");
                    
                    var profileNames = configuration.ModelProfiles.Select(p => p.ProfileName).ToList();
                    profileNames.Insert(0, "<New Profile>");

                    if (_selectedProfileIndex + 1 >= profileNames.Count) _selectedProfileIndex = -1;

                    string currentProfileName = _selectedProfileIndex == -1 ? "<New Profile>" : profileNames[_selectedProfileIndex + 1];

                    ImGui.SetNextItemWidth(610);
                    if (ImGui.BeginCombo("##ModelProfileCombo", currentProfileName))
                    {
                        for (int i = 0; i < profileNames.Count; i++)
                        {
                            bool isSelected = (i - 1) == _selectedProfileIndex;
                            if (ImGui.Selectable(profileNames[i], isSelected))
                            {
                                _selectedProfileIndex = i - 1;
                                 if (_selectedProfileIndex == -1)
                                 {
                                     _profileNameBuffer = "New Profile";
                                     _profileProviderBuffer = AiProviderType.Gemini;
                                     _profileBaseUrlBuffer = string.Empty;
                                     _profileApiKeyBuffer = string.Empty;
                                     _profileModelIdBuffer = string.Empty;
                                     _profileMaxTokensBuffer = 1024;
                                     _profileUseTavilyInsteadBuffer = false;
                                     _profileTavilyApiKeyBuffer = string.Empty;
                                     _profileUseAsFallbackBuffer = true;
                                 }
                                 else
                                 {
                                     var profile = configuration.ModelProfiles[_selectedProfileIndex];
                                     _profileNameBuffer = profile.ProfileName;
                                     _profileProviderBuffer = profile.ProviderType;
                                     _profileBaseUrlBuffer = profile.BaseUrl;
                                     _profileApiKeyBuffer = profile.ApiKey;
                                     _profileModelIdBuffer = profile.ModelId;
                                     _profileMaxTokensBuffer = profile.MaxTokens;
                                     _profileUseTavilyInsteadBuffer = profile.UseTavilyInstead;
                                     _profileTavilyApiKeyBuffer = profile.TavilyApiKey;
                                     _profileUseAsFallbackBuffer = profile.UseAsFallback;
                                 }
                             }
                         }
                         ImGui.EndCombo();
                     }

                    ImGui.Text("Profile Name:");
                    ImGui.SetNextItemWidth(610);
                    ImGui.InputText("##profileName", ref _profileNameBuffer, 64);

                    ImGui.Text("Provider Type:");
                    ImGui.SetNextItemWidth(610);
                    if (ImGui.BeginCombo("##ProfileProviderCombo", _profileProviderBuffer.ToString()))
                    {
                        foreach (AiProviderType provider in Enum.GetValues(typeof(AiProviderType)))
                        {
                            if (ImGui.Selectable(provider.ToString(), _profileProviderBuffer == provider))
                            {
                                _profileProviderBuffer = provider;
                            }
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.Text("AI API Key:");
                    ImGui.SetNextItemWidth(610);
                    ImGui.InputText("##profileApiKey", ref _profileApiKeyBuffer, 256, ImGuiInputTextFlags.Password);

                    if (_profileProviderBuffer == AiProviderType.OpenAiCompatible)
                    {
                        ImGui.Text("Base URL:");
                        ImGui.SetNextItemWidth(610);
                        ImGui.InputText("##profileBaseUrl", ref _profileBaseUrlBuffer, 256);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("e.g., https://api.openai.com/v1");
                    }

                        ImGui.Text("Model ID:");
                    ImGui.SetNextItemWidth(610);
                    ImGui.InputText("##profileModelId", ref _profileModelIdBuffer, 128);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("e.g., gemini-2.5-flash or gpt-4o");

                    ImGui.Text("Max Tokens:");
                    ImGui.SetNextItemWidth(610);
                    if (ImGui.SliderInt("##profileMaxTokens", ref _profileMaxTokensBuffer, minResponseTokens, maxResponseTokens))
                    {
                        _profileMaxTokensBuffer = Math.Clamp(_profileMaxTokensBuffer, minResponseTokens, maxResponseTokens);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Controls the maximum length of the response for this model.\n" +
                                         "Also controls thinking budget on Think Mode (Gemini).");
                    }

                    if (_profileProviderBuffer == AiProviderType.Gemini)
                    {
                        ImGui.Checkbox("Use Tavily instead of Google?", ref _profileUseTavilyInsteadBuffer);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("If checked, Tavily will be used for web search instead of Gemini's built-in Google Search.");
                        
                        ImGui.BeginDisabled(!_profileUseTavilyInsteadBuffer);
                        ImGui.Text("Tavily API Key:");
                        ImGui.SetNextItemWidth(610);
                        ImGui.InputText("##profileTavilyApiKeyGemini", ref _profileTavilyApiKeyBuffer, 256, ImGuiInputTextFlags.Password);
                        ImGui.EndDisabled();
                    }
                    else if (_profileProviderBuffer == AiProviderType.OpenAiCompatible)
                    {
                        ImGui.Spacing();
                        ImGui.Text("Tavily API Key (For Web Search):");
                        ImGui.SetNextItemWidth(610);
                        ImGui.InputText("##profileTavilyApiKeyOpenAi", ref _profileTavilyApiKeyBuffer, 256, ImGuiInputTextFlags.Password);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Optional. If provided, the model can use Tavily to search the web.\nMay work or not depending on the model.");
                    }

                    ImGui.Spacing();
                    if (ImGui.Button("Test Connection"))
                    {
                        Task.Run(async () =>
                        {
                            PrintSystemMessage(">> Testing connection...");
                             ModelProfile testProfile = new ModelProfile
                             {
                                 ProfileName = _profileNameBuffer,
                                 ProviderType = _profileProviderBuffer,
                                 BaseUrl = _profileBaseUrlBuffer,
                                 ApiKey = _profileApiKeyBuffer,
                                 ModelId = _profileModelIdBuffer,
                                 MaxTokens = _profileMaxTokensBuffer,
                                 UseTavilyInstead = _profileUseTavilyInsteadBuffer,
                                 TavilyApiKey = _profileTavilyApiKeyBuffer,
                                 UseAsFallback = _profileUseAsFallbackBuffer
                             };

                            if (string.IsNullOrEmpty(testProfile.ModelId))
                            {
                                PrintSystemMessage(">> Connection Failed: Model ID is empty.");
                                return;
                            }

                            var testProvider = testProfile.ProviderType == AiProviderType.Gemini
                                ? (IAiProvider)new GeminiProvider(httpClient)
                                : (IAiProvider)new OpenAiProvider(httpClient);

                            var testRequest = new ProviderRequest
                            {
                                Model = testProfile.ModelId,
                                SystemPrompt = "You are a test assistant.",
                                ConversationHistory = new List<Content> { new Content { Role = "user", Parts = new List<Part> { new Part { Text = "Say 'Connection Successful!'" } } } },
                                MaxTokens = testProfile.MaxTokens,
                                Temperature = 0.7,
                                UseWebSearch = false
                            };

                            var result = await testProvider.SendPromptAsync(testRequest, testProfile);
                            if (result.WasSuccessful)
                            {
                                PrintSystemMessage($">> Connection Successful! Response: {result.ResponseText}");
                            }
                            else
                            {
                                PrintSystemMessage($">> Connection Failed: {result.Exception?.Message ?? result.HttpResponse?.ReasonPhrase ?? "Unknown Error"}");
                                if (!string.IsNullOrEmpty(result.RawResponse))
                                {
                                    Service.Log.Error($">> Raw Response: {result.RawResponse}");
                                }
                            }
                        });
                     }

                     ImGui.Checkbox("Use as fallback model?", ref _profileUseAsFallbackBuffer);

                     ImGui.Spacing();
                     if (ImGui.Button("Save Profile##ModelProfile"))
                     {
                         if (!string.IsNullOrWhiteSpace(_profileNameBuffer))
                         {
                             var newProfile = new ModelProfile
                             {
                                 ProfileName = _profileNameBuffer,
                                 ProviderType = _profileProviderBuffer,
                                 BaseUrl = _profileBaseUrlBuffer,
                                 ApiKey = _profileApiKeyBuffer,
                                 ModelId = _profileModelIdBuffer,
                                 MaxTokens = _profileMaxTokensBuffer,
                                 UseTavilyInstead = _profileUseTavilyInsteadBuffer,
                                 TavilyApiKey = _profileTavilyApiKeyBuffer,
                                 UseAsFallback = _profileUseAsFallbackBuffer
                             };

                            if (_selectedProfileIndex == -1)
                            {
                                configuration.ModelProfiles.Add(newProfile);
                                _selectedProfileIndex = configuration.ModelProfiles.Count - 1;
                            }
                            else
                            {
                                configuration.ModelProfiles[_selectedProfileIndex] = newProfile;
                            }
                            configuration.Save();
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Duplicate Profile##ModelProfile"))
                    {
                        if (_selectedProfileIndex != -1)
                        {
                         var profileToCopy = configuration.ModelProfiles[_selectedProfileIndex];
                         var newProfile = new ModelProfile
                         {
                             ProfileName = profileToCopy.ProfileName + " - Copy",
                             ProviderType = profileToCopy.ProviderType,
                             BaseUrl = profileToCopy.BaseUrl,
                             ApiKey = profileToCopy.ApiKey,
                             ModelId = profileToCopy.ModelId,
                             MaxTokens = profileToCopy.MaxTokens,
                             UseTavilyInstead = profileToCopy.UseTavilyInstead,
                             TavilyApiKey = profileToCopy.TavilyApiKey,
                             UseAsFallback = profileToCopy.UseAsFallback
                         };
                         configuration.ModelProfiles.Add(newProfile);
                         _selectedProfileIndex = configuration.ModelProfiles.Count - 1;
                         
                         _profileNameBuffer = newProfile.ProfileName;
                         _profileProviderBuffer = newProfile.ProviderType;
                         _profileBaseUrlBuffer = newProfile.BaseUrl;
                         _profileApiKeyBuffer = newProfile.ApiKey;
                         _profileModelIdBuffer = newProfile.ModelId;
                         _profileMaxTokensBuffer = newProfile.MaxTokens;
                         _profileUseTavilyInsteadBuffer = newProfile.UseTavilyInstead;
                         _profileTavilyApiKeyBuffer = newProfile.TavilyApiKey;
                         _profileUseAsFallbackBuffer = newProfile.UseAsFallback;
                         
                         configuration.Save();
                     }
                 }
                    ImGui.SameLine();
                    if (ImGui.Button("Delete Profile##ModelProfile"))
                    {
                        if (_selectedProfileIndex != -1)
                        {
                            configuration.ModelProfiles.RemoveAt(_selectedProfileIndex);
                            _selectedProfileIndex = -1;
                            _profileNameBuffer = "New Profile";
                            _profileProviderBuffer = AiProviderType.Gemini;
                            _profileBaseUrlBuffer = string.Empty;
                            _profileApiKeyBuffer = string.Empty;
                            _profileModelIdBuffer = string.Empty;
                            _profileMaxTokensBuffer = 1024;
                            _profileUseTavilyInsteadBuffer = false;
                            _profileTavilyApiKeyBuffer = string.Empty;
                            configuration.Save();
                        }
                    }

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Text("Model Assignments");

                    string GetProfileName(int index) => (index >= 0 && index < configuration.ModelProfiles.Count) ? configuration.ModelProfiles[index].ProfileName : "None";

                    ImGui.Text("Default Model:");
                    ImGui.SetNextItemWidth(610);
                    if (ImGui.BeginCombo("##DefaultModelCombo", GetProfileName(_defaultModelProfileIndexBuffer)))
                    {
                        if (ImGui.Selectable("None", _defaultModelProfileIndexBuffer == -1)) _defaultModelProfileIndexBuffer = -1;
                        for (int i = 0; i < configuration.ModelProfiles.Count; i++)
                        {
                            if (ImGui.Selectable(configuration.ModelProfiles[i].ProfileName, _defaultModelProfileIndexBuffer == i))
                                _defaultModelProfileIndexBuffer = i;
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.Text("Thinking Model:");
                    ImGui.SetNextItemWidth(610);
                    if (ImGui.BeginCombo("##ThinkingModelCombo", GetProfileName(_thinkingModelProfileIndexBuffer)))
                    {
                        if (ImGui.Selectable("None", _thinkingModelProfileIndexBuffer == -1)) _thinkingModelProfileIndexBuffer = -1;
                        for (int i = 0; i < configuration.ModelProfiles.Count; i++)
                        {
                            if (ImGui.Selectable(configuration.ModelProfiles[i].ProfileName, _thinkingModelProfileIndexBuffer == i))
                                _thinkingModelProfileIndexBuffer = i;
                        }
                        ImGui.EndCombo();
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Choose model with reasoning ability.");

                    ImGui.Text("Greeting Model:");
                    ImGui.SetNextItemWidth(610);
                    if (ImGui.BeginCombo("##GreetingModelCombo", GetProfileName(_greetingModelProfileIndexBuffer)))
                    {
                        if (ImGui.Selectable("None", _greetingModelProfileIndexBuffer == -1)) _greetingModelProfileIndexBuffer = -1;
                        for (int i = 0; i < configuration.ModelProfiles.Count; i++)
                        {
                            if (ImGui.Selectable(configuration.ModelProfiles[i].ProfileName, _greetingModelProfileIndexBuffer == i))
                                _greetingModelProfileIndexBuffer = i;
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("AI Persona"))
                {
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
                        ImGui.SetNextItemWidth(195);
                        ImGui.InputText("##customname", ref _customUserNameBuffer, 32);
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("You can also additionally use System Prompt to override this.\n" +
                                             "Example: Don't call me by my real name. Address me as Warrior of Light instead of my real name.");
                        }
                    }

                    ImGui.Spacing();

                    ImGui.Text("System Prompt (Persona):");
                    ImGui.InputTextMultiline("##systemprompt", ref _systemPromptBuffer, 8192, new System.Numerics.Vector2(810, 250));

                    ImGui.Spacing();
                    if (ImGui.SliderFloat("Temperature", ref _temperatureBuffer, 0.0f, 2.0f, "%.2f"))
                    {
                        _temperatureBuffer = Math.Clamp(_temperatureBuffer, 0.0f, 2.0f);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Controls the randomness/creativity of AI responses.\n" +
                                         "0.0 = Very focused and deterministic\n" +
                                         "1.0 = Balanced (default)\n" +
                                         "2.0 = Very creative and unpredictable");
                    }

                    ImGui.Spacing();

                    ImGui.Text("Custom NPC Companion Settings:");

                    ImGui.SetNextItemWidth(350);
                    ImGui.InputText("Minion to Replace", ref _minionToReplaceBuffer, 64);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("The name of the minion you want to turn into your NPC companion.\nMust be spelled exactly right (e.g., 'The Lawnblazer').\nLeave empty to disable.");

                    ImGui.SetNextItemWidth(350);

                    if (_glamourerManager.IsApiAvailable)
                    {
                        string currentDesignName = "";
                        if (_selectedGlamourerDesignIndex >= 0 && _selectedGlamourerDesignIndex < _glamourerDesigns.Count)
                        {
                            currentDesignName = _glamourerDesigns[_selectedGlamourerDesignIndex];
                        }

                        ImGui.Text("Glamourer Design");
                        ImGui.SetNextItemWidth(350);

                        if (ImGui.BeginCombo("##GlamourerDesignCombo", string.IsNullOrEmpty(_glamourerDesignFilter) ? currentDesignName : _glamourerDesignFilter))
                        {
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.InputTextWithHint("##GlamourerSearch", "Type to search...", ref _glamourerDesignFilter, 256))
                            {
                                // Filter updated, no need to do anything special here
                            }

                            var filteredDesigns = GetFilteredGlamourerDesigns();

                            foreach (var design in filteredDesigns)
                            {
                                var isSelected = design == currentDesignName;
                                if (ImGui.Selectable(design, isSelected))
                                {
                                    _selectedGlamourerDesignIndex = _glamourerDesigns.IndexOf(design);
                                    _glamourerDesignFilter = "";

                                    if (_selectedGlamourerDesignIndex > 0 && _selectedGlamourerDesignIndex < _glamourerDesigns.Count)
                                    {
                                        var designs = _glamourerManager.GetDesigns();
                                        var selectedName = _glamourerDesigns[_selectedGlamourerDesignIndex];
                                        var designEntry = designs.FirstOrDefault(kvp => kvp.Value == selectedName);
                                        _npcGlamourerDesignGuidBuffer = designEntry.Key.ToString();
                                    }
                                    else
                                    {
                                        _npcGlamourerDesignGuidBuffer = Guid.Empty.ToString();
                                    }
                                }

                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }

                            ImGui.EndCombo();
                        }

                        if (ImGui.IsItemClicked())
                        {
                            _glamourerManager.RecheckApiAvailability();
                            UpdateGlamourerDesigns();
                        }
                    }
                    else
                    {
                        ImGui.Text("Glamourer Design");
                        ImGui.SetNextItemWidth(350);
                        ImGui.BeginDisabled();
                        ImGui.BeginCombo("##GlamourerDesignCombo", "Glamourer not available");
                        ImGui.EndCombo();
                        ImGui.EndDisabled();
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("The saved Glamourer design to apply to the minion.");

                    ImGui.Separator();
                    ImGui.Text("Persona Profiles:");
                    ImGui.SetNextItemWidth(230);

                    string currentPersonaName = "";
                    if (_selectedPersonaIndex >= 0 && _selectedPersonaIndex < _personaFiles.Count)
                    {
                        currentPersonaName = _personaFiles[_selectedPersonaIndex];
                    }

                    if (ImGui.BeginCombo("##PersonaSelectCombo", string.IsNullOrEmpty(_personaFileFilter) ? currentPersonaName : _personaFileFilter))
                    {
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputTextWithHint("##PersonaSearch", "Type to search...", ref _personaFileFilter, 256))
                        {
                            // Filter updated, no need to do anything special here
                        }

                        var filteredPersonas = GetFilteredPersonaFiles();

                        foreach (var persona in filteredPersonas)
                        {
                            var isSelected = persona == currentPersonaName;
                            if (ImGui.Selectable(persona, isSelected))
                            {
                                _selectedPersonaIndex = _personaFiles.IndexOf(persona);
                                _personaFileFilter = "";

                                if (_personaFiles[_selectedPersonaIndex] == "<New Profile>")
                                {
                                    _saveAsNameBuffer = _aiNameBuffer;
                                }
                                else
                                {
                                    _saveAsNameBuffer = _personaFiles[_selectedPersonaIndex];
                                }
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }

                        ImGui.EndCombo();
                    }

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
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
                        Service.Log.Info($"[Persona] Save Profile clicked. SaveAs='{_saveAsNameBuffer}', SelectedPersonaIndex={_selectedPersonaIndex}, PersonaFolder='{_personaFolder.FullName}'");
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
                                Service.Log.Info($"[Persona] Existing persona detected. Prompting overwrite. Path='{filePath}'");
                                _showOverwriteConfirmation = true;
                                ImGui.OpenPopup("Overwrite Confirmation");
                            }
                            else
                            {
                                Service.Log.Info($"[Persona] No existing persona detected. Saving new. Path='{filePath}'");
                                SavePersona(_saveAsNameBuffer);
                            }
                        }
                    }

                    ImGui.Spacing();

                    float openFolderButtonWidth = ImGui.CalcTextSize("Open Persona Folder").X + ImGui.GetStyle().FramePadding.X * 2.0f;
                    if (ImGui.Button("Open Persona Folder"))
                    {
                        Util.OpenLink(_personaFolder.FullName);
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
                                Service.Log.Info($"[Persona] Overwrite confirmed. SaveAs='{_saveAsNameBuffer}'");
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

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Other Settings"))
                {
                    ImGui.Text("Chat Log:");
                    ImGui.Checkbox("Show My Prompt", ref _showPromptBuffer);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Show your name and messages in conversation.");
                    }
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(380.0f);
                    ImGui.Checkbox("Show Additional Info", ref _showAdditionalInfoBuffer);

                    ImGui.Checkbox("Show Thoughts", ref _showThoughtsBuffer);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Show AI thoughts on answers when thinking mode is enabled.\nNot all models are supported.");
                    }
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(380.0f);
                    ImGui.Checkbox("Remove Line Breaks", ref _removeLineBreaksBuffer);

                    ImGui.Checkbox("Custom Chat Color", ref _useCustomColorsBuffer);
                    if (_useCustomColorsBuffer)
                    {
                        ImGui.SameLine();
                        ImGui.ColorEdit4("Text Color", ref _foregroundColorBuffer, ImGuiColorEditFlags.NoInputs);
                    }

                    ImGui.Separator();
                    ImGui.Text("On Login:");
                    ImGui.Checkbox("Login Greeting", ref _greetOnLoginBuffer);
                    if (_greetOnLoginBuffer)
                    {
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(600);
                        ImGui.InputText("##logingreetingprompt", ref _loginGreetingPromptBuffer, 512);
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("The prompt for the login greeting.");
                        }
                    }
                    ImGui.Checkbox("Fresh Login", ref _freshLoginBuffer);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Clears conversation history on logout and starts new conversation on login.\n" +
                                         "Also controls whether to greet on each login or just once.");
                    }

                    ImGui.Separator();
                    ImGui.Text("Behavior:");
                    ImGui.Checkbox("Enable In-game Context", ref _enableInGameContextBuffer);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Enables AI to use some in-game context as conversation's context.\n" +
                                         "For example: player's race, gender, level, location, weather information, etc.");
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
                    ImGui.SetCursorPosX(560.0f);
                    ImGui.BeginDisabled(!_enableHistoryBuffer);
                    ImGui.SetNextItemWidth(80);
                    if (ImGui.InputInt("History Limit", ref _conversationHistoryLimitBuffer))
                    {
                        if (_conversationHistoryLimitBuffer < 0)
                        {
                            _conversationHistoryLimitBuffer = 0;
                        }
                        else if (_conversationHistoryLimitBuffer > 100)
                        {
                            _conversationHistoryLimitBuffer = 100;
                        }
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Number of conversation exchanges to remember (0 = unlimited).\n" +
                                            "Each exchange includes your message and AI's response.");
                    }
                    ImGui.EndDisabled();

                    ImGui.Checkbox("Auto Model Fallback", ref _enableAutoFallbackBuffer);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("If an API request fails, the plugin will try other saved models.");
                    }

                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.Separator();
            ImGui.Spacing();
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

            ImGui.End();

            if (wasVisible && !_drawConfigWindow)
            {
                _glamourerDesignFilter = string.Empty;
                _personaFileFilter = string.Empty;
                LoadConfigIntoBuffers();
            }
        }

        private void SaveChanges()
        {
            var oldPersonaState = new
            {
                Name = configuration.AIName,
                LetSystemPromptHandleName = configuration.LetSystemPromptHandleAIName,
                Mode = configuration.AddressingMode,
                CustomUser = configuration.CustomUserName,
                Prompt = configuration.SystemPrompt,
                Temperature = configuration.Temperature
            };

            if (oldPersonaState.Name != _aiNameBuffer)
            {
                SaveCurrentSessionLog(oldPersonaState.Name);
                _currentSessionChatLog.Clear();
                _historicalChatLog.Clear();
            }

            configuration.Provider = _providerBuffer;
            configuration.ApiKey = _apiKeyBuffer;
            configuration.OpenAiApiKey = _openAiApiKeyBuffer;
            configuration.OpenAiBaseUrl = _openAiBaseUrlBuffer;
            configuration.OpenAiModel = _openAiModelBuffer;

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
            configuration.Temperature = _temperatureBuffer;
            configuration.MinionToReplace = _minionToReplaceBuffer;
            configuration.NpcGlamourerDesignGuid = _npcGlamourerDesignGuidBuffer;
            configuration.ShowPrompt = _showPromptBuffer;
            configuration.ShowThoughts = _showThoughtsBuffer;
            configuration.RemoveLineBreaks = _removeLineBreaksBuffer;
            configuration.ShowAdditionalInfo = _showAdditionalInfoBuffer;
            configuration.GreetOnLogin = _greetOnLoginBuffer;
            configuration.FreshLogin = _freshLoginBuffer;
            if (string.IsNullOrWhiteSpace(_loginGreetingPromptBuffer))
            {
                _loginGreetingPromptBuffer = "I'm back to Eorzea, please greet me.";
            }
            configuration.LoginGreetingPrompt = _loginGreetingPromptBuffer;
            configuration.EnableConversationHistory = _enableHistoryBuffer;
            configuration.ConversationHistoryLimit = _conversationHistoryLimitBuffer;
            configuration.EnableAutoFallback = _enableAutoFallbackBuffer;
            configuration.UseCustomColors = _useCustomColorsBuffer;
            configuration.ForegroundColor = _foregroundColorBuffer;
            configuration.EnableInGameContext = _enableInGameContextBuffer;

            configuration.DefaultModelIndex = _defaultModelProfileIndexBuffer;
            configuration.ThinkingModelIndex = _thinkingModelProfileIndexBuffer;
            configuration.GreetingModelIndex = _greetingModelProfileIndexBuffer;

            configuration.Save();
            UpdateCurrentProvider();

            bool personaChanged = oldPersonaState.Name != configuration.AIName ||
                                   oldPersonaState.LetSystemPromptHandleName != configuration.LetSystemPromptHandleAIName ||
                                   oldPersonaState.Mode != configuration.AddressingMode ||
                                   oldPersonaState.CustomUser != configuration.CustomUserName ||
                                   oldPersonaState.Prompt != configuration.SystemPrompt ||
                                   Math.Abs(oldPersonaState.Temperature - configuration.Temperature) > 0.001f;

            if (!string.IsNullOrEmpty(configuration.MinionToReplace) && !string.IsNullOrEmpty(configuration.AIName))
            {
                _minionNamingManager.UpdateNamingConfiguration(configuration.MinionToReplace, configuration.AIName, _glamouredMinionObjectId);
            }
            else
            {
                _minionNamingManager.ClearNaming();
            }

            if (personaChanged)
            {
                Service.Log.Info("Persona configuration changed. Resetting conversation history.");
                InitializeConversation();
                PrintSystemMessage($"{_aiNameBuffer}>> Persona settings were changed. Conversation history has been cleared.");

                if (oldPersonaState.Name != configuration.AIName)
                {
                    LoadHistoricalLogs(configuration.AIName);
                }
            }
        }
    }
}
