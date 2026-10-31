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
    // Station platform geometry, metro landing and platform tests.
    internal static partial class RWTransitRide
    {
        private static bool FindMetroLanding(RWOverlaySnapshot snap, Vector3 origin,
            out Vector3 landing,out string info)
        {
            landing=Vector3.zero;
            info=Name+": no same-layer station pedestrian geometry";
            Vector3 marker;
            bool hasMarker=TryStationAnchor(origin,out marker);
            const float searchRadius=19f;  // RWOverlay scans 24m at the boarded car
            const float maxHeight=4.2f;
            float baseY=origin.y-1.7f; // physical car transform height, not cab eye
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null){info=Name+": game world unavailable";return false;}
            var em=world.EntityManager;
            Entity physical=em.Exists(Body)?Body:Followed;
            Vector3 carPos;
            UnityEngine.Quaternion carRot;
            bool havePose=TryPose(physical,em,out carPos,out carRot);
            Vector3 axis=havePose ? carRot*Vector3.forward : Vector3.zero;
            axis.y=0f;
            bool haveAxis=axis.sqrMagnitude>.25f;
            if(haveAxis)axis.Normalize();
            float best=float.PositiveInfinity;
            string chosen="";
            int areas=0,areaRejected=0,nets=0,roadExcluded=0,netRejected=0;
            if(snap.NavAvailable && snap.Triangles!=null)
            {
                foreach(var tri in snap.Triangles)
                {
                    // No steep stair or sloped roof candidate for a standing
                    // platform. This does not alter the normal walking controller.
                    float minY=Mathf.Min(tri.A.y,Mathf.Min(tri.B.y,tri.C.y));
                    float maxY=Mathf.Max(tri.A.y,Mathf.Max(tri.B.y,tri.C.y));
                    if(maxY-minY>1.6f){areaRejected++;continue;}
                    areas++;
                    Vector3 nearest=ClosestTrianglePointXZ(tri.A,tri.B,tri.C,origin);
                    TryMetroPoint(nearest,"METRO AREA "+tri.AreaId+"/"+tri.TriangleId,
                        origin,baseY,marker,hasMarker,haveAxis,axis,
                        searchRadius,maxHeight,0f,ref best,ref landing,ref chosen);
                    // A polygon can extend under the rolling stock; search
                    // physically offset points on both platform sides as well.
                    for(int ring=0;ring<2;ring++)
                    {
                        float radius=ring==0?5.0f:8.0f;
                        for(int direction=0;direction<12;direction++)
                        {
                            float angle=direction*Mathf.PI/6f;
                            Vector3 probe=new Vector3(origin.x+radius*Mathf.Cos(angle),0f,
                                                      origin.z+radius*Mathf.Sin(angle));
                            Vector3 inside;
                            if(InsideTriXZ(tri.A,tri.B,tri.C,probe,out inside))
                                TryMetroPoint(inside,"METRO AREA "+tri.AreaId+"/"+tri.TriangleId,
                                    origin,baseY,marker,hasMarker,haveAxis,axis,
                                    searchRadius,maxHeight,0f,ref best,ref landing,ref chosen);
                        }
                    }
                }
            }
            if(snap.Lanes!=null)
            {
                foreach(var lane in snap.Lanes)
                {
                    if(lane.Points==null||lane.Points.Length<2)continue;
                    // RoadMethod is the key curb filter; do not forbid
                    // NodeOwner: underground station walk lanes can be node-owned.
                    if(lane.RoadMethod){roadExcluded++;continue;}
                    if(lane.WidthFromPrefab && lane.WidthMeters<.80f){netRejected++;continue;}
                    if(Mathf.Abs(lane.NearY-baseY)>maxHeight){netRejected++;continue;}
                    // A pure pedestrian track-adjacent lane is a strong platform
                    // candidate. Otherwise require a route marker nearby; do not
                    // treat unrelated tunnel pedestrian lanes as a platform.
                    bool qualified=lane.TrackAdjacent;
                    if(!qualified && hasMarker)
                    {
                        var mid=lane.Points[lane.Points.Length/2];
                        float dx=mid.x-marker.x,dz=mid.z-marker.z;
                        qualified=dx*dx+dz*dz<100f && Mathf.Abs(mid.y-marker.y)<3.2f;
                    }
                    if(!qualified && !hasMarker && !lane.NodeOwner &&
                       lane.WidthFromPrefab && lane.WidthMeters>=1.4f &&
                       lane.DistanceXZ<10f && lane.NearY>baseY+.25f &&
                       lane.NearY<baseY+3.5f)
                    {
                        // Some underground station lanes are pure pedestrians
                        // but the nearest track was absent from the quad-tree
                        // sample. Accept only a prefab-width, same-level lane
                        // close to the *stopped* carriage and validate that its
                        // projection is on the side, not along the track axis.
                        qualified=true;
                    }
                    // Node-owned non-track lanes are likely junctions/crossings.
                    if(!qualified){netRejected++;continue;}
                    nets++;
                    for(int i=1;i<lane.Points.Length;i++)
                    {
                        var a=lane.Points[i-1];var b=lane.Points[i];
                        float dx=b.x-a.x,dz=b.z-a.z,den=dx*dx+dz*dz;
                        if(den<.00001f)continue;
                        float t=Mathf.Clamp01(((origin.x-a.x)*dx+(origin.z-a.z)*dz)/den);
                        Vector3 at=Vector3.Lerp(a,b,t);
                        TryMetroPoint(at,"METRO PED NET "+lane.Id+
                            (lane.TrackAdjacent?" TRACK-NEAR":" MARKER-NEAR"),
                            origin,baseY,marker,hasMarker,haveAxis,axis,
                            searchRadius,maxHeight,lane.TrackAdjacent ? 0.4f : 3.0f,
                            ref best,ref landing,ref chosen);
                    }
                }
            }
            if(float.IsInfinity(best))
            {
                info=Name+" platform exit: none; marker="+hasMarker+
                    " areas="+areas+" areaSlopeRejected="+areaRejected+
                    " qualifiedPedNets="+nets+" roadExcluded="+roadExcluded+
                    " netRejected="+netRejected+
                    " cachedArea="+snap.NavAvailable+
                    " body="+origin.ToString("F1")+
                    " nearMarker="+(hasMarker?marker.ToString("F1"):"unavailable")+
                    " snapshotLanes="+(snap.Lanes==null?0:snap.Lanes.Length)+
                    " snapshotTriangles="+(snap.Triangles==null?0:snap.Triangles.Length);
                return false;
            }
            landing.y+=1.7f;
            info=chosen+" score="+best.ToString("F2")+
                 " stopMarker="+hasMarker;
            return true;
        }
        private static void TryMetroPoint(Vector3 p,string source,Vector3 origin,
            float baseY,Vector3 marker,bool hasMarker,bool haveAxis,Vector3 axis,
            float radius,float maxDY,float penalty,ref float best,ref Vector3 landing,
            ref string chosen)
        {
            if(!IsFinite(p.x)||!IsFinite(p.y)||!IsFinite(p.z))return;
            Vector3 delta=p-origin;delta.y=0f;
            float horizontal=delta.magnitude;
            float height=Mathf.Abs(p.y-baseY);
            // Keep the landing to the SIDE of the stopped carriage, not on
            // the rails, under its centre, or at the far end of the tunnel.
            if(horizontal<3.1f||horizontal>radius||height>maxDY)return;
            if(haveAxis)
            {
                float along=Mathf.Abs(Vector3.Dot(delta,axis));
                float side=Mathf.Abs(delta.x*axis.z-delta.z*axis.x);
                if(side<2.65f||along>15.0f)return;
            }
            float score=horizontal+1.1f*height+penalty;
            if(hasMarker)
            {
                float dx=p.x-marker.x,dz=p.z-marker.z;
                float nearMarker=Mathf.Sqrt(dx*dx+dz*dz);
                // Stop marker can be on the rail centreline, not the platform.
                // Use as a soft hint only, never as an absolute requirement.
                score+=Mathf.Min(nearMarker,20f)*.10f;
            }
            if(score>=best)return;
            best=score;landing=p;chosen=source;
        }
    }
}
