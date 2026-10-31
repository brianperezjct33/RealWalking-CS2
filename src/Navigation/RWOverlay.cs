// SPDX-License-Identifier: MIT
// Read-only ECS pedestrian geometry snapshots (Net and Navigation Area).
// No game entity writes. Rendering is isolated in RWOverlayView.cs.
using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    // Shared read-only Net search visitor. Used by RWOverlayScanSystem.
    internal struct LaneVisitor : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
    {
        public float2 CenterXZ;
        public float Radius;
        public NativeList<Entity> Matches;
        public int CandidateCount;
        public int DroppedCount;
        public bool Intersect(QuadTreeBoundsXZ bounds)
        {
            Bounds3 b = bounds.m_Bounds;
            return b.max.x >= CenterXZ.x - Radius && b.min.x <= CenterXZ.x + Radius
                && b.max.z >= CenterXZ.y - Radius && b.min.z <= CenterXZ.y + Radius;
        }
        public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
        {
            if (!Intersect(bounds)) return;
            CandidateCount++;
            if (Matches.Length < 4096) Matches.Add(item);
            else DroppedCount++;
        }
    }


    internal static class RWOverlayState
    {
        internal static bool Visible;
        internal static bool AllLayers;
        internal static bool ShowNavigation=true;
        internal static bool ShowCorridor=false;
        internal static bool ShowRadar=false;
        internal static RWOverlaySnapshot? Latest;

        // One visible switch, one complete set of diagnostics.
        internal static void SetUnifiedDebug(bool enabled)
        {
            Visible=enabled;
            ShowRadar=enabled;
            ShowCorridor=enabled;
            ShowNavigation=enabled;
            AllLayers=false; // Avoid drawing distant upper/lower geometry by default.
        }
    }

    internal sealed class RWOverlayPolyline
    {
        internal Vector3[] Points = Array.Empty<Vector3>();
        internal float DistanceXZ;
        internal float NearY;
        internal int Id;
        internal bool NodeOwner;
        internal bool TrackAdjacent;
        internal bool RoadMethod; // pedestrian lane also advertises Road path method (potential curb/shared road)
        internal float WidthMeters; // raw prefab lane width; NOT a validated pavement width
        internal float WalkableHalfWidth; // controller/overlay use same heuristic; not collision geometry
        internal string WidthRule = string.Empty;
        internal bool WidthFromPrefab;
    }

    internal sealed class RWOverlayTriangle
    {
        internal Vector3 A,B,C;
        internal float NearY;
        internal float DistanceXZ;
        internal int AreaId,TriangleId;
    }

    internal sealed class RWOverlaySnapshot
    {
        internal Vector3 Eye;
        internal float SampleAt;
        internal int EntryRequestId;
        internal Vector3 Forward;
        internal float FootY;
        internal float SearchRadius=24f;
        internal string Status = string.Empty;
        internal RWOverlayPolyline[] Lanes = new RWOverlayPolyline[0];
        internal RWOverlayTriangle[] Triangles = new RWOverlayTriangle[0];
        internal int TotalPed,TotalNav,Skipped;
        internal double QueryMilliseconds;
        internal bool NavAvailable;
        // Ship-berth scan context: keep the dock marker used to collect this
        // exact geometry snapshot. Never compare it with a later route read.
        internal Entity WaterRideBody = Entity.Null;
        internal bool WaterAtBerth;
        internal bool WaterDockMarker;
        internal Vector3 WaterDock;
    }

    // Sampling is kept out of MonoBehaviour.OnGUI. It is read-only and throttled to ~2.5 Hz.
    public sealed partial class RWOverlayScanSystem : GameSystemBase
    {
        private const float Radius=24f;
        private const int MaxNetOwners=256;
        private const int MaxCandidateLanes=400;
        private const int MaxShowLanes=96; // Keep richer data for vertical controller even when overlay hides most lines.
        private const int MaxShowTriangles=220; // Prevent dense stations exhausting the old 74-triangle navigation cache.
        private const float EyeHeight=1.7f;
        // G1.9 gameplay heuristic, NOT a claim about the physical pavement mesh.
        // The same effective corridor is drawn in yellow and used by movement.
        private const float MinimumWalkwayWidth=3.0f; // TRUE playable total width, never reduced by safety inset
        // RC16: the ONLY road prefab allowed to widen a Net pedestrian lane.
        // Use the *edge road prefab*, never the spawned pedestrian lane prefab.
        // No width-based inference and no generic Road-method expansion.
        private const float PedestrianStreetMultiplier=2.0f;
        private const float MaxWalkwayHalfWidth=9.0f; // existing G1.9 safety cap
        private float _nextSample;
        private int _lastSampledEntryRequestId;
        private bool _reportedError;
        private readonly HashSet<int> _reportedRailGuards=new HashSet<int>();
        private readonly HashSet<int> _reportedWaterGuards=new HashSet<int>();
        private readonly Dictionary<Entity,string> _roadPrefabNames=new Dictionary<Entity,string>();
        private readonly HashSet<string> _reportedRoadPrefabs=new HashSet<string>(StringComparer.Ordinal);

        // An exact, case-insensitive prefab-name allowlist: this is intentionally
        // NOT a Contains("Pedestrian Street") check; Small Pedestrian Street and
        // custom prefabs with a longer name must retain their normal lane width.
        private static bool IsVanillaPedestrianStreetName(string name)
        {
            if(string.IsNullOrWhiteSpace(name))return false;
            string n=name.Trim();
            return string.Equals(n,"Pedestrian Street",StringComparison.OrdinalIgnoreCase)
                || string.Equals(n,"PedestrianStreet",StringComparison.OrdinalIgnoreCase);
        }

        private string RoadPrefabName(Entity owner, Game.Prefabs.PrefabSystem prefabs)
        {
            if(prefabs==null || !EntityManager.HasComponent<Edge>(owner) ||
                !EntityManager.HasComponent<Game.Prefabs.PrefabRef>(owner))return "";
            Entity prefab=EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(owner).m_Prefab;
            if(prefab==Entity.Null || !EntityManager.Exists(prefab))return "";
            if(_roadPrefabNames.TryGetValue(prefab,out string name))return name;
            // PrefabSystem is the same game API already used by RWStopNames.
            try { name=prefabs.GetPrefabName(prefab) ?? ""; }
            catch { name=""; }
            if(_roadPrefabNames.Count>=1024)_roadPrefabNames.Clear();
            _roadPrefabNames[prefab]=name;
            return name;
        }

        private static float3 BezierAt(Bezier4x3 c, float t)
        {
            float u=1f-t;
            return c.a*(u*u*u)+c.b*(3f*u*u*t)+c.c*(3f*u*t*t)+c.d*(t*t*t);
        }
        private static Vector3 V(float3 v) { return new Vector3(v.x,v.y,v.z); }
        private static float ClosestPolylineXZ(Vector3[] pts, Vector3 point, out float atY)
        {
            float best=float.PositiveInfinity;
            atY=0f;
            // Use true closest point to each sampled segment; avoids selecting a remote curve endpoint.
            for(int i=1;i<pts.Length;i++)
            {
                Vector3 a=pts[i-1],b=pts[i];
                float dx=b.x-a.x,dz=b.z-a.z;
                float denom=dx*dx+dz*dz;
                float t=denom<0.00001f?0f:Mathf.Clamp01(((point.x-a.x)*dx+(point.z-a.z)*dz)/denom);
                float px=a.x+t*dx,pz=a.z+t*dz;
                float dd=(point.x-px)*(point.x-px)+(point.z-pz)*(point.z-pz);
                if(dd<best){best=dd;atY=a.y+t*(b.y-a.y);}
            }
            return Mathf.Sqrt(best);
        }

        protected override void OnUpdate()
        {
            if (!RWOverlayState.Visible && !RWOverlayState.ShowRadar &&
                RWWalkState.Mode != RWTravelMode.Navigation && !RWTransitRide.Active && !RWAutoEntry.Pending) return;
            // G1.9.1: do not build ECS geometry snapshots when the game loses focus.
            if (!UnityEngine.Application.isFocused) return;
            float now=UnityEngine.Time.realtimeSinceStartup;
            // Never let a previous navigation / width-overlay sampling interval
            // delay a NEW Alt+F aim request. This costs one fresh, read-only scan
            // and does not enable/disable any debug visuals.
            bool hasEntryAim=RWAutoEntry.TryGetScanFocus(
                out Vector3 _,out int pendingEntryId,out float _unusedRadius);
            bool newEntry=hasEntryAim && pendingEntryId!=_lastSampledEntryRequestId;
            if(now<_nextSample && !newEntry) return;
            // ECS search trees can block on dependency.Complete(): do not rescan 8.3x/s.
            // Valid snapshot is reused between scans; active walker interpolates geometry each frame.
            _nextSample=now+(RWTransitRide.IsWater && RWTransitRide.IsBoardingAtWaterStop()?0.95f:(RWWalkState.Mode==RWTravelMode.Navigation?0.24f:0.55f));
            var snap=new RWOverlaySnapshot();
            RWOverlayState.Latest=snap;
            try
            {
                Vector3 eye,forward;
                float offset;
                string why;
                if(RWAutoEntry.TryGetScanFocus(out Vector3 entryFocus, out int entryRequestId,
                    out float entryScanRadius))
                {
                    // RC18: entryFocus is the pre-toggle orbit CAMERA position.
                    // Only the entry snapshot uses this query centre; active
                    // walking, transport, and debug drawing remain unchanged.
                    eye=entryFocus;
                    forward=Vector3.forward;
                    offset=0f;
                    why="entry-camera-nearest";
                    snap.SearchRadius=entryScanRadius;
                    snap.EntryRequestId=entryRequestId;
                    _lastSampledEntryRequestId=entryRequestId;
                }
                else if(RWTransitRide.Active)
                {
                    if(!RWTransitRide.TryEye(out eye))
                    {snap.Status="transit position unavailable";return;}
                    forward=Vector3.forward;offset=0f;why="ride";
                    if(RWTransitRide.IsWater && RWTransitRide.IsBoardingAtWaterStop())
                    {
                        snap.WaterAtBerth = true;
                        snap.WaterRideBody = RWTransitRide.Body;
                        Vector3 dock;
                        if(RWTransitRide.TryWaterDockAnchor(eye,out dock))
                        {
                            // Preserve the same marker and ride identity as the
                            // actual pedestrian geometry sampled this frame.
                            snap.WaterDockMarker = true;
                            snap.WaterDock = dock;
                            // A cruise ship's central pivot can be far offshore.
                            // Query pedestrians near its real stop marker instead.
                            eye.x=dock.x;eye.z=dock.z;
                            snap.SearchRadius=RWTransitRide.Kind==RWVehicleKind.Ship?
                                RWTransitRanges.ShipDockMarkerScan:36f;
                            why=RWTransitRide.Kind==RWVehicleKind.Ship?"cruise-ship-dock-marker":"ferry-dock-marker";
                        }
                        else
                        {
                            // Markerless dock assets: bounded wider search *only*
                            // while the vessel is in the Boarding state.
                            snap.SearchRadius=RWTransitRide.Kind==RWVehicleKind.Ship?
                                RWTransitRanges.ShipNoMarkerScan:78f;
                            why=RWTransitRide.Kind==RWVehicleKind.Ship?"cruise-ship-wide-quay":"ferry-wide-quay";
                        }
                    }
                }
                else if(!ExistingFpcAdapter.TryGetFreeCamera(out eye,out forward,out offset,out why))
                {
                    snap.Status=why;
                    return;
                }
                snap.SampleAt=now;
                snap.Eye=eye;
                snap.Forward=forward;
                snap.FootY=eye.y-EyeHeight;
                var watch=System.Diagnostics.Stopwatch.StartNew();
                CollectNet(snap);
#if RW_AREA
                CollectAreas(snap);
#else
                snap.Status="Navigation Area not compiled: use Build-G1-Areas.cmd";
#endif
                watch.Stop();
                snap.QueryMilliseconds=watch.Elapsed.TotalMilliseconds;
                if(string.IsNullOrEmpty(snap.Status))snap.Status="READ ONLY / candidate geometry, not collision proof";
            }
            catch(Exception ex)
            {
                snap.Status="OVERLAY query failed; open Player.log. " + ex.GetType().Name;
                if(!_reportedError)RWJournal.Error("[RW-G1] OVERLAY-ERROR v0.4.0: "+ex);
                _reportedError=true;
                // Disable overlay querying on error rather than risking repeated ECS exceptions.
                RWOverlayState.SetUnifiedDebug(false);
            }
        }

        private static void TrimEntryLayers<T>(List<T> candidates,int limit,Func<T,float> height) where T:class
        {
            if(candidates.Count<=limit)return;
            var selected=new List<T>(limit);
            var selectedSet=new HashSet<T>();
            var countByBand=new Dictionary<int,int>();
            int perBand=Mathf.Max(28,limit/5);
            foreach(var item in candidates)
            {
                int band=Mathf.FloorToInt(height(item)/4f);
                countByBand.TryGetValue(band,out int count);
                if(count>=perBand)continue;
                selected.Add(item);selectedSet.Add(item);countByBand[band]=count+1;
                if(selected.Count==limit)break;
            }
            if(selected.Count<limit)
                foreach(var item in candidates)
                {
                    if(selectedSet.Contains(item))continue;
                    selected.Add(item);
                    if(selected.Count==limit)break;
                }
            candidates.Clear();candidates.AddRange(selected);
        }

    }

    // Screen-space render on top of the game. It intentionally ignores occlusion:
    // overlay lines behind buildings still show; this is a diagnostic X-ray, NOT collision geometry.
}
