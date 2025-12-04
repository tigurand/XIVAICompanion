using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System;
using System.Linq;

namespace XIVAICompanion.Emoting
{
    public class EmoteHooks : IDisposable
    {
        public Action<IGameObject, ushort>? OnEmote;

        public delegate void OnEmoteFuncDelegate(ulong unk, ulong instigatorAddr, ushort emoteId, ulong targetId, ulong unk2);
        private readonly Hook<OnEmoteFuncDelegate>? _emoteHook;

        public bool IsValid = false;
        private readonly IClientState _clientState;
        private readonly IObjectTable _objectTable;
        private readonly ISigScanner _sigScanner;

        public EmoteHooks(IGameInteropProvider interopProvider, IClientState clientState, IObjectTable objectTable, ISigScanner sigScanner)
        {
            _clientState = clientState;
            _objectTable = objectTable;
            _sigScanner = sigScanner;

            try
            {
                var emoteFuncPtr = "E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24";
                _emoteHook = interopProvider.HookFromSignature<OnEmoteFuncDelegate>(emoteFuncPtr, OnEmoteDetour);
                _emoteHook?.Enable();


                IsValid = true;
                Service.Log.Info("Emote hooks initialized successfully");
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "Failed to initialize emote hooks");
                IsValid = false;
            }
        }

        public void Dispose()
        {
            _emoteHook?.Dispose();
            IsValid = false;
        }

        private void OnEmoteDetour(ulong unk, ulong instigatorAddr, ushort emoteId, ulong targetId, ulong unk2)
        {
            try
            {
                if (_objectTable.LocalPlayer != null)
                {
                    var instigatorObj = _objectTable.FirstOrDefault(x => (ulong)x.Address == instigatorAddr);
                    if (instigatorObj != null)
                    {
                        OnEmote?.Invoke(instigatorObj, emoteId);
                    }
                }
            }
            catch (Exception ex)
            {
                Service.Log.Warning(ex, "Error in emote detour");
            }

            _emoteHook?.Original(unk, instigatorAddr, emoteId, targetId, unk2);
        }

    }
}