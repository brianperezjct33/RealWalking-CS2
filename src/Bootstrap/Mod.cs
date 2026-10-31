// SPDX-License-Identifier: MIT
using Colossal.Logging;
using Game;
using Game.Modding;
using Unity.Entities;

namespace CS2RealWalkingG1Bridge
{
    public sealed class Mod : IMod
    {
        // Standalone integration (2026-10-29): the only loader IMod owns the bundled camera core.
        private FirstPersonCameraContinued.Mod _embeddedCamera;
        internal static readonly ILog Log = LogManager.GetLogger("CS2RealWalkingG1Bridge").SetShowsErrorsInUI(false);
        public void OnLoad(UpdateSystem updateSystem)
        {
            // Do not register any input handlers or camera patches if an external FPC
            // has already been loaded.  This is a guard, not a dual-mod compatibility layer.
            if (RWStandaloneFpcConflict.Check()) return;
            // Install the camera's original patches, settings and input before Real Walking systems.
            _embeddedCamera = new FirstPersonCameraContinued.Mod();
            _embeddedCamera.OnLoad(updateSystem, this);
            RWJournal.Warn("[RW-STANDALONE] Embedded FPC camera initialized; external FPC is NOT required.");
            RWJournal.Warn("[RW-STANDALONE] Navigation/Free walking, camera-nearest entry, transit; unified diagnostics Ctrl+Shift+V.");
            RWJournal.Warn("[RW-G1] JOURNAL session=" + RWJournal.Session + " path=" + RWJournal.JournalFile);
            // Camera lifetime watchdog: run recovery outside rendering/PreCulling.
            updateSystem.UpdateAt<RWStandaloneCameraRecoverySystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ScanHotkeySystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<RWOverlayScanSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<RWTransitNearbySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<RWStopStripSystem>(SystemUpdatePhase.UIUpdate);
        }
        public void OnDispose() { RWWalkState.Mode = RWTravelMode.Free; RWWalkingController.Release(); RWOverlayView.Release(); RWTransitRide.End("mod disposed"); RWTransitHint.Release(); RWStopStripHud.Release(); RWAutoEntry.Cancel(); if (_embeddedCamera != null) _embeddedCamera.OnDispose(); }
    }
}
