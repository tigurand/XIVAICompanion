using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
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

            if (UIHelper.AddHeaderIcon(Service.PluginInterface, "autorp_button", FontAwesomeIcon.TheaterMasks, out var openAutoRpPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Auto Role-Play Window" }) && openAutoRpPressed)
            {
                _drawAutoRpWindow = true;
            }

            if (UIHelper.AddHeaderIcon(Service.PluginInterface, "chat_button", FontAwesomeIcon.Comment, out var openChatPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Chat Window" }) && openChatPressed)
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
            ImGui.SetNextItemWidth(303);
            ImGui.Combo("AI Model", ref _selectedModelIndex, _availableModels, _availableModels.Length);
            ImGui.SameLine();
            if (ImGui.SmallButton("Details"))
            {
                string modelsDocs = "";
                if (_selectedModelIndex == 0)
                {
                    modelsDocs = "https://ai.google.dev/gemini-api/docs/models#gemini-2.5-pro";
                }
                else if (_selectedModelIndex == 1)
                {
                    modelsDocs = "https://ai.google.dev/gemini-api/docs/models#gemini-2.5-flash";
                }
                else if (_selectedModelIndex == 2)
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
            if (ImGui.TreeNodeEx("Behavior Options:##rp_open", ImGuiTreeNodeFlags.SpanFullWidth))
            {
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
                    ImGui.InputText("##logingreetingprompt", ref _loginGreetingPromptBuffer, 512);
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

                ImGui.Checkbox("Custom Chat Color", ref _useCustomColorsBuffer);
                if (_useCustomColorsBuffer)
                {
                    ImGui.SameLine();
                    ImGui.ColorEdit4("Text Color", ref _foregroundColorBuffer, ImGuiColorEditFlags.NoInputs);
                }
                ImGui.SameLine();
                ImGui.SetCursorPosX(380.0f);
                ImGui.Checkbox("Enable In-game Context", ref _enableInGameContextBuffer);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Enables AI to use some in-game context as conversation's context.\n" +
                                     "For example: player's race, gender, level, location, weather information, etc.");
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

                ImGui.TreePop();
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

            ImGui.SameLine();
            float openFolderButtonWidth = ImGui.CalcTextSize("Open Persona Folder").X + ImGui.GetStyle().FramePadding.X * 2.0f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - openFolderButtonWidth);
            if (ImGui.Button("Open Persona Folder"))
            {
                Util.OpenLink(_personaFolder.FullName);
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
            configuration.Temperature = _temperatureBuffer;
            configuration.MinionToReplace = _minionToReplaceBuffer;
            configuration.NpcGlamourerDesignGuid = _npcGlamourerDesignGuidBuffer;
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
            configuration.ConversationHistoryLimit = _conversationHistoryLimitBuffer;
            configuration.EnableAutoFallback = _enableAutoFallbackBuffer;
            configuration.UseCustomColors = _useCustomColorsBuffer;
            configuration.ForegroundColor = _foregroundColorBuffer;
            configuration.EnableInGameContext = _enableInGameContextBuffer;

            configuration.Save();

            bool personaChanged = oldPersonaState.Name != configuration.AIName ||
                                  oldPersonaState.LetSystemPromptHandleName != configuration.LetSystemPromptHandleAIName ||
                                  oldPersonaState.Mode != configuration.AddressingMode ||
                                  oldPersonaState.CustomUser != configuration.CustomUserName ||
                                  oldPersonaState.Prompt != configuration.SystemPrompt ||
                                  Math.Abs(oldPersonaState.Temperature - configuration.Temperature) > 0.001f;

            if (!string.IsNullOrEmpty(configuration.MinionToReplace) && !string.IsNullOrEmpty(configuration.AIName))
            {
                _minionNamingManager.UpdateNamingConfiguration(configuration.MinionToReplace, configuration.AIName);
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