using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using XIVAICompanion.Configurations;

namespace XIVAICompanion.Managers
{
    public class MinionNamingManager : IDisposable
    {
        private readonly INamePlateGui _namePlateGui;
        private readonly IObjectTable _objectTable;
        private readonly Configuration _configuration;

        private string _currentNamedMinionName = string.Empty;
        private string _currentCustomName = string.Empty;
        private ulong _trackedMinionObjectId = 0;

        public MinionNamingManager(INamePlateGui namePlateGui, IObjectTable objectTable, Configuration configuration)
        {
            _namePlateGui = namePlateGui;
            _objectTable = objectTable;
            _configuration = configuration;

            _namePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
        }

        public void Dispose()
        {
            _namePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
        }

        /// <summary>
        /// Updates the naming configuration. Called when configuration changes.
        /// </summary>
        /// <param name="newMinionName">The minion to name (from MinionToReplace)</param>
        /// <param name="newCustomName">The custom name to apply (from AIName)</param>
        /// <param name="minionObjectId">The specific object ID of the player's minion to rename</param>
        public void UpdateNamingConfiguration(string newMinionName, string newCustomName, ulong minionObjectId = 0)
        {
            if (_currentNamedMinionName != newMinionName || _currentCustomName != newCustomName || _trackedMinionObjectId != minionObjectId)
            {
                _currentNamedMinionName = newMinionName;
                _currentCustomName = newCustomName;
                _trackedMinionObjectId = minionObjectId;

                _namePlateGui.RequestRedraw();
            }
        }

        /// <summary>
        /// Clears the current naming configuration (reverts to original names)
        /// </summary>
        public void ClearNaming()
        {
            _currentNamedMinionName = string.Empty;
            _currentCustomName = string.Empty;
            _trackedMinionObjectId = 0;
            _namePlateGui.RequestRedraw();
        }

        private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
        {
            if (string.IsNullOrEmpty(_currentNamedMinionName) || string.IsNullOrEmpty(_currentCustomName) || _trackedMinionObjectId == 0)
                return;

            foreach (var handler in handlers)
            {
                if (handler.NamePlateKind == NamePlateKind.EventNpcCompanion &&
                    handler.GameObject != null &&
                    handler.GameObject.ObjectKind == ObjectKind.Companion)
                {
                    if (handler.GameObject.GameObjectId == _trackedMinionObjectId)
                    {
                        string currentMinionName = handler.GameObject.Name.TextValue;

                        if (currentMinionName.Contains(_currentNamedMinionName, StringComparison.OrdinalIgnoreCase))
                        {
                            handler.SetField(NamePlateStringField.Name, _currentCustomName);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Forces a nameplate redraw (useful after configuration changes)
        /// </summary>
        public void ForceRedraw()
        {
            _namePlateGui.RequestRedraw();
        }

        /// <summary>
        /// Gets the currently tracked minion name
        /// </summary>
        public string CurrentNamedMinion => _currentNamedMinionName;

        /// <summary>
        /// Gets the current custom name being applied
        /// </summary>
        public string CurrentCustomName => _currentCustomName;

        /// <summary>
        /// Checks if a specific minion is currently being named
        /// </summary>
        /// <param name="minionName">The minion name to check</param>
        /// <returns>True if this minion is currently being renamed</returns>
        public bool IsMinionBeingNamed(string minionName)
        {
            return !string.IsNullOrEmpty(_currentNamedMinionName) &&
                   minionName.Contains(_currentNamedMinionName, StringComparison.OrdinalIgnoreCase);
        }
    }
}