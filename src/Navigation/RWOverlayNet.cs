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
    // Read-only pedestrian Net sampling; never changes vehicle or pedestrian AI.
    public sealed partial class RWOverlayScanSystem
    {
        private void CollectNet(RWOverlaySnapshot snap)
        {
            var system=World.GetExistingSystemManaged<Game.Net.SearchSystem>();
            if(system==null){snap.Status="Game.Net.SearchSystem unavailable";return;}
            JobHandle dependency;
            var tree=system.GetNetSearchTree(true,out dependency);
            dependency.Complete();
            var matches=new NativeList<Entity>(Allocator.Temp);
            try
            {
                var visitor=new LaneVisitor{
                    CenterXZ=new float2(snap.Eye.x,snap.Eye.z),Radius=snap.SearchRadius,Matches=matches
                };
                tree.Iterate(ref visitor,0);
                var seenLanes=new HashSet<Entity>();
                var seenOwners=new HashSet<Entity>();
                var roadPrefabs=World.GetExistingSystemManaged<Game.Prefabs.PrefabSystem>();
                var output=new List<RWOverlayPolyline>(128);
                var trackCurves=new List<Vector3[]>(64); // TrackLane nearby => avoid expanding platform into rails.
                for(int i=0;i<matches.Length;i++)
                {
                    Entity owner=matches[i];
                    if(!seenOwners.Add(owner)||!EntityManager.Exists(owner)||!EntityManager.HasBuffer<SubLane>(owner))continue;
                    int maxOwners=snap.EntryRequestId!=0?4096:(snap.SearchRadius>90f?900:(snap.SearchRadius>24f?640:MaxNetOwners));
                    if(seenOwners.Count>maxOwners){snap.Skipped++;break;}
                    bool isNode=EntityManager.HasComponent<Node>(owner);
                    // Only the exact Pedestrian Street ROAD prefab may be 2x wide.
                    // Nodes, building connection lanes, docks and other Edge roads
                    // retain their own unexpanded prefab lane width.
                    string roadPrefabName=isNode?"":RoadPrefabName(owner,roadPrefabs);
                    bool targetPedestrianStreet=IsVanillaPedestrianStreetName(roadPrefabName);
                    var subs=EntityManager.GetBuffer<SubLane>(owner);
                    for(int j=0;j<subs.Length;j++)
                    {
                        SubLane record=subs[j];
                        Entity lane=record.m_SubLane;
                        if(!seenLanes.Add(lane)||!EntityManager.Exists(lane))continue;
                        bool ped=(record.m_PathMethods & Game.Pathfind.PathMethod.Pedestrian)!=0;
                        // A pedestrian-lane width can describe a station platform. Preserve
                        // conservative G1.6 bounds next to same-level TRACK lanes.
                        if(!ped && !EntityManager.HasComponent<PedestrianLane>(lane))
                        {
                            if(EntityManager.HasComponent<TrackLane>(lane) && EntityManager.HasComponent<Curve>(lane))
                            {
                                var rail=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                                var railPoints=new Vector3[9];
                                for(int k=0;k<railPoints.Length;k++)railPoints[k]=V(BezierAt(rail,(float)k/(railPoints.Length-1)));
                                trackCurves.Add(railPoints);
                            }
                            continue;
                        }
                        if(!EntityManager.HasComponent<Curve>(lane))continue;
                        snap.TotalPed++;
                        var bezier=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                        // 12 segments, with segment-based XZ closest point.
                        var points=new Vector3[9];
                        for(int k=0;k<points.Length;k++)points[k]=V(BezierAt(bezier,(float)k/8f));
                        float nearY;
                        float distance=ClosestPolylineXZ(points,snap.Eye,out nearY);
                        if(distance>snap.SearchRadius)continue;
                        int maxCandidates=snap.SearchRadius>90f?1100:(snap.SearchRadius>24f?750:MaxCandidateLanes);
                        // Entry scans are temporary and use the whole spatial tree
                        // result. Early caps here used tree iteration order rather
                        // than proximity, silently discarding central sidewalks.
                        if(snap.EntryRequestId==0 && output.Count>=maxCandidates)
                        {snap.Skipped++;continue;}
                        // Width is read from the actual spawned lane prefab; NodeLane offsets
                        // can make its two ends different widths. Use the narrower end conservatively.
                        // This is the game's PATH/LANE width, not a verified pavement boundary.
                        float laneWidth=1.50f; bool widthKnown=false;
                        if(EntityManager.HasComponent<Game.Prefabs.PrefabRef>(lane))
                        {
                            var prefab=EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(lane).m_Prefab;
                            if(EntityManager.Exists(prefab)&&EntityManager.HasComponent<Game.Prefabs.NetLaneData>(prefab))
                            {
                                laneWidth=EntityManager.GetComponentData<Game.Prefabs.NetLaneData>(prefab).m_Width;
                                if(EntityManager.HasComponent<NodeLane>(lane))
                                {
                                    var offsets=EntityManager.GetComponentData<NodeLane>(lane).m_WidthOffset;
                                    laneWidth+=Mathf.Min(offsets.x,offsets.y);
                                }
                                widthKnown=laneWidth>=0.40f&&laneWidth<=30f;
                            }
                        }
                        if(!widthKnown)laneWidth=1.50f;
                        // RC16: pedestrian Net width is never inferred from lane size
                        // or the generic Road path method. Double only the EXACT
                        // vanilla Pedestrian Street edge prefab, and only when the
                        // spawned lane reports a credible prefab width.
                        // Keep the long-standing 3.0m minimum and 18m safety cap.
                        bool applyStreetMultiplier=!isNode && widthKnown && targetPedestrianStreet;
                        float requestedWalkableWidth=Mathf.Max(MinimumWalkwayWidth,
                            laneWidth*(applyStreetMultiplier?PedestrianStreetMultiplier:1.0f));
                        float halfWidth=Mathf.Clamp(requestedWalkableWidth*.5f,
                            MinimumWalkwayWidth*.5f,MaxWalkwayHalfWidth);
                        string rule=applyStreetMultiplier?"vanilla-pedestrian-street-x2":
                            !widthKnown?"minimum-fallback":"normal-min";
                        // Once per distinct road prefab, expose the *actual* runtime
                        // name so we can diagnose an unexpected vanilla internal ID.
                        // No modded-prefab guessing or wildcard amplification.
                        if(!isNode && widthKnown && (laneWidth>=3.6f || targetPedestrianStreet) &&
                           !string.IsNullOrEmpty(roadPrefabName) &&
                           (_reportedRoadPrefabs.Count<64 || targetPedestrianStreet) &&
                           _reportedRoadPrefabs.Add(roadPrefabName))
                            RWJournal.Trace("[RW-WIDTH] ROAD-PREFAB name='"+roadPrefabName+
                                "' pedestrianStreetX2="+targetPedestrianStreet+
                                " laneWidth="+laneWidth.ToString("F2"));
                        output.Add(new RWOverlayPolyline{
                            Points=points,DistanceXZ=distance,NearY=nearY,
                            Id=lane.Index,NodeOwner=isNode,
                            RoadMethod=(record.m_PathMethods & Game.Pathfind.PathMethod.Road)!=0,
                            WidthMeters=laneWidth,WidthFromPrefab=widthKnown,
                            WalkableHalfWidth=halfWidth,WidthRule=rule
                        });
                    }
                }
                // Waterfront safety check ONLY for inferred inflated Net corridors.
                // If either edge of the envelope reaches open water, retract to
                // the real prefab width; the pedestrian Net centerline still
                // remains available. This does not alter Navigation Areas.
                RWWaterSafety waterGuard=null;
                bool triedWaterGuard=false;
                for(int i=0;i<output.Count;i++)
                {
                    var walk=output[i];
                    float rawHalf=Mathf.Clamp(walk.WidthMeters*.5f-.20f,
                        MinimumWalkwayWidth*.5f,6.0f);
                    if(walk.WalkableHalfWidth<=rawHalf+.30f || walk.Points==null)continue;
                    if(!triedWaterGuard)
                    {
                        waterGuard=RWWaterSafety.TryCreate();
                        triedWaterGuard=true;
                    }
                    if(waterGuard==null || !waterGuard.ReachesOpenWater(walk.Points, walk.WalkableHalfWidth))continue;
                    walk.WalkableHalfWidth=rawHalf;
                    walk.WidthRule="waterfront-prefab-guard";
                    if(_reportedWaterGuards.Count<128 && _reportedWaterGuards.Add(walk.Id))
                        RWJournal.Trace("[RW-WIDTH] WATERFRONT-GUARD lane="+walk.Id+
                            " prefab="+walk.WidthMeters.ToString("F2")+
                            " half="+rawHalf.ToString("F2"));
                }
                // G1.9 falsely doubled any wide Edge lane, including some station
                // platforms. Detect a same-layer track next to the pedestrian path
                // and restore G1.6's conservative prefab-based width for that lane.
                // This is an empirical heuristic, not a station prefab classifier.
                for(int i=0;i<output.Count;i++)
                {
                    var walk=output[i];
                    if(walk.Points==null||walk.Points.Length<2)continue;
                    var middle=walk.Points[walk.Points.Length/2];
                    bool besideRail=false;
                    for(int j=0;j<trackCurves.Count;j++)
                    {
                        float railY;
                        float d=ClosestPolylineXZ(trackCurves[j],middle,out railY);
                        if(d<=9.0f && Mathf.Abs(railY-middle.y)<=3.0f)
                        {besideRail=true;break;}
                    }
                    walk.TrackAdjacent=besideRail;
                    if(besideRail)
                    {
                        walk.WalkableHalfWidth=Mathf.Clamp(walk.WidthMeters*.5f-.12f,.30f,6f);
                        walk.WidthRule="rail-adjacent-G1.6-guard";
                        if(_reportedRailGuards.Count<128 && _reportedRailGuards.Add(walk.Id))
                            RWJournal.Trace("[RW-G1] WALK-PLATFORM-GUARD v0.9.0 lane="+walk.Id+
                                " prefabWidth="+walk.WidthMeters.ToString("F2")+
                                " actualTotalWidth="+(walk.WalkableHalfWidth*2f).ToString("F2")+
                                " rule="+walk.WidthRule);
                    }
                }
                float rankY=RWWalkState.Mode==RWTravelMode.Navigation &&
                    !float.IsNaN(RWWalkState.TargetFootY)?RWWalkState.TargetFootY:snap.FootY;
                if(snap.EntryRequestId!=0)output.Sort((a,b)=>(a.DistanceXZ*a.DistanceXZ+
                    (a.NearY-snap.Eye.y)*(a.NearY-snap.Eye.y)).CompareTo(
                    b.DistanceXZ*b.DistanceXZ+(b.NearY-snap.Eye.y)*(b.NearY-snap.Eye.y)));
                else output.Sort((a,b)=>(a.DistanceXZ+1.75f*Mathf.Abs(a.NearY-rankY)).CompareTo(b.DistanceXZ+1.75f*Mathf.Abs(b.NearY-rankY)));
                int maxLanes=snap.EntryRequestId!=0?3500:
                    (snap.SearchRadius>90f?350:(snap.SearchRadius>24f?220:MaxShowLanes));
                // Do not suppress the nearest 3D candidate due to the old
                // per-height-band quota; closest first is already ordered.
                if(output.Count>maxLanes){snap.Skipped+=output.Count-maxLanes;output.RemoveRange(maxLanes,output.Count-maxLanes);}
                snap.Lanes=output.ToArray();
                snap.Skipped+=visitor.DroppedCount;
            }
            finally{matches.Dispose();}
        }

    }
}
