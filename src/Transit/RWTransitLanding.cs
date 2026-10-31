// SPDX-License-Identifier: MIT
// NO game entity creation, no vehicle AI writes, and no save modifications.
using System;
using System.Reflection;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CS2RealWalkingG1Bridge
{
    internal static partial class RWTransitRide
    {
        internal static bool FindLanding(out Vector3 landing,out string info)
        {
            landing=Vector3.zero;info="no nearby walkable surface";
            if(IsRail && !IsBoardingAtRailStop())
            {info="train/metro must be boarding at a stop";return false;}
            if(IsWater && !IsBoardingAtWaterStop())
            {info=Name+" must be boarding at a real quay stop";return false;}
            Vector3 origin;
            if(!TryEye(out origin)){info="ride position unavailable";return false;}
            var snap=RWOverlayState.Latest;
            Vector3 dockAnchor=Vector3.zero;
            bool dockAnchored=false;
            bool nearScan=snap!=null && (new Vector2(origin.x-snap.Eye.x,origin.z-snap.Eye.z)).magnitude<=7f;
            if(IsWater)
            {
                // The dock marker and navigation polygons MUST come from the
                // same scan. A second route read here may temporarily disagree
                // with the earlier scan, especially for large cruise ships.
                // Reject any scan from a different ride or before docking.
                nearScan=snap!=null && snap.WaterAtBerth && snap.WaterRideBody==Body;
                if(nearScan && snap.WaterDockMarker)
                {
                    dockAnchor=snap.WaterDock;
                    dockAnchored=true;
                    float dx=origin.x-dockAnchor.x,dz=origin.z-dockAnchor.z;
                    // Retain the existing physically bounded berth and level checks.
                    float maxDockGap=Kind==RWVehicleKind.Ship?
                        RWTransitRanges.ShipDockMarkerMaxDistance:135f;
                    nearScan = dx*dx+dz*dz<=maxDockGap*maxDockGap &&
                        Mathf.Abs(dockAnchor.y-(origin.y-1.7f))<=16f &&
                        (new Vector2(dockAnchor.x-snap.Eye.x,dockAnchor.z-snap.Eye.z)).magnitude<=8f;
                }
                else if(nearScan)
                {
                    // Markerless ship: the geometry must remain centred on
                    // the current vessel, not on a previous berth.
                    nearScan=(new Vector2(origin.x-snap.Eye.x,origin.z-snap.Eye.z)).magnitude<=7f;
                }
            }
            if(snap==null || snap.SampleAt<=0f ||
               UnityEngine.Time.realtimeSinceStartup-snap.SampleAt>(IsWater?1.9f:1.25f) || !nearScan)
            {
                info="walkable geometry not updated / dock snapshot mismatch"+
                    " age="+(snap==null?"missing":(UnityEngine.Time.realtimeSinceStartup-snap.SampleAt).ToString("F2"))+
                    " sameWaterRide="+(snap!=null && snap.WaterAtBerth && snap.WaterRideBody==Body)+
                    " scanMarker="+(snap!=null && snap.WaterDockMarker);
                return false;
            }
            // S1.7.3: Both metro and mainline trains use the verified platform
            // geometry path. S1.7.2 fixed metro by making the station marker
            // optional; normal trains were still sent through the older mandatory
            // TryStationAnchor gate, rejecting stations without a readable Node/
            // Transform. IsRail covers train-prefab and rail-component cases.
            // PublicTransport.Boarding is checked above for both vehicle kinds.
            // The area/pure-pedestrian-Net safety filters remain unchanged.
            if(IsRail)
                return FindMetroLanding(snap,origin,out landing,out info);
            if(IsWater)
                return FindShipLanding(snap,origin,dockAnchored,dockAnchor,out landing,out info);
            const float maxRadius=14f;
            const float maxHeightDiff=3.4f;
            // Base center is body transform, not seat height / camera offset.
            float baseY=origin.y-1.7f;
            float best=float.PositiveInfinity;
            string source="";
            Vector3 station=Vector3.zero;
            bool hasStation=IsRail && TryStationAnchor(origin,out station);
            int stationAreaCandidates=0,stationNetCandidates=0;
            int stationRoadFiltered=0,stationHeightFiltered=0;
            if(IsRail && !hasStation)
            {
                info="rail stop transform unavailable; no safe platform landing";
                return false;
            }
            // Prioritize a pedestrian Navigation Area near the route's *real*
            // TransportStop. A curbside Net can be track-adjacent too; adjacency
            // alone never proves that a lane belongs to a platform.
            if(IsRail && snap.NavAvailable)
            {
                for(int i=0;i<snap.Triangles.Length;i++)
                {
                    var tri=snap.Triangles[i];
                    Vector3 p=ClosestTrianglePointXZ(tri.A,tri.B,tri.C,origin);
                    stationAreaCandidates++;
                    ConsiderStation(p,"STATION AREA "+tri.AreaId+"/"+tri.TriangleId,
                        origin,station,baseY,maxRadius,maxHeightDiff,0f,
                        ref best,ref landing,ref source);
                    // Sample points to avoid a nav triangle that reaches beneath
                    // the train's body: no arbitrary terrain or track projection.
                    for(int direction=0;direction<8;direction++)
                    {
                        float theta=direction*Mathf.PI*.25f;
                        var probe=new Vector3(origin.x+2.5f*Mathf.Cos(theta),0f,
                                              origin.z+2.5f*Mathf.Sin(theta));
                        Vector3 inside;
                        if(InsideTriXZ(tri.A,tri.B,tri.C,probe,out inside))
                            ConsiderStation(inside,"STATION AREA "+tri.AreaId+"/"+tri.TriangleId,
                                origin,station,baseY,maxRadius,maxHeightDiff,0f,
                                ref best,ref landing,ref source);
                    }
                }
            }
            for(int i=0;i<snap.Lanes.Length;i++)
            {
                var lane=snap.Lanes[i];
                if(lane.Points==null || lane.Points.Length<2)continue;
                // Never accept a curb/shared-road path for a rail disembark.
                // S1.6 additionally required width>=2.8m and track adjacency;
                // that incorrectly rejected narrow *internal* pedestrian lanes
                // on underground subway platforms. Prefer true track-adjacent
                // lanes but accept a station-marker-local pedestrian-only Net
                // with a prefab width >=0.9m (not G1.9 synthetic corridor width).
                if(IsRail)
                {
                    if(lane.RoadMethod || lane.NodeOwner || !lane.WidthFromPrefab ||
                       lane.WidthMeters<.9f)
                    {stationRoadFiltered++;continue;}
                    if(Mathf.Abs(lane.NearY-station.y)>3f)
                    {stationHeightFiltered++;continue;}
                    float markerGap=new Vector2(lane.Points[lane.Points.Length/2].x-station.x,
                        lane.Points[lane.Points.Length/2].z-station.z).magnitude;
                    if(!lane.TrackAdjacent && markerGap>9f)continue;
                    stationNetCandidates++;
                }
                for(int j=1;j<lane.Points.Length;j++)
                {
                    var a=lane.Points[j-1];var b=lane.Points[j];
                    float dx=b.x-a.x,dz=b.z-a.z,den=dx*dx+dz*dz;
                    if(den<1e-6f)continue;
                    Vector3 reference=origin;
                    float t=Mathf.Clamp01(((reference.x-a.x)*dx+(reference.z-a.z)*dz)/den);
                    var p=Vector3.Lerp(a,b,t);
                    if(IsRail)
                        ConsiderStation(p,"STATION PEDESTRIAN NET "+lane.Id+
                            (lane.TrackAdjacent?" TRACK-NEAR":" MARKER-NEAR"),
                            origin,station,baseY,maxRadius,maxHeightDiff,
                            lane.TrackAdjacent?1.25f:2.75f,
                            ref best,ref landing,ref source);
                    else
                        Consider(p,"NET "+lane.Id,origin,baseY,maxRadius,maxHeightDiff,
                            ref best,ref landing,ref source);
                }
            }
            if(snap.NavAvailable && !IsRail)
            {
                for(int i=0;i<snap.Triangles.Length;i++)
                {
                    var tri=snap.Triangles[i];
                    Vector3 p=ClosestTrianglePointXZ(tri.A,tri.B,tri.C,origin);
                    Consider(p,"AREA "+tri.AreaId+"/"+tri.TriangleId,origin,baseY,maxRadius,
                        maxHeightDiff,ref best,ref landing,ref source);
                    for(int direction=0;direction<8;direction++)
                    {
                        float theta=direction*Mathf.PI*.25f;
                        var sample=new Vector3(origin.x+2.8f*Mathf.Cos(theta),0f,
                                               origin.z+2.8f*Mathf.Sin(theta));
                        Vector3 inside;
                        if(InsideTriXZ(tri.A,tri.B,tri.C,sample,out inside))
                            Consider(inside,"AREA "+tri.AreaId+"/"+tri.TriangleId,
                                origin,baseY,maxRadius,maxHeightDiff,
                                ref best,ref landing,ref source);
                    }
                }
            }
            if(float.IsInfinity(best))
            {
                if(IsRail)
                {
                    info="no verified platform: areas="+stationAreaCandidates+
                        " pedestrianNets="+stationNetCandidates+
                        " filteredRoad="+stationRoadFiltered+
                        " filteredHeight="+stationHeightFiltered+
                        " navAvailable="+snap.NavAvailable+
                        " station="+station.ToString("F1")+
                        " ride="+origin.ToString("F1");
                }
                return false;
            }
            // Make room for the player's eyes; no height interpolation.
            landing.y+=1.7f;
            info=source+" gap="+best.ToString("F2")+"m";
            return true;
        }
    }
}
