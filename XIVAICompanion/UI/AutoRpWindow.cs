using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System;
using System.Numerics;

namespace XIVAICompanion
{
    public partial class AICompanionPlugin
    {
        private static readonly string[] ReplyChannelOptions = { "Say", "Yell", "Shout", "Party", "Alliance", "Tell (Reply)", "Free Company", "PvP Team", "Novice Network",
            "Linkshell 1", "Linkshell 2", "Linkshell 3", "Linkshell 4", "Linkshell 5", "Linkshell 6", "Linkshell 7", "Linkshell 8",
            "CWLS 1", "CWLS 2", "CWLS 3", "CWLS 4", "CWLS 5", "CWLS 6", "CWLS 7", "CWLS 8", };
        private void DrawAutoRpWindow()
        {
            if (!_drawAutoRpWindow) return;

            ImGui.SetNextWindowSizeConstraints(new Vector2(480, 468), new Vector2(9999, 9999));
            ImGui.SetNextWindowSize(new Vector2(480, 468), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Auto Role-Play", ref _drawAutoRpWindow))
            {
                if (_isAutoRpRunning)
                {
                    if (ImGui.Button("Stop", new Vector2(ImGui.GetContentRegionAvail().X, 30)))
                    {
                        _isAutoRpRunning = false;
                        Service.ChatGui.ChatMessage -= OnChatMessage;
                        _chatOocMode = false;
                        configuration.AutoRpConfig.TargetName = _autoRpTargetNameBuffer;
                        configuration.Save();
                        Service.Log.Info("Auto RP Mode Stopped.");
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
                            PrintSystemMessage($"{_aiNameBuffer}>> AutoRP Error: API key is not set in /ai cfg.");
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer) && _autoRpTargetNameBuffer == _localPlayerName)
                            {
                                Service.Log.Info("Targeted character is self. Ignoring for auto-listening and falling back to manual input mode.");
                            }

                            _isAutoRpRunning = true;
                            Service.ChatGui.ChatMessage += OnChatMessage;

                            if (_autoRpAutoTargetBuffer)
                            {
                                _lastTargetId = Service.TargetManager.Target?.GameObjectId ?? 0;
                                HandleTargetChange();
                            }

                            Service.Log.Info($"Auto RP Mode Started. Listening for '{_autoRpTargetNameBuffer}'.");
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

                ImGui.SameLine();
                if (ImGui.Button("Get Current Target"))
                {
                    var target = Service.TargetManager.Target;

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

                if (ImGui.Checkbox("Open Listener Mode", ref _openListenerModeBuffer))
                {
                    configuration.AutoRpConfig.IsOpenListenerModeEnabled = _openListenerModeBuffer;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("When Auto RP is running, this will capture ANY message in the selected channels and respond.\nThis bypasses the main 'Target Player Name' logic.");

                ImGui.BeginDisabled(!_openListenerModeBuffer);
                ImGui.SameLine();
                ImGui.SetCursorPosX(200.0f);
                if (ImGui.Checkbox("Use Mixed History", ref _mixedHistoryModeBuffer))
                {
                    configuration.AutoRpConfig.MixedHistoryMode = _mixedHistoryModeBuffer;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Use mixed history to talk with multiple people instead of personal history.\nWarning: This may confuse the AI.");
                ImGui.EndDisabled();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Listen for messages in the following channels:");
                if (_openListenerModeBuffer)
                {
                    if (ImGui.TreeNodeEx("Generic Channels##rp_open", ImGuiTreeNodeFlags.SpanFullWidth))
                    {
                        if (ImGui.BeginTable("channels_open", 3))
                        {
                            ImGui.TableNextColumn();
                            if (ImGui.Checkbox("Say##open", ref _openListenerListenSayBuffer)) { configuration.AutoRpConfig.OpenListenerListenSay = _openListenerListenSayBuffer; configuration.Save(); }
                            if (ImGui.Checkbox("Party##open", ref _openListenerListenPartyBuffer)) { configuration.AutoRpConfig.OpenListenerListenParty = _openListenerListenPartyBuffer; configuration.Save(); }
                            if (ImGui.Checkbox("Incoming Tells##open", ref _openListenerListenTellBuffer)) { configuration.AutoRpConfig.OpenListenerListenTell = _openListenerListenTellBuffer; configuration.Save(); }
                            if (ImGui.Checkbox("Novice Network##open", ref _openListenerListenNoviceNetworkBuffer)) { configuration.AutoRpConfig.OpenListenerListenNoviceNetwork = _openListenerListenNoviceNetworkBuffer; configuration.Save(); }

                            ImGui.TableNextColumn();
                            if (ImGui.Checkbox("Yell##open", ref _openListenerListenYellBuffer)) { configuration.AutoRpConfig.OpenListenerListenYell = _openListenerListenYellBuffer; configuration.Save(); }
                            if (ImGui.Checkbox("Cross-World Party##open", ref _openListenerListenCrossPartyBuffer)) { configuration.AutoRpConfig.OpenListenerListenCrossParty = _openListenerListenCrossPartyBuffer; configuration.Save(); }
                            if (ImGui.Checkbox("Free Company##open", ref _openListenerListenFreeCompanyBuffer)) { configuration.AutoRpConfig.OpenListenerListenFreeCompany = _openListenerListenFreeCompanyBuffer; configuration.Save(); }

                            ImGui.TableNextColumn();
                            if (ImGui.Checkbox("Shout##open", ref _openListenerListenShoutBuffer)) { configuration.AutoRpConfig.OpenListenerListenShout = _openListenerListenShoutBuffer; configuration.Save(); }
                            if (ImGui.Checkbox("Alliance##open", ref _openListenerListenAllianceBuffer)) { configuration.AutoRpConfig.OpenListenerListenAlliance = _openListenerListenAllianceBuffer; configuration.Save(); }
                            if (ImGui.Checkbox("PvP Team##open", ref _openListenerListenPvPTeamBuffer)) { configuration.AutoRpConfig.OpenListenerListenPvPTeam = _openListenerListenPvPTeamBuffer; configuration.Save(); }
                            ImGui.EndTable();
                        }
                        ImGui.TreePop();
                    }
                    if (ImGui.TreeNodeEx("Linkshells##rp_open", ImGuiTreeNodeFlags.SpanFullWidth))
                    {
                        if (ImGui.BeginTable("lschannels_open", 4))
                        {
                            for (int i = 0; i < 8; i++)
                            {
                                ImGui.TableNextColumn();
                                if (ImGui.Checkbox($"LS{i + 1}##open", ref _openListenerListenLsBuffers[i])) { configuration.AutoRpConfig.OpenListenerListenLs[i] = _openListenerListenLsBuffers[i]; configuration.Save(); }
                            }
                            ImGui.EndTable();
                        }
                        ImGui.TreePop();
                    }
                    if (ImGui.TreeNodeEx("Cross-world Linkshells##rp_open", ImGuiTreeNodeFlags.SpanFullWidth))
                    {
                        if (ImGui.BeginTable("cwlschannels_open", 4))
                        {
                            for (int i = 0; i < 8; i++)
                            {
                                ImGui.TableNextColumn();
                                if (ImGui.Checkbox($"CWLS{i + 1}##open", ref _openListenerListenCwlsBuffers[i])) { configuration.AutoRpConfig.OpenListenerListenCwls[i] = _openListenerListenCwlsBuffers[i]; configuration.Save(); }
                            }
                            ImGui.EndTable();
                        }
                        ImGui.TreePop();
                    }
                }
                else
                {
                    if (ImGui.TreeNodeEx("Generic Channels##rp", ImGuiTreeNodeFlags.SpanFullWidth))
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
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Advanced Settings");

                ImGui.BeginDisabled(_autoRpReplyInSpecificChannelBuffer);
                if (ImGui.Checkbox("Reply in original channel", ref _autoRpReplyInChannelBuffer))
                {
                    configuration.AutoRpConfig.ReplyInOriginalChannel = _autoRpReplyInChannelBuffer;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("If checked, the AI will attempt to respond in the same channel the message was received in.");
                ImGui.EndDisabled();

                ImGui.BeginDisabled(_autoRpReplyInChannelBuffer);
                if (ImGui.Checkbox("Reply to a specific channel:", ref _autoRpReplyInSpecificChannelBuffer))
                {
                    configuration.AutoRpConfig.ReplyInSpecificChannel = _autoRpReplyInSpecificChannelBuffer;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("If checked, the AI will always respond in the selected channel.");

                if (configuration.AutoRpConfig.ReplyInSpecificChannel)
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.Combo("##SpecificChannelCombo", ref _autoRpSpecificReplyChannelBuffer, ReplyChannelOptions, ReplyChannelOptions.Length))
                    {
                        configuration.AutoRpConfig.SpecificReplyChannel = _autoRpSpecificReplyChannelBuffer;
                        configuration.Save();
                    }
                }
                ImGui.EndDisabled();

                ImGui.SetNextItemWidth(100);
                if (ImGui.DragFloat("Initial reply delay (sec)", ref _autoRpInitialDelayBuffer, 0.1f, 0.0f, 10.0f))
                {
                    _autoRpInitialDelayBuffer = Math.Clamp(_autoRpInitialDelayBuffer, 0.0f, 10.0f);
                    configuration.AutoRpConfig.InitialResponseDelaySeconds = _autoRpInitialDelayBuffer;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Adds a 'thinking time' delay before the AI sends a reply to game chat to feel more human.\nSet to 0 to disable.");

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
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("When Auto RP is running, this will capture ANY incoming tell from ANY player and respond.\nThis bypasses the main 'Target Player Name' logic.");
                }
            }
            ImGui.End();
        }

        private void HandleTargetChange()
        {
            if (!_autoRpAutoTargetBuffer) return;

            var currentTarget = Service.TargetManager.Target;

            if (currentTarget is IPlayerCharacter playerTarget)
            {
                var newName = playerTarget.Name.ToString();
                if (_autoRpTargetNameBuffer != newName)
                {
                    _autoRpTargetNameBuffer = newName;
                    _currentRpPartnerName = newName;
                    Service.Log.Info($"[Auto RP] Target automatically updated to: {_autoRpTargetNameBuffer}");
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(_autoRpTargetNameBuffer))
                {
                    _autoRpTargetNameBuffer = string.Empty;
                    _currentRpPartnerName = string.Empty;
                    Service.Log.Info("[Auto RP] Target is not a player. Switched to Manual Input Mode.");
                }
            }
        }
    }
}