// SPDX-License-Identifier: GPL-2.0-only
// RC18: nearest safe walkable surface to the PRE-ENTRY ORBIT CAMERA POSITION.
// Alt+F no longer requires aiming a ray, a screen window, or a terrain reference.
using System;
using FirstPersonCameraContinued.MonoBehaviours;
using FirstPersonCameraContinued.Enums;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    internal static class RWAutoEntry
    {
        internal static bool Pending;
        internal static int RequestId { get; private set; }
        private const float InitialScanRadius = 110f;
        private const float ExpandedScanRadius = 240f;
        private static float _requested;
        private static float _lastAttempt;
        private static float _lastRejectedLog;
        private static int _lastRejectedWater;
        private static bool _hasCamera;
        private static bool _expanded;
        private static Vector3 _cameraPosition;
        private static float _scanRadius;

        internal static void Request()
        {
            // Called from CameraInput.Toggle BEFORE FPC Enable(). Save the
            // actual render camera position once; NEVER sample the moving FPC rig.
            _hasCamera = CaptureOrbitCamera(out _cameraPosition);
            _expanded = false;
            _scanRadius = InitialScanRadius;
            RequestId++;
            Pending = true;
            _requested = Time.realtimeSinceStartup;
            _lastAttempt = 0f;
            _lastRejectedWater = 0;
            _lastRejectedLog = 0f;
            RWJournal.Warn("[RW-AUTOENTRY] REQUEST=" + RequestId +
                " method=camera-nearest-3d captured=" + _hasCamera +
                (_hasCamera ? " camera=" + _cameraPosition.ToString("F1") : "")+
                " scanRadius=" + _scanRadius.ToString("F1"));
        }

        internal static void Cancel()
        {
            Pending = false;
            _hasCamera = false;
        }

        // This signature is retained to avoid affecting periodic walking and
        // transit geometry collection. For an entry request the focus IS the
        // captured orbit camera position, not a look-at point or orbit pivot.
        internal static bool TryGetScanFocus(out Vector3 focus, out int requestId,
            out float searchRadius)
        {
            focus = _cameraPosition;
            requestId = RequestId;
            searchRadius = _scanRadius;
            return Pending && _hasCamera;
        }

        private static bool CaptureOrbitCamera(out Vector3 origin)
        {
            origin = Vector3.zero;
            try
            {
                var world = World.DefaultGameObjectInjectionWorld;
                var cameraSystem = world?.GetExistingSystemManaged<CameraUpdateSystem>();
                Camera orbitCamera = cameraSystem?.activeCamera;
                if (orbitCamera == null) orbitCamera = Camera.main;
                if (orbitCamera == null) return false;
                origin = orbitCamera.transform.position;
                return Finite(origin);
            }
            catch (Exception e)
            {
                RWJournal.Warn("[RW-AUTOENTRY] CAMERA-CAPTURE-FAILED " + e.GetType().Name);
                return false;
            }
        }

        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private static void ExpandQuery(float now, string reason)
        {
            if (_expanded) return;
            _expanded = true;
            _scanRadius = ExpandedScanRadius;
            // The ECS scan system uses RequestId to force a fresh snapshot,
            // even if Ctrl+Shift+V already performed a diagnostic scan.
            RequestId++;
            _requested = now;
            _lastAttempt = 0f;
            RWJournal.Warn("[RW-AUTOENTRY] EXPAND request=" + RequestId +
                " reason=" + reason + " scanRadius=" + _scanRadius.ToString("F1"));
        }

        internal static void Tick()
        {
            if (!Pending || !Application.isFocused) return;
            float now = Time.realtimeSinceStartup;
            if (now < _lastAttempt + .2f) return;
            _lastAttempt = now;
            if (now - _requested > 4.0f)
            {
                if (!_expanded && _hasCamera)
                {
                    ExpandQuery(now, "no eligible nearby snapshot before timeout");
                    return;
                }
                Cancel();
                RWTransitHint.Notify(RWLocalization.T("rw.walk.no_network"));
                var previous = RWOverlayState.Latest;
                RWJournal.Warn("[RW-AUTOENTRY] NO SAFE NEARBY SURFACE; remain free"+
                    (previous == null ? " snapshot=null" :
                    " net=" + previous.Lanes.Length + " nav=" + previous.Triangles.Length +
                    " request=" + previous.EntryRequestId + " expected=" + RequestId +
                    " waterRejected=" + _lastRejectedWater +
                    " scanRadius=" + _scanRadius.ToString("F1")+
                    " status=" + previous.Status + " skipped=" + previous.Skipped));
                return;
            }
            var controller = UnityEngine.Object.FindObjectOfType<FirstPersonCameraController>();
            if (controller == null || controller.GetMode() != CameraMode.Manual) return;
            var snap = RWOverlayState.Latest;
            if (!_hasCamera || snap == null || snap.EntryRequestId != RequestId ||
                snap.SampleAt < _requested || now - snap.SampleAt > 1f) return;

            var water = RWWaterSafety.TryCreate();
            bool picked = RWEntryGeometry.TryPickNearest(snap, _cameraPosition,
                water, out var pick, out RWEntryGeometry.PickDiagnostics diag);
            _lastRejectedWater = diag.WaterRejected;
            if (!picked)
            {
                if (!_expanded)
                {
                    ExpandQuery(now, "no safe pedestrian Net/Area in 110m");
                    return;
                }
                if (now - _lastRejectedLog > 1f)
                {
                    _lastRejectedLog = now;
                    RWJournal.Warn("[RW-AUTOENTRY] PICK-MISS request=" + RequestId+
                        " radius=" + snap.SearchRadius.ToString("F1")+
                        " nav=" + snap.Triangles.Length + " net=" + snap.Lanes.Length+
                        " skipped=" + snap.Skipped + " areaEnabled=" + snap.NavAvailable+
                        " waterData=" + (water != null) + " " + diag);
                }
                return;
            }
            try
            {
                var field = typeof(FirstPersonCameraController).GetField("_model",
                    System.Reflection.BindingFlags.NonPublic|
                    System.Reflection.BindingFlags.Instance);
                var model = field?.GetValue(controller) as
                    FirstPersonCameraContinued.DataModels.CameraDataModel;
                if (model == null) return;
                float eyeY = pick.Point.y + 1.7f;
                RWWalkState.EnableNavigation();
                if (!RWCameraHeight.Publish(eyeY)) return;
                model.Position = new float3(pick.Point.x,eyeY,pick.Point.z);
                RWWalkingController.Ensure();
                Cancel();
                RWJournal.Warn("[RW-AUTOENTRY] ENTRY surface=" + pick.Surface+
                    " xyz=" + pick.Point.ToString("F2")+
                    " camera=" + _cameraPosition.ToString("F2")+
                    " distance3d=" + pick.Distance3D.ToString("F2")+
                    " distanceXZ=" + pick.DistanceXZ.ToString("F2")+
                    " pickMethod=camera-nearest-3d"+
                    " scanRadius=" + snap.SearchRadius.ToString("F1")+
                    " " + diag);
            }
            catch (Exception e)
            {
                Cancel();
                RWJournal.Error("[RW-AUTOENTRY] ERROR " + e);
            }
        }
    }
}
