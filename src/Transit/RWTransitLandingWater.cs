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
    // Quay, ferry and cruise-ship landing constraints. No changes to safety rules.
    internal static partial class RWTransitRide
    {
        // S1.7.4: Vessel disembark. Do not use nearest generic walking Net: bridges,
        // waterfront road curbs and multi-level quays may all be closer than the berth.
        // With a real stop marker, scan that marker's walking geometry (not ship centre).
        // Without a marker, require same-level quay-like geometry near the stopped hull.
        private static bool FindShipLanding(RWOverlaySnapshot snap,Vector3 origin,
            bool hasDock,Vector3 dock,out Vector3 landing,out string info)
        {
            landing=Vector3.zero;
            info=Name+" quay: no verified landing";
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null)return false;
            var em=world.EntityManager;
            Vector3 boat;UnityEngine.Quaternion orient;
            Entity body=em.Exists(Body)?Body:Followed;
            if(!TryPose(body,em,out boat,out orient))
            {info=Name+" quay: vessel transform unavailable";return false;}
            // Do not confuse an ECS pedestrian lane at sea level with a pier.
            // S1.7.4 selected QUAY PED NET footY=399.95/399.98 in the user's
            // actual logs: both coordinates were essentially at the water surface.
            // Sample open water at the vessel pivot (not at a possibly raised dock
            // marker or dry shoreline, where Terrain may bias the result).
            float waterY=float.NaN;
            try
            {
                var water=world.GetExistingSystemManaged<Game.Simulation.WaterSystem>();
                var terrain=world.GetExistingSystemManaged<Game.Simulation.TerrainSystem>();
                if(water!=null && water.Loaded && terrain!=null)
                {
                    var heights=terrain.GetHeightData();
                    var surface=water.GetSurfaceData(out var dependencies);
                    dependencies.Complete();
                    if(surface.isCreated)
                        waterY=Game.Simulation.WaterUtils.SampleHeight(ref surface,
                            ref heights,new float3(boat.x,0f,boat.z));
                }
            }
            catch(Exception ex)
            {
                RWJournal.Warn("[RW-TRANSIT] SHIP-WATER-SAMPLE-ERROR "+ex.GetType().Name);
            }
            // Fail closed: no confirmed water level means no verified *dry* quay.
            // Waterline + 2.0m also keeps the post-exit walker clearly above the
            // sea-level pedestrian nets that its ordinary geometry scan may see.
            if(!IsFinite(waterY))
            {
                info=Name+" quay: cannot verify water elevation; no safe disembark";
                return false;
            }
            const float minimumDeckClearance=2.0f;
            float minimumDryFootY=waterY+minimumDeckClearance;
            var audit=new ShipLandingAudit();
            var forward=orient*Vector3.forward;forward.y=0f;
            if(forward.sqrMagnitude<.01f)forward=Vector3.forward;
            forward.Normalize();
            var right=new Vector3(forward.z,0f,-forward.x);
            float best=float.PositiveInfinity;
            string selected="";
            int triCount=0,netCount=0,roadRejected=0,heightRejected=0;
            Vector3 scanTarget=hasDock?dock:origin;
            if(snap.NavAvailable && snap.Triangles!=null)
            {
                foreach(var tri in snap.Triangles)
                {
                    float minY=Mathf.Min(tri.A.y,Mathf.Min(tri.B.y,tri.C.y));
                    float maxY=Mathf.Max(tri.A.y,Mathf.Max(tri.B.y,tri.C.y));
                    if(maxY-minY>1.8f){heightRejected++;continue;}
                    triCount++;
                    Vector3 p=ClosestTrianglePointXZ(tri.A,tri.B,tri.C,scanTarget);
                    ConsiderShipPoint(p,"QUAY AREA "+tri.AreaId+"/"+tri.TriangleId,
                        origin,boat,forward,right,dock,hasDock,minimumDryFootY,0f,
                        audit,ref best,ref landing,ref selected);
                    // A dock navigation face may cover the landing but have no
                    // vertex at its near side. Probe *inside the same face*.
                    float[] radii={4f,9f,16f};
                    foreach(float radius in radii)
                        for(int j=0;j<12;j++)
                        {
                            float a=j*Mathf.PI/6f;
                            Vector3 probe=new Vector3(scanTarget.x+radius*Mathf.Cos(a),0f,
                                scanTarget.z+radius*Mathf.Sin(a));
                            Vector3 inside;
                            if(InsideTriXZ(tri.A,tri.B,tri.C,probe,out inside))
                                ConsiderShipPoint(inside,"QUAY AREA "+tri.AreaId+"/"+tri.TriangleId,
                                    origin,boat,forward,right,dock,hasDock,minimumDryFootY,0f,
                                    audit,ref best,ref landing,ref selected);
                        }
                }
            }
            if(snap.Lanes!=null)
            {
                foreach(var lane in snap.Lanes)
                {
                    if(lane.Points==null||lane.Points.Length<2)continue;
                    if(lane.RoadMethod){roadRejected++;continue;} // Reject road-bridge curb/shared lanes.
                    if(!lane.WidthFromPrefab||lane.WidthMeters<1.05f)continue;
                    netCount++;
                    for(int j=1;j<lane.Points.Length;j++)
                    {
                        Vector3 a=lane.Points[j-1],b=lane.Points[j];
                        float dx=b.x-a.x,dz=b.z-a.z,den=dx*dx+dz*dz;
                        if(den<.00001f)continue;
                        float t=Mathf.Clamp01(((scanTarget.x-a.x)*dx+(scanTarget.z-a.z)*dz)/den);
                        var p=Vector3.Lerp(a,b,t);
                        ConsiderShipPoint(p,"QUAY PED NET "+lane.Id,origin,boat,forward,right,
                            dock,hasDock,minimumDryFootY,2f,audit,
                            ref best,ref landing,ref selected);
                    }
                }
            }
            if(float.IsInfinity(best))
            {
                info=Name+" quay exit: none; boarding=true marker="+hasDock+
                    " dock="+(hasDock?dock.ToString("F1"):"missing")+
                    " vessel="+boat.ToString("F1")+
                    " area="+triCount+" purePedNets="+netCount+
                    " roadFiltered="+roadRejected+" steepArea="+heightRejected+
                    " waterY="+waterY.ToString("F2")+
                    " minFootY="+minimumDryFootY.ToString("F2")+
                    " wetCandidatesRejected="+audit.Wet+
                    " dryCandidates="+audit.Dry+
                    " dryFootRange="+audit.DryRange+
                    " boatHeightRejected="+audit.BoatHeight+
                    " markerHeightRejected="+audit.MarkerHeight+
                    " spatialRejected="+audit.Spatial+
                    " elevatedMarker="+(hasDock && Kind==RWVehicleKind.Ship && dock.y-boat.y>12.2f)+
                    " scanRadius="+snap.SearchRadius.ToString("F0")+
                    " scanCenter="+snap.Eye.ToString("F1")+
                    " snapshotLanes="+(snap.Lanes==null?0:snap.Lanes.Length)+
                    " snapshotTriangles="+(snap.Triangles==null?0:snap.Triangles.Length);
                return false;
            }
            float acceptedFootY=landing.y;
            landing.y+=1.7f;
            info=selected+" score="+best.ToString("F1")+" marker="+hasDock+
                " waterY="+waterY.ToString("F2")+
                " landingFootY="+acceptedFootY.ToString("F2")+
                " clearance="+(acceptedFootY-waterY).ToString("F2")+
                " wetCandidatesRejected="+audit.Wet+
                " elevatedMarker="+(hasDock && Kind==RWVehicleKind.Ship && dock.y-boat.y>12.2f);
            return true;
        }
        // Record why a *real* pedestrian-surface candidate failed.  A large ship's
        // terminal marker can be vertically offset from the accessible quay.
        // Keeping separate counts prevents another blind radius increase.
        private sealed class ShipLandingAudit
        {
            internal int Wet, Dry, BoatHeight, MarkerHeight, Spatial;
            private float _minDry=float.PositiveInfinity, _maxDry=float.NegativeInfinity;
            internal void AddDry(float y)
            {
                Dry++;
                if(y<_minDry)_minDry=y;
                if(y>_maxDry)_maxDry=y;
            }
            internal string DryRange => Dry==0 ? "none" :
                _minDry.ToString("F2")+".."+_maxDry.ToString("F2");
        }

        private static void ConsiderShipPoint(Vector3 p,string source,Vector3 origin,
            Vector3 boat,Vector3 forward,Vector3 right,Vector3 dock,bool hasDock,
            float minimumDryFootY,float penalty,ShipLandingAudit audit,
            ref float best,ref Vector3 landing,ref string chosen)
        {
            if(!IsFinite(p.x)||!IsFinite(p.y)||!IsFinite(p.z))return;
            if(p.y<minimumDryFootY){audit.Wet++;return;}
            audit.AddDry(p.y);
            Vector3 delta=p-boat;delta.y=0f;
            float fromBoat=delta.magnitude;
            float fromStop=hasDock?new Vector2(p.x-dock.x,p.z-dock.z).magnitude:0f;
            float sideways=Mathf.Abs(Vector3.Dot(delta,right));
            float lengthwise=Mathf.Abs(Vector3.Dot(delta,forward));
            float dy=p.y-boat.y;
            // An elevated passenger-terminal marker is not the passenger's
            // foot elevation.  RC9 required p.y <= boat.y+7 AND
            // p.y >= dock.y-5.2: for the actual failing dock (dockY=413.4,
            // boatY=400.0), these intervals do not overlap (407 < 408.2).
            // The water-level +2m guard and REAL pedestrian geometry still apply.
            // This exception is for large passenger ships with a confirmed high
            // stop marker only; ferry/rail/road behavior is unchanged.
            bool elevatedShipMarker=hasDock && Kind==RWVehicleKind.Ship &&
                dock.y-boat.y>12.2f;
            float maxFootY=elevatedShipMarker?
                Mathf.Min(dock.y+2f,boat.y+18f):boat.y+7f;
            if(dy < -1.3f || p.y>maxFootY)
            {audit.BoatHeight++;return;}
            float maxFromDock=Kind==RWVehicleKind.Ship?
                RWTransitRanges.ShipLandingDistanceFromMarker:24f;
            if(hasDock && fromStop>maxFromDock)
            {audit.Spatial++;return;}
            // When the stop marker is elevated, use its XZ for berth identity
            // but do not force pedestrian geometry to sit within 5.2m of its Y.
            // Actual candidate height is bounded by the dry-water and raised
            // terminal caps above, and the usual hull-side constraints below.
            if(hasDock && !elevatedShipMarker && Mathf.Abs(p.y-dock.y)>5.2f)
            {audit.MarkerHeight++;return;}
            float farLimit=Kind==RWVehicleKind.Ship?
                RWTransitRanges.ShipLandingDistanceFromHull:65f;
            if(fromBoat<5.0f || fromBoat>farLimit || sideways<3.8f ||
               lengthwise>(Kind==RWVehicleKind.Ship?RWTransitRanges.ShipLandingLongitudinal:48f))
            {audit.Spatial++;return;}
            if(!hasDock && (sideways>(Kind==RWVehicleKind.Ship?
                RWTransitRanges.ShipMarkerlessLandingSideways:30f) ||
                fromBoat>(Kind==RWVehicleKind.Ship?
                RWTransitRanges.ShipMarkerlessLandingFromHull:43f)))
            {audit.Spatial++;return;}
            float score=fromBoat*.20f + sideways*.08f + Mathf.Abs(dy-2f)*.85f +
                penalty + (hasDock?fromStop*1.6f:0f);
            if(score>=best)return;
            best=score;landing=p;chosen=source;
        }
        // S1.7.3: shared metro + mainline train platform search; no blind terrain,
        // road or rail-lane fallback. Only called while PublicTransport is Boarding.
        // The live passenger lane/Navigation Area is the authoritative walkable
        // geometry; a route stop marker, when available, is only a tie-breaker.
    }
}
