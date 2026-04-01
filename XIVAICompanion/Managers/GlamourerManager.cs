using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Glamourer.Api.Enums;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using Serilog;
using System;
using System.Collections.Generic;

namespace XIVAICompanion.Managers
{
    public class GlamourerManager
    {
        private readonly IDalamudPluginInterface _pluginInterface;
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
            _pluginInterface = pluginInterface;
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

        public void ApplyDesign(Guid designGuid, IGameObject minion)
        {
            if (!IsApiAvailable || minion == null || designGuid == Guid.Empty) return;

            var base64 = _getDesignBase64.Invoke(designGuid);
            if (string.IsNullOrEmpty(base64))
            {
                Service.Log.Warning($"Could not retrieve base64 for design {designGuid}");
                return;
            }

            if (minion.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion)
            {
                unsafe
                {
                    var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)minion.Address;
                    if (chara == null) return;

                    var originalModelCharaId = chara->ModelContainer.ModelCharaId;

                    byte[] originalCustomize = new byte[26];
                    var customizePtr = (byte*)&chara->DrawData.CustomizeData;
                    for (int i = 0; i < 26; i++)
                        originalCustomize[i] = customizePtr[i];

                    chara->ModelContainer.ModelCharaId = 0;
                    for (int i = 0; i < 26; i++)
                        customizePtr[i] = 1;

                    var minionAddress = minion.Address;
                    EventSubscriber<nint, StateChangeType>? subscriber = null;
                    subscriber = StateChangedWithType.Subscriber(_pluginInterface, (address, changeType) =>
                    {
                        if (address != minionAddress || changeType != StateChangeType.Design) return;

                        chara->ModelContainer.ModelCharaId = originalModelCharaId;
                        for (int i = 0; i < 26; i++)
                            customizePtr[i] = originalCustomize[i];

                        subscriber?.Dispose();
                        subscriber = null;
                    });

                    const ApplyFlag flags = ApplyFlag.Once | ApplyFlag.Customization | ApplyFlag.Equipment;
                    _applyState.Invoke(base64, minion.ObjectIndex, 0, flags);
                }
            }
        }
    }
}