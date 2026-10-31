// SPDX-License-Identifier: MIT
using System;
using Game;
using Unity.Entities;
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    public sealed partial class RWStandaloneCameraRecoverySystem : GameSystemBase
    {
        private float _nextCheck;
        private int _failedAttempts;
        private bool _reportedRecovery;

        protected override void OnUpdate()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextCheck) return;
            _nextCheck = now + 1.5f;
            // Late FPC load cannot be made safe in-process: stop recovering our
            // controller and require a clean restart with one edition enabled.
            if (RWStandaloneFpcConflict.Check())
            {
                Enabled = false;
                return;
            }
            var camera = World.GetExistingSystemManaged<FirstPersonCameraContinued.Systems.FirstPersonCameraSystem>();
            if (camera == null) return; // FPC creates this on game loading completion.
            bool recovered = false;
            try { recovered = camera.TryRecoverController(); }
            catch (Exception ex)
            {
                RWJournal.Error("[RW-STANDALONE] CAMERA-RECOVERY-EXCEPTION: " + ex);
            }
            if (recovered)
            {
                if (_failedAttempts > 0 && !_reportedRecovery)
                    RWJournal.Warn("[RW-STANDALONE] CAMERA-RECOVERED attempts=" + (_failedAttempts + 1));
                _failedAttempts = 0;
                _reportedRecovery = false;
                return;
            }

            _failedAttempts++;
            if (_failedAttempts == 1 || _failedAttempts == 5)
                RWJournal.Warn("[RW-STANDALONE] CAMERA-RECOVERY-PENDING attempt=" + _failedAttempts);
            if (_failedAttempts >= 8)
            {
                RWJournal.Error("[RW-STANDALONE] CAMERA-RECOVERY-FAILED after 8 UIUpdate attempts. Stop testing, restore previous build and upload game log.");
                Enabled = false; // Prevent unlimited GameObject creation attempts.
            }
        }
    }
}
