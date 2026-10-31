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
        internal static Entity Followed=Entity.Null;
        internal static Entity Body=Entity.Null;
        internal static RWVehicleKind Kind=RWVehicleKind.Unknown;
        internal static string Name { get { return RWLocalization.VehicleName(Kind); } }
        // One camera ride at a time. Velocity is sampled from rendered car positions,
        // without assuming that LayoutElement[0] is the leading end.
        private static Entity _directionRoot=Entity.Null;
        private static Vector3 _lastCenter;
        private static float _lastCenterAt;
        private static Vector3 _travelVelocity;
        private static bool _hasTravelDirection;
        private static bool _frontIsLast;
        private static float _lastDirectionLog;
        // A transiently missing route/stop snapshot must not erase a berth
        // marker that was verified a moment ago for this same stopped vessel.
        private static Entity _cachedDockBody = Entity.Null;
        private static Vector3 _cachedDock;
        private static Vector3 _cachedDockVessel;
        private static float _cachedDockAt = -100f;
        private static bool _hasCachedDock;
        private static void ResetDockCache()
        {
            _cachedDockBody = Entity.Null;
            _cachedDock = Vector3.zero;
            _cachedDockVessel = Vector3.zero;
            _cachedDockAt = -100f;
            _hasCachedDock = false;
        }
        private static void ResetRailDirection()
        {
            _directionRoot=Entity.Null;_lastCenterAt=0f;_travelVelocity=Vector3.zero;
            _hasTravelDirection=false;_frontIsLast=false;_lastDirectionLog=0f;
        }
        internal static bool Active { get {return Followed!=Entity.Null;} }
        internal static bool RoadExitNeedsStop
        {
            get { return Active && (Kind == RWVehicleKind.Bus || Kind == RWVehicleKind.Tram); }
        }
        internal static bool CanShowExitPrompt
        {
            get { return Active && (!RoadExitNeedsStop || RWTransitMotion.IsRideStoppedNow(Body)); }
        }
        internal static void TickRoadExitMotion()
        {
            if (RoadExitNeedsStop) RWTransitMotion.TickRide();
        }
        internal static void Start(Entity followed,Entity body,RWVehicleKind kind)
        {
            ResetRailDirection();
            ResetDockCache();
            Followed=followed;Body=body!=Entity.Null?body:followed;Kind=kind;
            if (kind == RWVehicleKind.Bus || kind == RWVehicleKind.Tram)
                RWTransitMotion.BeginRide(Body);
            else
                RWTransitMotion.EndRide();
            RWJournal.Warn("[RW-TRANSIT] RIDE-START follow="+followed.Index+":"+followed.Version+
                " body="+Body.Index+":"+Body.Version+" type="+kind);
        }
        internal static void End(string why)
        {
            if(Active)RWJournal.Warn("[RW-TRANSIT] RIDE-END reason="+why);
            Followed=Entity.Null;Body=Entity.Null;Kind=RWVehicleKind.Unknown;
            RWTransitMotion.EndRide();
            ResetRailDirection();
            ResetDockCache();
        }
        internal static bool IsRail
        {
            get
            {
                if(!Active)return false;
                if(Kind==RWVehicleKind.Train || Kind==RWVehicleKind.Subway)return true;
                var w=World.DefaultGameObjectInjectionWorld;
                if(w==null)return false;
                var em=w.EntityManager;
                return (em.Exists(Followed) && (em.HasComponent<Game.Vehicles.Train>(Followed) ||
                    em.HasComponent<Game.Vehicles.TrainCurrentLane>(Followed))) ||
                    (em.Exists(Body) && (em.HasComponent<Game.Vehicles.Train>(Body) ||
                    em.HasComponent<Game.Vehicles.TrainCurrentLane>(Body)));
            }
        }
        // Passenger vessels are not rail vehicles. Only accept quay transfer
        // during the game's real Boarding state; being stopped under a bridge
        // or at sea must never be enough to unlock E disembark.
        internal static bool IsWater
        {
            get { return Active && (Kind==RWVehicleKind.Ferry || Kind==RWVehicleKind.Ship); }
        }
        internal static bool IsBoardingAtWaterStop()
        {
            if(!IsWater)return false;
            var w=World.DefaultGameObjectInjectionWorld;
            if(w==null)return false;
            var em=w.EntityManager;
            Entity root=em.Exists(Body)?Body:Followed;
            if(em.Exists(root) && em.HasComponent<Game.Vehicles.Controller>(root))
            {
                var owner=em.GetComponentData<Game.Vehicles.Controller>(root).m_Controller;
                if(owner!=Entity.Null && em.Exists(owner))root=owner;
            }
            if(!em.Exists(root) || !em.HasComponent<Game.Vehicles.PublicTransport>(root))return false;
            return (em.GetComponentData<Game.Vehicles.PublicTransport>(root).m_State &
                    Game.Vehicles.PublicTransportFlags.Boarding)!=0;
        }
        // The station marker guides a separate *read-only* quay geometry scan.
        // A large cruise vessel can be >50m from the terminal's walkable area.
        // Markers are never treated as walkable surfaces themselves.
        internal static bool TryWaterDockAnchor(Vector3 vessel,out Vector3 anchor)
        {
            anchor=Vector3.zero;
            if(!IsWater || !IsBoardingAtWaterStop())return false;
            float now=UnityEngine.Time.realtimeSinceStartup;
            var route=RWStopStripState.Latest;
            float nearest=float.PositiveInfinity;
            if(route!=null && route.StopPositions!=null &&
               now-route.SampleAt<=3f)
            {
                foreach(var pos in route.StopPositions)
                {
                    if(pos==Vector3.zero || !IsFinite(pos.x)||!IsFinite(pos.y)||!IsFinite(pos.z))continue;
                    float dx=vessel.x-pos.x,dz=vessel.z-pos.z;
                    float d=Mathf.Sqrt(dx*dx+dz*dz);
                    float maxDockGap=Kind==RWVehicleKind.Ship?
                        RWTransitRanges.ShipDockMarkerMaxDistance:135f;
                    if(d>maxDockGap || Mathf.Abs(pos.y-(vessel.y-1.7f))>16f)continue;
                    if(d<nearest){nearest=d;anchor=pos;}
                }
            }
            if(!float.IsInfinity(nearest))
            {
                _cachedDockBody=Body;
                _cachedDock=anchor;
                _cachedDockVessel=vessel;
                _cachedDockAt=now;
                _hasCachedDock=true;
                return true;
            }
            // Reuse a *recent* verified marker only for this ride, while the
            // ship is still in the real Boarding state and has barely moved.
            // Never use this cache after departure or on the next ride.
            if(_hasCachedDock && _cachedDockBody==Body &&
               now>=_cachedDockAt && now-_cachedDockAt<=3f)
            {
                float mx=vessel.x-_cachedDockVessel.x;
                float mz=vessel.z-_cachedDockVessel.z;
                float dx=vessel.x-_cachedDock.x;
                float dz=vessel.z-_cachedDock.z;
                float maxDockGap=Kind==RWVehicleKind.Ship?
                    RWTransitRanges.ShipDockMarkerMaxDistance:135f;
                if(mx*mx+mz*mz<=36f && dx*dx+dz*dz<=maxDockGap*maxDockGap &&
                   Mathf.Abs(_cachedDock.y-(vessel.y-1.7f))<=16f)
                {
                    anchor=_cachedDock;
                    return true;
                }
            }
            return false;
        }
        internal static bool IsBoardingAtRailStop()
        {
            if(!Active||!IsRail)return false;
            var w=World.DefaultGameObjectInjectionWorld;
            if(w==null)return false;
            var em=w.EntityManager;
            Entity e=em.Exists(Body)?Body:Followed;
            if(em.HasComponent<Game.Vehicles.Controller>(e))
                e=em.GetComponentData<Game.Vehicles.Controller>(e).m_Controller;
            if(!em.Exists(e))return false;
            if(!em.HasComponent<Game.Vehicles.PublicTransport>(e))return false;
            return (em.GetComponentData<Game.Vehicles.PublicTransport>(e).m_State & Game.Vehicles.PublicTransportFlags.Boarding)!=0;
        }
        private static bool TryPose(Entity e,EntityManager em,out Vector3 point,out UnityEngine.Quaternion rotation)
        {
            point=Vector3.zero;rotation=UnityEngine.Quaternion.identity;
            if(e==Entity.Null||!em.Exists(e))return false;
            if(em.HasComponent<Game.Rendering.InterpolatedTransform>(e))
            {
                var t=em.GetComponentData<Game.Rendering.InterpolatedTransform>(e);
                point=new Vector3(t.m_Position.x,t.m_Position.y,t.m_Position.z);
                rotation=new UnityEngine.Quaternion(t.m_Rotation.value.x,t.m_Rotation.value.y,t.m_Rotation.value.z,t.m_Rotation.value.w);
                return true;
            }
            if(em.HasComponent<Game.Objects.Transform>(e))
            {
                var t=em.GetComponentData<Game.Objects.Transform>(e);
                point=new Vector3(t.m_Position.x,t.m_Position.y,t.m_Position.z);
                rotation=new UnityEngine.Quaternion(t.m_Rotation.value.x,t.m_Rotation.value.y,t.m_Rotation.value.z,t.m_Rotation.value.w);
                return true;
            }
            return false;
        }
        // Bypass FPC's ambiguous carriage/TrainNavigation bounds in the rail cab
        // only. This does not modify game ECS, carriage transforms or simulation.
        internal static bool TryRailCab(Entity requested,out float3 eye,out quaternion rotation)
        {
            eye=float3.zero;rotation=quaternion.identity;
            if(!IsRail || !Active || requested!=Followed)return false;
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null)return false;
            var em=world.EntityManager;
            Entity root=em.Exists(Body)?Body:requested;
            if(em.HasComponent<Game.Vehicles.Controller>(root))
            {
                var controller=em.GetComponentData<Game.Vehicles.Controller>(root).m_Controller;
                if(em.Exists(controller))root=controller;
            }
            Entity lead=requested,second=Entity.Null;
            // The root may be at either end; use the two *physical* outer cars.
            // At a stop, preserve the last proven moving direction. Only if no
            // movement has ever been observed do we fall back to TrainFlags.
            if(em.HasBuffer<Game.Vehicles.LayoutElement>(root))
            {
                var list=em.GetBuffer<Game.Vehicles.LayoutElement>(root,true);
                Entity first=Entity.Null,last=Entity.Null,firstInside=Entity.Null,lastInside=Entity.Null;
                Vector3 firstP=Vector3.zero,lastP=Vector3.zero;
                for(int i=0;i<list.Length;i++)
                {
                    Entity car=list[i].m_Vehicle;
                    Vector3 pos;UnityEngine.Quaternion rot;
                    if(!TryPose(car,em,out pos,out rot))continue;
                    if(first==Entity.Null)
                    {first=car;firstP=pos;}
                    else if(firstInside==Entity.Null)
                    {firstInside=car;}
                    lastInside=last;
                    last=car;lastP=pos;
                }
                if(first!=Entity.Null && last!=Entity.Null && first!=last)
                {
                    Vector3 axis=lastP-firstP;axis.y=0f;
                    Vector3 center=(firstP+lastP)*.5f;
                    float now=UnityEngine.Time.realtimeSinceStartup;
                    if(_directionRoot!=root)
                    {
                        ResetRailDirection();_directionRoot=root;
                        _frontIsLast=em.HasComponent<Game.Vehicles.Train>(root) &&
                            (em.GetComponentData<Game.Vehicles.Train>(root).m_Flags & Game.Vehicles.TrainFlags.Reversed)!=0;
                        _lastCenter=center;_lastCenterAt=now;
                    }
                    else if(now-_lastCenterAt>=.16f)
                    {
                        float dt=now-_lastCenterAt;
                        Vector3 delta=center-_lastCenter;delta.y=0f;
                        // Ignore teleport / despawn / long stalls and subpixel jitter.
                        if(dt>0f && dt<1.8f && delta.magnitude<25f)
                            _travelVelocity=Vector3.Lerp(_travelVelocity,delta/dt,.65f);
                        else _travelVelocity=Vector3.zero;
                        _lastCenter=center;_lastCenterAt=now;
                    }
                    if(axis.sqrMagnitude>4f && _travelVelocity.sqrMagnitude>1.0f)
                    {
                        float signedSpeed=Vector3.Dot(_travelVelocity,axis.normalized);
                        // A longitudinal component > 0.8 m/s is more convincing
                        // than low-speed bobbing at stations or on sharp curves.
                        if(Mathf.Abs(signedSpeed)>=.8f)
                        {
                            bool newLast=signedSpeed>0f;
                            if(!_hasTravelDirection || newLast!=_frontIsLast)
                            {
                                _frontIsLast=newLast;_hasTravelDirection=true;
                                RWJournal.Warn("[RW-RAIL-HEAD] moving lead="+(newLast?"layout-last":"layout-first")+
                                    " signedSpeed="+signedSpeed.ToString("F2")+
                                    " members="+list.Length);
                            }
                        }
                    }
                    lead=_frontIsLast?last:first;
                    second=_frontIsLast?lastInside:firstInside;
                    if(lead==second)second=Entity.Null;
                    if(now-_lastDirectionLog>20f)
                    {
                        _lastDirectionLog=now;
                        RWJournal.Warn("[RW-RAIL-HEAD] select "+(_frontIsLast?"last":"first")+
                            " motion="+(_hasTravelDirection?"verified":"fallback")+
                            " root="+root.Index+":"+root.Version);
                    }
                }
                else if(first!=Entity.Null)lead=first;
            }
            Vector3 point;UnityEngine.Quaternion carRotation;
            if(!TryPose(lead,em,out point,out carRotation))return false;
            Vector3 front=carRotation*Vector3.forward;
            Vector3 secondPosition;UnityEngine.Quaternion otherRotation;
            if(TryPose(second,em,out secondPosition,out otherRotation))
            {
                Vector3 outward=point-secondPosition;outward.y=0f;
                if(outward.sqrMagnitude>.2f)front=outward.normalized;
            }
            front.y=0f;
            if(front.sqrMagnitude<.01f)front=Vector3.forward;
            front.Normalize();
            float reach=5.5f;
            // Try geometry to estimate the nose without an invented universal train
            // length. World AABB is a conservative approximation for rotated cars.
            try
            {
                if(em.HasComponent<Game.Prefabs.PrefabRef>(lead))
                {
                    Entity prefab=em.GetComponentData<Game.Prefabs.PrefabRef>(lead).m_Prefab;
                    if(em.HasComponent<Game.Prefabs.ObjectGeometryData>(prefab))
                    {
                        var geo=em.GetComponentData<Game.Prefabs.ObjectGeometryData>(prefab);
                        var pos=new float3(point.x,point.y,point.z);
                        var rot=new quaternion(carRotation.x,carRotation.y,carRotation.z,carRotation.w);
                        var bounds=Game.Objects.ObjectUtils.CalculateBounds(pos,rot,geo);
                        float spanX=bounds.max.x-bounds.min.x,spanZ=bounds.max.z-bounds.min.z;
                        reach=Mathf.Clamp(.45f*(Mathf.Abs(front.x)*spanX+Mathf.Abs(front.z)*spanZ),2.5f,13f);
                    }
                }
            }
            catch{/* stable fixed offset if geometry unavailable */}
            // Height is *above the car transform*, not above terrain. Metro tracks
            // can be below ground; the old FPC non-train branch put cameras under cars.
            Vector3 cab=point+front*reach+Vector3.up*2.15f;
            eye=new float3(cab.x,cab.y,cab.z);
            UnityEngine.Quaternion look=UnityEngine.Quaternion.LookRotation(front,Vector3.up);
            rotation=new quaternion(look.x,look.y,look.z,look.w);
            return true;
        }
        internal static bool TryEye(out Vector3 eye)
        {
            eye=Vector3.zero;
            if(!Active)return false;
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null)return false;
            var em=world.EntityManager;
            var body=em.Exists(Body)?Body:Followed;
            if(!em.Exists(body))return false;
            float3 p;
            if(em.HasComponent<InterpolatedTransform>(body))p=em.GetComponentData<InterpolatedTransform>(body).m_Position;
            else if(em.HasComponent<Game.Objects.Transform>(body))p=em.GetComponentData<Game.Objects.Transform>(body).m_Position;
            else return false;
            eye=new Vector3(p.x,p.y+1.7f,p.z);
            return IsFinite(eye.x)&&IsFinite(eye.y)&&IsFinite(eye.z);
        }
        private static bool IsFinite(float x){return !float.IsNaN(x)&&!float.IsInfinity(x);}
        // Read the actual TransportStop transforms collected from the current route.
        // Never mistake a curb merely because it is a pedestrian Net beside rail.
        private static bool TryStationAnchor(Vector3 origin,out Vector3 station)
        {
            station=Vector3.zero;
            var strip=RWStopStripState.Latest;
            if(strip==null || strip.StopPositions==null || strip.StopPositions.Length==0 ||
                UnityEngine.Time.realtimeSinceStartup-strip.SampleAt>2f)return false;
            float best=float.PositiveInfinity;
            for(int i=0;i<strip.StopPositions.Length;i++)
            {
                Vector3 p=strip.StopPositions[i];
                if(!IsFinite(p.x)||!IsFinite(p.y)||!IsFinite(p.z) ||
                    p==Vector3.zero)continue;
                float dx=p.x-origin.x,dz=p.z-origin.z;
                float dist=Mathf.Sqrt(dx*dx+dz*dz);
                if(dist>45f || Mathf.Abs(p.y-(origin.y-1.7f))>6.0f)continue;
                // A stop has to be reasonably near the train, but it need not be
                // beside the carriage boarded earlier in a long train.
                float score=dist+.3f*Mathf.Abs(p.y-(origin.y-1.7f));
                if(score<best){best=score;station=p;}
            }
            return !float.IsInfinity(best);
        }
        // Search only ground-verified geometry already collected by RWOverlayScanSystem.
        // No blind terrain fallback: an out-of-station subway, bridge, or ship at sea
        // must not teleport the player to a distant or wrong-height surface.
    }
}
