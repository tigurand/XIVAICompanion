using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace XIVAICompanion.Emoting
{
    public class EmoteState
    {
        public CancellationTokenSource Cts { get; set; } = null!;
        public ushort AnimationId { get; set; }
        public bool IsLooping { get; set; }
    }

    public class EmoteManager : IDisposable
    {
        private readonly ConcurrentDictionary<uint, EmoteState> _emoteStates = new();
        private readonly object _stateLock = new object();

        public bool IsMinionGroundSitOrSleep { get; private set; } = false;

        private static readonly HashSet<ushort> _groundSitAndSleepAnimationIds = new()
        {
            654, // Ground sit animation
            585  // Sleep animation
        };

        public EmoteManager()
        {
        }

        public void TriggerEmoteTimed(ICharacter character, ushort animationId, int duration)
        {
            if (character == null) return;

            Task.Run(async () =>
            {
                ApplyAnimation(character, animationId);
                await Task.Delay(duration);
                StopEmote(character);
            });
        }

        public void TriggerEmoteUntilPlayerMoves(IPlayerCharacter player, ICharacter character, ushort animationId)
        {
            if (player == null || character == null) return;

            var gameObjectId = (uint)character.GameObjectId;

            EmoteState newState;
            CancellationTokenSource cts;

            lock (_stateLock)
            {
                StopLoopingEmoteInternal(gameObjectId, character);

                Thread.Sleep(50);

                newState = new EmoteState
                {
                    Cts = new CancellationTokenSource(),
                    AnimationId = animationId,
                    IsLooping = true
                };

                var addAttempts = 0;
                var addResult = false;

                while (addAttempts < 5 && !addResult)
                {
                    addResult = _emoteStates.TryAdd(gameObjectId, newState);

                    if (!addResult)
                    {
                        addAttempts++;

                        if (_emoteStates.TryRemove(gameObjectId, out var oldState))
                        {
                            oldState.Cts?.Cancel();
                            oldState.Cts?.Dispose();
                        }

                        Thread.Sleep(25);
                    }
                }

                if (!addResult)
                {
                    newState.Cts.Dispose();
                    return;
                }

                cts = newState.Cts;
            }

            Task.Run(async () =>
            {
                try
                {
                    ApplyAnimation(character, animationId);
                    Vector3 startPos = player.Position;

                    await Task.Delay(300, cts.Token);

                    while (!cts.Token.IsCancellationRequested)
                    {
                        if (Vector3.Distance(startPos, player.Position) > 0.01f)
                        {
                            break;
                        }

                        if (character.Address == IntPtr.Zero)
                        {
                            break;
                        }

                        await Task.Delay(100, cts.Token);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected when cancelled
                }
                catch (Exception ex)
                {
                    Service.Log.Error(ex, "Error in looping emote task");
                }
                finally
                {
                    lock (_stateLock)
                    {
                        if (_emoteStates.TryGetValue(gameObjectId, out var currentState) && currentState.Cts == cts)
                        {
                            StopEmote(character);
                            _emoteStates.TryRemove(gameObjectId, out _);
                        }
                    }
                }
            }, cts.Token);
        }

        public void StopEmote(ICharacter character)
        {
            if (character == null) return;

            try
            {
                unsafe
                {
                    var characterStruct = (Character*)character.Address;
                    if (characterStruct != null)
                    {
                        characterStruct->Timeline.BaseOverride = 0;
                        Thread.Sleep(15);
                        characterStruct->Timeline.LipsOverride = 0;
                        Thread.Sleep(15);
                        characterStruct->Timeline.BaseOverride = 0;
                        Thread.Sleep(15);
                        characterStruct->Timeline.BaseOverride = 0;

                        IsMinionGroundSitOrSleep = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Error in force stop emote");
                ApplyAnimation(character, 0);
            }
        }

        public void StopLoopingEmote(uint gameObjectId, ICharacter? character = null)
        {
            lock (_stateLock)
            {
                StopLoopingEmoteInternal(gameObjectId, character);
            }
        }

        private void StopLoopingEmoteInternal(uint gameObjectId, ICharacter? character = null)
        {
            if (_emoteStates.TryRemove(gameObjectId, out var state))
            {
                state.Cts.Cancel();
                state.Cts.Dispose();

                if (character != null)
                {
                    StopEmote(character);
                }
            }
        }

        private unsafe void ApplyAnimation(ICharacter character, ushort animationId)
        {
            if (character == null) return;

            try
            {
                var characterStruct = (Character*)character.Address;
                if (characterStruct == null) return;

                if (animationId != 0)
                {
                    characterStruct->Timeline.BaseOverride = 0;
                    Thread.Sleep(15);
                    characterStruct->Timeline.LipsOverride = 0;
                    Thread.Sleep(15);
                    characterStruct->Timeline.BaseOverride = 0;
                    Thread.Sleep(15);
                }

                characterStruct->Timeline.BaseOverride = animationId;

                if (_groundSitAndSleepAnimationIds.Contains(animationId))
                {
                    IsMinionGroundSitOrSleep = true;
                }
                else if (animationId == 0)
                {
                    IsMinionGroundSitOrSleep = false;
                }

                Service.Log.Info($"Applying animation {animationId} to actor {character.Name}");
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Failed to apply animation.");
            }
        }

        public void ApplyFacialExpression(ICharacter character, ushort animationId)
        {
            if (character == null) return;

            Task.Run(async () =>
            {
                SetFacialExpression(character, animationId);
                await Task.Delay(1000);
                StopFacialExpression(character);
            });
        }

        public unsafe void SetFacialExpression(ICharacter character, ushort animationId)
        {
            if (character == null) return;
            try
            {
                var characterStruct = (Character*)character.Address;
                if (characterStruct == null) return;
                characterStruct->Timeline.LipsOverride = animationId;
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Failed to set facial expression.");
            }
        }

        public void StopFacialExpression(ICharacter character)
        {
            SetFacialExpression(character, 0);
        }

        public void Dispose()
        {
            foreach (var entry in _emoteStates.Values)
            {
                entry.Cts.Cancel();
                entry.Cts.Dispose();
            }
            _emoteStates.Clear();
        }

        public bool IsLoopingEmoteActive(uint gameObjectId)
        {
            var result = _emoteStates.TryGetValue(gameObjectId, out var state) && state.IsLooping;
            Service.Log.Debug($"[LOOPING_STATE] IsLoopingEmoteActive for {gameObjectId}: {result}");
            return result;
        }

        public void ClearGroundSitOrSleepState()
        {
            lock (_stateLock)
            {
                IsMinionGroundSitOrSleep = false;
                Service.Log.Debug("[LOOPING_STATE] Minion ground sit/sleep state cleared explicitly.");
            }
        }
    }
}