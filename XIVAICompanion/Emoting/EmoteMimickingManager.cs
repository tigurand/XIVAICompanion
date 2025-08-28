using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XIVAICompanion.Emoting
{
    /// <summary>
    /// Manages emote mimicking for glamoured minions
    /// </summary>
    public class EmoteMimickingManager : IDisposable
    {
        private readonly EmoteHooks _emoteHooks;
        private readonly EmoteManager _emoteManager;
        private readonly IDataManager _dataManager;
        private readonly ConcurrentDictionary<string, string> _currentlyEmotingCharacters = new();
        private readonly ConcurrentDictionary<uint, CancellationTokenSource> _pendingEmotes = new();
        private bool _disposed = false;

        private const int MimicDelay = 1000;

        private readonly ushort[] _ignoredEmotes = { 50 }; // 50 = /sit on chair

        private static readonly HashSet<ushort> _facialExpressionTimelines = new()
        {
            604, 605, 606, 607, 608, 611, 612, 614, 615, 616,
            617, 619, 620, 621, 622, 623, 624, 625,
            8051, 6215, 6216, 6226, 6227, 6228, 6229,
            6261, 6262, 6253, 8021
        };

        private static readonly HashSet<ushort> _stopEmoteIds = new()
        {
            89, // Wake up
            53  // Stand up from ground sit
        };

        public EmoteMimickingManager(IGameInteropProvider interopProvider, IClientState clientState,
            IObjectTable objectTable, IDataManager dataManager, ISigScanner sigScanner)
        {
            _dataManager = dataManager;
            _emoteManager = new EmoteManager();
            _emoteHooks = new EmoteHooks(interopProvider, clientState, objectTable, sigScanner);
            _emoteHooks.OnEmote += OnPlayerEmote;

            Service.Log.Info("Emote mimicking manager initialized");
        }

        private async void OnPlayerEmote(IGameObject instigator, ushort emoteId)
        {
            try
            {
                var player = Service.ClientState.LocalPlayer;

                if (player == null ||
                    instigator.GameObjectId != player.GameObjectId)
                    return;

                Service.Log.Debug($"Player performed emote {emoteId}");

                var glamouredMinions = GetGlamouredMinions();

                var emoteData = GetEmoteData(emoteId);
                if (emoteData == null) return;

                var animationId = GetAnimationIdForEmote(emoteData.Value);
                bool isFacialExpression = _facialExpressionTimelines.Contains(animationId);

                if (_emoteManager.IsMinionGroundSitOrSleep && !_facialExpressionTimelines.Contains(animationId) && !_stopEmoteIds.Contains(emoteId))
                {
                    Service.Log.Debug($"Skipping emote {emoteId} due to ground sit/sleep state.");
                    return;
                }

                foreach (var minion in glamouredMinions)
                {
                    var minionId = (uint)minion.GameObjectId;

                    if (!isFacialExpression || _stopEmoteIds.Contains(emoteId))
                    {
                        if (_pendingEmotes.TryRemove(minionId, out var oldCts))
                        {
                            oldCts.Cancel();
                            oldCts.Dispose();
                            await Task.Yield();
                        }
                    }

                    var newCts = new CancellationTokenSource();

                    if (!isFacialExpression)
                    {
                        _pendingEmotes[minionId] = newCts;
                    }

                    await Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(MimicDelay, newCts.Token);
                            if (newCts.IsCancellationRequested) return;

                            await Service.Framework.RunOnFrameworkThread(() =>
                            {
                                var currentEmoteData = GetEmoteData(emoteId);
                                if (currentEmoteData != null)
                                {
                                    ApplyEmoteToMinion(minion, emoteId);
                                }
                            });
                        }
                        finally
                        {
                            if (!isFacialExpression)
                            {
                                _pendingEmotes.TryRemove(minionId, out _);
                            }
                        }
                    }, newCts.Token);
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Error processing player emote");
            }
        }

        private ICharacter[] GetGlamouredMinions()
        {
            try
            {
                var minions = Service.ObjectTable
                    .Where(obj => obj.ObjectKind == ObjectKind.Companion)
                    .Cast<ICharacter>()
                    .Where(companion => IsPlayerCompanion(companion) && IsHumanoidMinion(companion))
                    .ToArray();

                return minions;
            }
            catch (Exception ex)
            {
                Service.Log.Warning(ex, "Error getting glamoured minions");
                return Array.Empty<ICharacter>();
            }
        }

        private bool IsPlayerCompanion(ICharacter companion)
        {
            //return Vector3.Distance(Service.ClientState.LocalPlayer?.Position ?? Vector3.Zero, companion.Position) < 30f;
            var glamouredCompanion = (companion.GameObjectId != 0) ? Service.ObjectTable.FirstOrDefault(o => o.GameObjectId == companion.GameObjectId) : null;
            return glamouredCompanion != null;
        }

        private bool IsHumanoidMinion(ICharacter minion)
        {
            return minion.ObjectKind == ObjectKind.Companion;
        }

        private void ApplyEmoteToMinion(ICharacter minion, ushort emoteId)
        {
            try
            {
                if (_ignoredEmotes.Contains(emoteId))
                {
                    return;
                }

                if (_stopEmoteIds.Contains(emoteId))
                {
                    _emoteManager.ClearGroundSitOrSleepState();
                    _emoteManager.StopLoopingEmote((uint)minion.GameObjectId);
                    _emoteManager.StopEmote(minion);
                    return;
                }

                var emote = GetEmoteData(emoteId);
                if (emote == null || string.IsNullOrEmpty(emote.Value.Name.ToString()))
                {
                    Service.Log.Warning($"Could not find valid emote data for ID {emoteId}");
                    return;
                }

                var emoteValue = emote.Value;
                Service.Log.Debug($"Applying emote {emoteValue.Name} to minion {minion.Name}");

                var animationId = GetAnimationIdForEmote(emoteValue);
                if (animationId == 0)
                {
                    Service.Log.Warning($"No animation found for emote {emote.Value.Name}");
                    return;
                }

                ApplyEmoteByType(minion, emote.Value, animationId);
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, $"Error applying emote to minion {minion.Name}");
            }
        }

        private Emote? GetEmoteData(ushort emoteId)
        {
            try
            {
                var emotes = _dataManager.GetExcelSheet<Emote>();
                return emotes?.GetRow(emoteId);
            }
            catch (Exception ex)
            {
                Service.Log.Warning(ex, $"Error getting emote data for ID {emoteId}");
                return null;
            }
        }

        private ushort GetAnimationIdForEmote(Emote emote)
        {
            try
            {
                var actionTimeline = emote.ActionTimeline.Count > 0 ? emote.ActionTimeline[0] : default(RowRef<ActionTimeline>);
                return actionTimeline.IsValid ? (ushort)actionTimeline.Value.RowId : (ushort)0;
            }
            catch (Exception ex)
            {
                Service.Log.Warning(ex, $"Error getting animation ID for emote {emote.Name}");
                return 0;
            }
        }

        private void ApplyEmoteByType(ICharacter minion, Emote emote, ushort animationId)
        {
            try
            {
                if (_facialExpressionTimelines.Contains(animationId))
                {
                    _emoteManager.ApplyFacialExpression(minion, animationId);
                    return;
                }

                var emoteMode = emote.EmoteMode.IsValid ? emote.EmoteMode.Value.ConditionMode : (byte)0;

                switch (emoteMode)
                {
                    case 3: // Looping emotes (Special category) - continue until player moves
                    case 11: // Some other looping type
                        ApplyLoopingEmote(minion, animationId);
                        break;

                    default: // Single emotes (General and Expressions)
                        ApplyTimedEmote(minion, animationId);
                        break;
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, $"Error determining emote type for {emote.Name}");
                ApplyTimedEmote(minion, animationId);
            }
        }

        private void ApplyTimedEmote(ICharacter minion, ushort animationId)
        {
            _emoteManager.TriggerEmoteTimed(minion, animationId, 1000);
        }

        private void ApplyLoopingEmote(ICharacter minion, ushort animationId)
        {
            _emoteManager.StopLoopingEmote((uint)minion.GameObjectId, minion);

            if (Service.ClientState.LocalPlayer != null)
            {
                _emoteManager.TriggerEmoteUntilPlayerMoves(
                    Service.ClientState.LocalPlayer,
                    minion,
                    animationId);
            }
        }

        public void StopAllEmotes()
        {
            try
            {
                var glamouredMinions = GetGlamouredMinions();
                foreach (var minion in glamouredMinions)
                {
                    _emoteManager.StopEmote(minion);
                }

                _currentlyEmotingCharacters.Clear();
                Service.Log.Debug("Stopped all minion emotes");
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Error stopping all emotes");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                StopAllEmotes();

                foreach (var cts in _pendingEmotes.Values)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                _pendingEmotes.Clear();

                _emoteHooks?.Dispose();
                _emoteManager?.Dispose();
                Service.Log.Info("Emote mimicking manager disposed");
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Error disposing emote mimicking manager");
            }

            _disposed = true;
        }
    }
}