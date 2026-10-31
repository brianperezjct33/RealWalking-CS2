// SPDX-License-Identifier: MIT
using System;
using Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CS2RealWalkingG1Bridge
{
    public sealed partial class ScanHotkeySystem : GameSystemBase
    {
        private bool _reported;
        private bool _lastVChord;
        private bool _lastJChord;
        private bool _lastE;
        private float _lastFireAt = -1000f;

        protected override void OnUpdate()
        {
            try
            {
                if (RWStandaloneFpcConflict.Blocked) return;
                Keyboard keyboard = Keyboard.current;
                bool focused = UnityEngine.Application.isFocused;
                bool ctrl = keyboard != null &&
                    (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
                bool shift = keyboard != null &&
                    (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

                bool eDown = keyboard != null && keyboard.eKey.isPressed;
                bool pressedE = eDown && !_lastE;
                _lastE = eDown;
                bool chordJ = keyboard != null && ctrl && shift && keyboard.jKey.isPressed;
                bool chordV = keyboard != null && ctrl && shift && keyboard.vKey.isPressed;
                bool pressedJ = chordJ && !_lastJChord;
                bool pressedV = chordV && !_lastVChord;
                _lastJChord = chordJ;
                _lastVChord = chordV;

                RWTransitHint.Ensure();
                RWTransitRide.TickRoadExitMotion();
                RWAutoEntry.Tick();
                if (RWTransitRide.Active && !RWTransitFpc.IsFollowingRide(RWTransitRide.Followed))
                    RWTransitRide.End("native FPC follow ended");

                float now = UnityEngine.Time.realtimeSinceStartup;
                if (!focused || now - _lastFireAt < 0.35f) return;

                if (pressedE && !ctrl && !shift)
                {
                    if (RWTransitRide.Active)
                    {
                        if (RWTransitRide.Disembark()) _lastFireAt = now;
                        return;
                    }
                    if (RWTransitState.Ready &&
                        RWWalkState.Mode == RWTravelMode.Navigation && !RWTransitRide.Active)
                    {
                        if (RWTransitFpc.TryBoard(RWTransitState.Candidate)) _lastFireAt = now;
                        return;
                    }
                }

                if (pressedJ)
                {
                    // Boarding leaves the ground solver in Free while the camera follows
                    // a vehicle. J must detach first, rather than toggling back to walk.
                    if (RWTransitRide.Active)
                    {
                        if (RWTransitFpc.TryLeaveRideForFreeFlight(out string reason))
                            _lastFireAt = now;
                        else
                            RWJournal.Warn("[RW-TRANSIT] FREE-FLIGHT-REJECT " + reason);
                        return;
                    }
                    _lastFireAt = now;
                    RWWalkState.ToggleFreeFlight();
                    if (RWWalkState.Mode == RWTravelMode.Navigation) RWWalkingController.Ensure();
                    RWJournal.Warn("[RW-G1] WALK-MODE mode=" + RWWalkState.Mode +
                        " key=Ctrl+Shift+J (Navigation/Free only)");
                    return;
                }

                if (pressedV)
                {
                    _lastFireAt = now;
                    bool enabled = !RWOverlayState.Visible;
                    RWOverlayState.SetUnifiedDebug(enabled);
                    if (enabled) RWOverlayView.Ensure();
                    RWJournal.Warn("[RW-G1] DEBUG-OVERLAY enabled=" + enabled +
                        " key=Ctrl+Shift+V (paths, Navigation Areas, width, radar)");
                }
            }
            catch (Exception ex)
            {
                if (!_reported) RWJournal.Error("[RW-G1] HOTKEY ERROR: " + ex);
                _reported = true;
                Enabled = false;
            }
        }
    }
}
