using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Utility;
using ECommons;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using XIVAICompanion.Utils;

namespace XIVAICompanion
{
    public partial class AICompanionPlugin
    {
        private unsafe void DrawChatWindow()
        {
            if (!_drawChatWindow) return;

            ImGui.SetNextWindowSizeConstraints(new Vector2(450, 300), new Vector2(9999, 9999));
            ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin($"{Name} | Chat", ref _drawChatWindow))
            {
                if (UIHelper.AddHeaderIcon(Service.PluginInterface, "autorp_button", FontAwesomeIcon.Heart, out var kofiPressed, new UIHelper.HeaderIconOptions { Tooltip = "Support on Ko-fi" }) && kofiPressed)
                {
                    GenericHelpers.ShellStart("https://ko-fi.com/lucillebagul");
                }

                if (UIHelper.AddHeaderIcon(Service.PluginInterface, "autorp_button", FontAwesomeIcon.TheaterMasks, out var openAutoRpPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Auto Role-Play Window" }) && openAutoRpPressed)
                {
                    _drawAutoRpWindow = true;
                }

                if (UIHelper.AddHeaderIcon(Service.PluginInterface, "config_button", FontAwesomeIcon.Cog, out var openConfigPressed, new UIHelper.HeaderIconOptions { Tooltip = "Open Configuration" }) && openConfigPressed)
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
                            "This is separate from the AI's short-term history (in the main config).\n" +
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
                    if (ImGui.Button("Open Logs Folder"))
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

                _searchModeBuffer = configuration.SearchMode;
                _thinkModeBuffer = configuration.ThinkMode;

                bool previousSearchMode = _searchModeBuffer;
                bool previousThinkMode = _thinkModeBuffer;

                ImGui.Checkbox("Web Search", ref _searchModeBuffer);
                ImGui.SameLine();
                ImGui.Checkbox("Think", ref _thinkModeBuffer);
                ImGui.SameLine();
                ImGui.Checkbox("Fresh", ref _chatFreshMode);

                if (_isAutoRpRunning)
                {
                    ImGui.SameLine();
                    ImGui.Checkbox("OOC", ref _chatOocMode);
                }

                if (previousSearchMode != _searchModeBuffer)
                {
                    configuration.SearchMode = _searchModeBuffer;
                    configuration.Save();
                }

                if (previousThinkMode != _thinkModeBuffer)
                {
                    configuration.ThinkMode = _thinkModeBuffer;
                    configuration.Save();
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

                if (ImGui.InputTextWithHint("##ChatInput", "Type a message", ref _chatInputBuffer, 2048, inputTextFlags, (data) =>
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
                    ProcessPrompt(_chatInputBuffer, _chatInputBuffer);

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

                Service.Log.Info($"Found {logFiles.Count} historical chat logs for the exact persona '{aiName}' to load.");

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
                Service.Log.Error(ex, "Failed to load historical chat logs.");
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
                    Service.Log.Info($"Automatically deleted {deleteCount} chat log(s) older than {configuration.DaysToKeepLogs} days.");
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Failed during automatic deletion of old chat logs.");
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
                Service.Log.Info($"Chat session with '{aiNameToUse}' saved to {fileName}");
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Failed to save chat session log.");
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
                    Service.Log.Warning($"Cannot send message to chat because the prefixes are too long in bytes: {finalCommand}{finalPrefix}");
                    PrintSystemMessage($"{_aiNameBuffer}>> Cannot send message, prefix/command is too long.");
                    return;
                }

                int maxContentBytes = chatByteLimit - prefixBytes - commandBytes;

                var chunks = SplitIntoChunksByBytes(message, maxContentBytes).ToList();
                Service.Log.Info($"Sending message to chat in {chunks.Count} chunk(s) with command '{finalCommand}' and prefix '{finalPrefix}'.");

                foreach (var chunk in chunks)
                {
                    string finalMessage = finalCommand + finalPrefix + chunk;
                    _chatMessageQueue.Enqueue(finalMessage);
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "An error occurred while trying to send a message to game chat.");
                PrintSystemMessage($"{_aiNameBuffer}>> An error occurred while trying to send the message.");
            }
        }

        private static readonly char[] ChunkDelimiters = { ' ', '\n', '\r', '、', '。' };
        private static readonly char[] WhitespaceDelimiters = { ' ', '\n', '\r' };
        private static readonly char[] RetainedDelimiters = { '、', '。' };

        private int FindLastDelimiterIndex(string text, int startIndex, int count)
        {
            int lastDelimiterIndex = -1;
            for (int i = startIndex + count - 1; i >= startIndex; i--)
            {
                if (Array.IndexOf(ChunkDelimiters, text[i]) != -1)
                {
                    lastDelimiterIndex = i;
                    break;
                }
            }
            return lastDelimiterIndex;
        }

        private int FindLastDelimiterIndex(string text)
        {
            for (int i = text.Length - 1; i >= 0; i--)
            {
                if (Array.IndexOf(ChunkDelimiters, text[i]) != -1)
                {
                    return i;
                }
            }
            return -1;
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
                    int lastDelimiter = FindLastDelimiterIndex(text, offset, size);
                    if (lastDelimiter != -1 && lastDelimiter > offset)
                    {
                        if (Array.IndexOf(RetainedDelimiters, text[lastDelimiter]) != -1)
                        {
                            size = lastDelimiter - offset + 1;
                        }
                        else
                        {
                            size = lastDelimiter - offset;
                        }
                    }
                }

                string chunk = text.Substring(offset, size);
                yield return chunk.TrimStart(WhitespaceDelimiters);
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
                    Service.Log.Error($"Cannot split message. A single character at position {currentPos} exceeds the max byte size of {maxChunkByteSize}.");
                    yield break;
                }

                int maxCharCount = searchLength;
                string potentialChunk = text.Substring(currentPos, maxCharCount);

                bool isLastChunk = (currentPos + maxCharCount) >= text.Length;

                int breakPos = FindLastDelimiterIndex(potentialChunk);

                if (!isLastChunk && breakPos > 0)
                {
                    if (Array.IndexOf(RetainedDelimiters, potentialChunk[breakPos]) != -1)
                    {
                        string finalChunk = potentialChunk.Substring(0, breakPos + 1);
                        yield return finalChunk;
                        currentPos += finalChunk.Length;
                    }
                    else
                    {
                        string finalChunk = potentialChunk.Substring(0, breakPos);
                        yield return finalChunk;
                        currentPos += finalChunk.Length;
                    }
                }
                else
                {
                    yield return potentialChunk;
                    currentPos += potentialChunk.Length;
                }

                while (currentPos < text.Length && Array.IndexOf(WhitespaceDelimiters, text[currentPos]) != -1)
                {
                    currentPos++;
                }
            }
        }
    }
}