// SPDX-License-Identifier: MIT
// Real Walking navigation: exact pedestrian Net/Area height and bounded movement.
// The controller publishes world-space eye height to the embedded FPC camera.
// Free-flight height and transit-follow behavior are handled separately.
using System;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CS2RealWalkingG1Bridge
{
    internal enum RWTravelMode
    {
        Free,
        Navigation
    }

    /// <summary>
    /// Current walking state shared by the navigation controller and embedded camera.
    /// Runtime state only; no game entities or save data are modified.
    /// </summary>
    internal static class RWWalkState
    {
        internal static RWTravelMode Mode = RWTravelMode.Free;
        internal static string Status = "OFF / FPC free flight";
        internal static string Surface = "none";
        internal static float TargetFootY = float.NaN;
        internal static float SurfaceDistance = float.NaN;
        internal static float Gap = float.NaN;
        internal static bool Locked;
        internal static int Generation;

        // Only a verified Net anchor may initiate the short stair run-up.
        internal static int LastNetId = -1;
        internal static Vector3 NavigationMove = Vector3.zero;

        internal static void ToggleFreeFlight()
        {
            Mode = Mode == RWTravelMode.Free ? RWTravelMode.Navigation : RWTravelMode.Free;
            ResetTracking();
        }

        internal static void EnableNavigation()
        {
            if (Mode == RWTravelMode.Navigation) return;
            Mode = RWTravelMode.Navigation;
            ResetTracking();
        }

        internal static void ForceFree(string reason)
        {
            Mode = RWTravelMode.Free;
            ResetTracking();
            Status = reason;
            RWJournal.Warn("[RW-G1] WALK-RELEASE v0.9.0 reason=" + reason);
        }

        private static void ResetTracking()
        {
            Generation++;
            RWCameraHeight.Clear();
            Locked = false;
            TargetFootY = float.NaN;
            SurfaceDistance = float.NaN;
            Gap = float.NaN;
            Surface = "none";
            LastNetId = -1;
            NavigationMove = Vector3.zero;
            Status = Mode == RWTravelMode.Free ? "FREE / FPC native" : "Seeking " + Mode;
        }
    }

}
