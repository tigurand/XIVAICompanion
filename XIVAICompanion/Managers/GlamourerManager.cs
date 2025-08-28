using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Glamourer.Api.IpcSubscribers;
using Serilog;
using System;
using System.Collections.Generic;

namespace XIVAICompanion.Managers
{
    public class GlamourerManager
    {
        private readonly ApiVersion _apiVersion;
        private readonly GetDesignList _getDesignList;
        private readonly ApplyDesign _applyByGuid;
        private readonly RevertState _revertState;

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
            _applyByGuid = new ApplyDesign(pluginInterface);
            _revertState = new RevertState(pluginInterface);
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

            try
            {
                _applyByGuid.Invoke(designGuid, character.ObjectIndex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not apply Glamourer design via IPC.");
            }
        }
    }
}