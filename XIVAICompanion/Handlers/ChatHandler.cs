using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XIVAICompanion
{
    public partial class AICompanionPlugin
    {
        private static readonly HashSet<XivChatType> AllowedRpAndListenerChatTypes = new HashSet<XivChatType>
        {
            XivChatType.Say,
            XivChatType.Party,
            XivChatType.Alliance,
            XivChatType.TellIncoming,
            XivChatType.Shout,
            XivChatType.Yell,
            XivChatType.FreeCompany,
            XivChatType.CrossParty,
            XivChatType.NoviceNetwork,
            XivChatType.PvPTeam,
            XivChatType.Ls1,
            XivChatType.Ls2,
            XivChatType.Ls3,
            XivChatType.Ls4,
            XivChatType.Ls5,
            XivChatType.Ls6,
            XivChatType.Ls7,
            XivChatType.Ls8,
            XivChatType.CrossLinkShell1,
            XivChatType.CrossLinkShell2,
            XivChatType.CrossLinkShell3,
            XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5,
            XivChatType.CrossLinkShell6,
            XivChatType.CrossLinkShell7,
            XivChatType.CrossLinkShell8
        };
        private string ParsePlayerNameFromRaw(string rawSender)
        {
            if (string.IsNullOrEmpty(rawSender)) return string.Empty;

            string cleanedName = rawSender;

            while (cleanedName.Length > 0 && !char.IsLetter(cleanedName[0]))
            {
                cleanedName = cleanedName.Substring(1);
            }

            if (cleanedName.Length >= 4 &&
                char.IsUpper(cleanedName[0]) &&
                char.IsUpper(cleanedName[1]) &&
                char.IsUpper(cleanedName[2]) &&
                cleanedName[3] == ' ')
            {
                cleanedName = cleanedName.Substring(4);
            }

            for (int i = 1; i < cleanedName.Length; i++)
            {
                if (char.IsUpper(cleanedName[i]) && cleanedName[i - 1] != ' ')
                {
                    cleanedName = cleanedName.Substring(0, i).Trim();
                    break;
                }
            }

            cleanedName = cleanedName.Trim();

            return cleanedName;
        }

        private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            if (!AllowedRpAndListenerChatTypes.Contains(type)) return;

            bool isTellReply = _autoReplyToAllTellsBuffer && type == XivChatType.TellIncoming;
            bool isOpenListenerReply = _openListenerModeBuffer && IsOpenListenerChannelEnabled(type);

            if (_isAutoRpRunning && (isOpenListenerReply || (isTellReply && _isDevModeEnabled)))
            {
                if (sender.TextValue.StartsWith("[CT]")) return;
                if (IsAutoRpProcessing()) return;
                if ((DateTime.Now - _lastRpResponseTimestamp).TotalSeconds < _autoRpDelayBuffer) return;

                string cleanPlayerName = ParsePlayerNameFromRaw(sender.TextValue);
                if (cleanPlayerName == _localPlayerName) return;

                _currentRpPartnerName = cleanPlayerName;

                string logPrefix = isTellReply ? "[Auto-Tell Reply]" : "[Open Listener]";
                Service.Log.Info($"{logPrefix} Captured message from '{cleanPlayerName}' in '{type}': {message.TextValue}");

                string replyMessageText = message.TextValue;

                _currentSessionChatLog.Add(new ChatMessage { Timestamp = DateTime.Now, Author = cleanPlayerName, Message = replyMessageText });
                _shouldScrollToBottom = true;

                Task.Run(() => SendAutoReplyPrompt(replyMessageText, cleanPlayerName, type));

                return;
            }

            if (_isAutoRpRunning && IsRpChannelEnabled(type))
            {
                if (string.IsNullOrWhiteSpace(_autoRpTargetNameBuffer) || _autoRpTargetNameBuffer == _localPlayerName) return;
                if (IsAutoRpProcessing()) return;
                if ((DateTime.Now - _lastRpResponseTimestamp).TotalSeconds < _autoRpDelayBuffer) return;

                var senderName = sender.TextValue;
                if (!string.IsNullOrEmpty(senderName) && !char.IsLetter(senderName[0]))
                {
                    senderName = senderName.Substring(1);
                }
                if (!senderName.StartsWith(_autoRpTargetNameBuffer)) return;

                _currentRpPartnerName = _autoRpTargetNameBuffer;

                Service.Log.Info($"[Auto RP] Captured message from '{_autoRpTargetNameBuffer}' in channel '{type}': {message.TextValue}");
                string messageText = message.TextValue;

                _currentSessionChatLog.Add(new ChatMessage { Timestamp = DateTime.Now, Author = _autoRpTargetNameBuffer, Message = messageText });
                _shouldScrollToBottom = true;

                Task.Run(() => SendAutoRpPrompt(messageText, type));
            }
        }

        private int GetLsArrayIndex(XivChatType type)
        {
            return type switch
            {
                XivChatType.Ls1 => 0,
                XivChatType.Ls2 => 1,
                XivChatType.Ls3 => 2,
                XivChatType.Ls4 => 3,
                XivChatType.Ls5 => 4,
                XivChatType.Ls6 => 5,
                XivChatType.Ls7 => 6,
                XivChatType.Ls8 => 7,
                _ => -1
            };
        }

        private int GetCwlsArrayIndex(XivChatType type)
        {
            return type switch
            {
                XivChatType.CrossLinkShell1 => 0,
                XivChatType.CrossLinkShell2 => 1,
                XivChatType.CrossLinkShell3 => 2,
                XivChatType.CrossLinkShell4 => 3,
                XivChatType.CrossLinkShell5 => 4,
                XivChatType.CrossLinkShell6 => 5,
                XivChatType.CrossLinkShell7 => 6,
                XivChatType.CrossLinkShell8 => 7,
                _ => -1
            };
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
                    int lsIndex = GetLsArrayIndex(type);
                    if (lsIndex == -1)
                    {
                        Service.Log.Warning($"Unsupported LS channel type: {type}");
                        return false;
                    }
                    return _autoRpListenLsBuffers[lsIndex];
                case >= XivChatType.CrossLinkShell1 and <= XivChatType.CrossLinkShell8:
                    int cwlsIndex = GetCwlsArrayIndex(type);
                    if (cwlsIndex == -1)
                    {
                        Service.Log.Warning($"Unsupported CWLS channel type: {type}");
                        return false;
                    }
                    return _autoRpListenCwlsBuffers[cwlsIndex];
                default:
                    return false;
            }
        }

        private bool IsOpenListenerChannelEnabled(XivChatType type)
        {
            switch (type)
            {
                case XivChatType.Say: return _openListenerListenSayBuffer;
                case XivChatType.Party: return _openListenerListenPartyBuffer;
                case XivChatType.Alliance: return _openListenerListenAllianceBuffer;
                case XivChatType.TellIncoming: return _openListenerListenTellBuffer;
                case XivChatType.Shout: return _openListenerListenShoutBuffer;
                case XivChatType.Yell: return _openListenerListenYellBuffer;
                case XivChatType.FreeCompany: return _openListenerListenFreeCompanyBuffer;
                case XivChatType.CrossParty: return _openListenerListenCrossPartyBuffer;
                case XivChatType.NoviceNetwork: return _openListenerListenNoviceNetworkBuffer;
                case XivChatType.PvPTeam: return _openListenerListenPvPTeamBuffer;
                case >= XivChatType.Ls1 and <= XivChatType.Ls8:
                    int lsIndex = GetLsArrayIndex(type);
                    if (lsIndex == -1)
                    {
                        Service.Log.Warning($"Unsupported OpenListener LS channel type: {type}");
                        return false;
                    }
                    return _openListenerListenLsBuffers[lsIndex];
                case >= XivChatType.CrossLinkShell1 and <= XivChatType.CrossLinkShell8:
                    int cwlsIndex = GetCwlsArrayIndex(type);
                    if (cwlsIndex == -1)
                    {
                        Service.Log.Warning($"Unsupported OpenListener CWLS channel type: {type}");
                        return false;
                    }
                    return _openListenerListenCwlsBuffers[cwlsIndex];
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

        private string GetPrefixForChannelIndex(int index)
        {
            return index switch
            {
                0 => "/s ",
                1 => "/y ",
                2 => "/sh ",
                3 => "/p ",
                4 => "/a ",
                5 => "/r ",
                6 => "/fc ",
                7 => "/pvpteam ",
                8 => "/n ",
                9 => "/l1 ",
                10 => "/l2 ",
                11 => "/l3 ",
                12 => "/l4 ",
                13 => "/l5 ",
                14 => "/l6 ",
                15 => "/l7 ",
                16 => "/l8 ",
                17 => "/cwl1 ",
                18 => "/cwl2 ",
                19 => "/cwl3 ",
                20 => "/cwl4 ",
                21 => "/cwl5 ",
                22 => "/cwl6 ",
                23 => "/cwl7 ",
                24 => "/cwl8 ",
                _ => string.Empty
            };
        }
    }
}
