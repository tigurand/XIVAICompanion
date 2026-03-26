using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace XIVAICompanion.Managers
{
    public class GlamourerManager
    {
        private readonly ApiVersion _apiVersion;
        private readonly GetDesignList _getDesignList;
        private readonly GetDesignJObject _getDesignJObject;
        private readonly GetDesignBase64 _getDesignBase64;
        private readonly ApplyDesign _applyByGuid;
        private readonly RevertState _revertState;
        private readonly ApplyState _applyState;

        public bool IsApiAvailable { get; private set; }

        public GlamourerManager(IDalamudPluginInterface pluginInterface)
        {
            _apiVersion = new ApiVersion(pluginInterface);
            try
            {
                IsApiAvailable = _apiVersion.Invoke().Major >= 1;
            }
            catch
            {
                IsApiAvailable = false;
            }

            _getDesignList = new GetDesignList(pluginInterface);
            _getDesignJObject = new GetDesignJObject(pluginInterface);
            _getDesignBase64 = new GetDesignBase64(pluginInterface);
            _applyByGuid = new ApplyDesign(pluginInterface);
            _revertState = new RevertState(pluginInterface);
            _applyState = new ApplyState(pluginInterface);
        }

        public void RecheckApiAvailability()
        {
            if (IsApiAvailable) return;

            try
            {
                IsApiAvailable = _apiVersion.Invoke().Major >= 1;
                if (IsApiAvailable)
                {
                    Log.Information("Successfully connected to Glamourer API after initial failure.");
                }
            }
            catch { /* Do nothing */ }
        }

        public void Revert(IGameObject character)
        {
            if (!IsApiAvailable || character == null) return;
            try
            {
                _revertState.Invoke(character.ObjectIndex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not revert Glamourer state via IPC.");
            }
        }

        public Dictionary<Guid, string> GetDesigns()
        {
            if (!IsApiAvailable) return new Dictionary<Guid, string>();

            try
            {
                return _getDesignList.Invoke() ?? new Dictionary<Guid, string>();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not get Glamourer designs via IPC.");
                return new Dictionary<Guid, string>();
            }
        }

        public void ApplyDesign(Guid designGuid, IGameObject character)
        {
            if (!IsApiAvailable || character == null || designGuid == Guid.Empty) return;

            var base64 = _getDesignBase64.Invoke(designGuid);
            if (string.IsNullOrEmpty(base64))
            {
                Log.Warning($"Could not retrieve base64 for design {designGuid}");
                return;
            }

            if (character.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion)
            {
                unsafe
                {
                    var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)character.Address;
                    if (chara == null) return;

                    var originalModelCharaId = chara->ModelContainer.ModelCharaId;

                    byte[] originalCustomize = new byte[26];
                    var customizePtr = (byte*)&chara->DrawData.CustomizeData;
                    for (int i = 0; i < 26; i++)
                        originalCustomize[i] = customizePtr[i];

                    try
                    {
                        chara->ModelContainer.ModelCharaId = 0;

                        for (int i = 0; i < 26; i++)
                            customizePtr[i] = 1;

                        const ApplyFlag flags = ApplyFlag.Once | ApplyFlag.Customization | ApplyFlag.Equipment;
                        _applyState.Invoke(base64, character.ObjectIndex, 0, flags);
                    }
                    finally
                    {
                        chara->ModelContainer.ModelCharaId = originalModelCharaId;

                        for (int i = 0; i < 26; i++)
                            customizePtr[i] = originalCustomize[i];
                    }
                }
                return;
            }

            try
            {
                Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    const ApplyFlag flags = ApplyFlag.Once | ApplyFlag.Customization | ApplyFlag.Equipment;
                    _applyByGuid.Invoke(designGuid, character.ObjectIndex, 0, flags);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not apply Glamourer design via IPC.");
            }
        }
    }
}