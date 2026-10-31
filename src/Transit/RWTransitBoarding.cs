// SPDX-License-Identifier: MIT
// Real Walking Transit Boarding RC7: train-front selection and safe one-key disembark.
// No ECS writes; FPC camera model + original walk geometry are the only runtime writes.
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
    // Internal identity is independent of the display language. Never branch on localized text.
    internal enum RWVehicleKind { Unknown, Bus, Tram, Train, Subway, Ferry, Ship, Airplane }

    // Passenger ships need a berth-scale search; other vehicles keep their tested ranges.
    // These are bounded proximity / geometry-selection ranges, not permission to board a moving vehicle.
    internal static class RWTransitRanges
    {
        internal const float OrdinaryBoardingGap = 3.5f;
        internal const float ShipBoardingGap = 10f;
        internal const float OrdinaryBoardingVerticalGap = 4.5f;
        internal const float ShipBoardingVerticalGap = 6f;
        internal const float ShipDockMarkerScan = 60f;
        internal const float ShipNoMarkerScan = 112f;
        internal const float ShipDockMarkerMaxDistance = 180f;
        internal const float ShipLandingDistanceFromMarker = 52f;
        internal const float ShipLandingDistanceFromHull = 180f;
        internal const float ShipLandingLongitudinal = 140f;
        internal const float ShipMarkerlessLandingFromHull = 125f;
        internal const float ShipMarkerlessLandingSideways = 44f;
    }

    internal static class RWTransitState
    {
        internal static Entity Candidate = Entity.Null;
        internal static RWVehicleKind Kind = RWVehicleKind.Unknown;
        internal static string VehicleName { get { return RWLocalization.VehicleName(Kind); } }
        internal static Entity PhysicalVehicle = Entity.Null;
        internal static float CandidateTime;
        internal static float Distance;
        internal static int Scanned, PositionFound;
        internal static void Clear()
        {
            Candidate = Entity.Null;
            PhysicalVehicle = Entity.Null;
            Kind = RWVehicleKind.Unknown;
            CandidateTime = 0;
            Distance = float.PositiveInfinity;
        }
        internal static bool Ready
        {
            get {
                return UnityEngine.Application.isFocused &&
                       RWWalkState.Mode == RWTravelMode.Navigation &&
                       Candidate != Entity.Null &&
                       UnityEngine.Time.realtimeSinceStartup - CandidateTime < .9f &&
                       RWTransitMotion.IsStoppedNow(PhysicalVehicle, (Kind == RWVehicleKind.Ship || Kind == RWVehicleKind.Ferry));
            }
        }
    }

    // Only the public transit component is eligible; service vehicles and private cars are excluded.
    // Evaluate proximity at 3 Hz, while preserving the established FPC and walking code.
    public sealed partial class RWTransitNearbySystem : GameSystemBase
    {
        private EntityQuery _publicTransit;
        private EntityQuery _trainCarriages;
        private float _nextScan;
        private float _nextSummary;
        private bool _errorReported;
        private float _nextShipDiagnostic;
        private Entity _nearestCruiseBody;
        private float _nearestCruiseGap;
        private int _nearCruise, _cruiseHeightRejected, _cruiseRangeRejected, _cruiseMotionRejected, _cruiseEligible;
        private float _nextFerryDiagnostic;
        private Entity _nearestFerryBody;
        private float _nearestFerryGap;
        private int _nearFerry, _ferryHeightRejected, _ferryRangeRejected, _ferryMotionRejected, _ferryEligible;
        protected override void OnCreate()
        {
            base.OnCreate();
            ComponentType[] exclusions = {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Game.Tools.Temp>(),
                ComponentType.ReadOnly<Game.Objects.Unspawned>()
            };
            _publicTransit = GetEntityQuery(new EntityQueryDesc {
                All = new [] { ComponentType.ReadOnly<Game.Vehicles.PublicTransport>() },
                None = exclusions
            });
            // A train carriage may have Controller but not PublicTransport; match it to the head.
            _trainCarriages = GetEntityQuery(new EntityQueryDesc {
                All = new [] {
                    ComponentType.ReadOnly<Game.Vehicles.Controller>(),
                    ComponentType.ReadOnly<Game.Vehicles.TrainCurrentLane>()
                },
                None = exclusions
            });
        }
        protected override void OnUpdate()
        {
            if (!UnityEngine.Application.isFocused || RWWalkState.Mode != RWTravelMode.Navigation)
            {
                RWTransitState.Clear();
                RWTransitMotion.Clear();
                return;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextScan) return;
            _nextScan = now + .33f;
            try
            {
                Vector3 player;
                if (!RWTransitFpc.TryGetManualPosition(out player)) { RWTransitState.Clear(); return; }
                // Prefer the established nav height, avoiding selecting a train beneath a bridge.
                if (!float.IsNaN(RWWalkState.TargetFootY) && !float.IsInfinity(RWWalkState.TargetFootY))
                    player.y = RWWalkState.TargetFootY + 1.7f;
                Entity best = Entity.Null;
                Entity bestBody = Entity.Null;
                RWVehicleKind bestKind = RWVehicleKind.Unknown;
                // A ship can be 10m beyond its rendered prefab bounds, so 3.5m
                // must not also be the initial best-candidate score.
                float bestDistance = float.PositiveInfinity;
                int scanned = 0, found = 0;
                _nearCruise = _cruiseHeightRejected = _cruiseRangeRejected = _cruiseMotionRejected = _cruiseEligible = 0;
                _nearestCruiseBody = Entity.Null;
                _nearestCruiseGap = float.PositiveInfinity;
                _nearFerry = _ferryHeightRejected = _ferryRangeRejected = _ferryMotionRejected = _ferryEligible = 0;
                _nearestFerryBody = Entity.Null;
                _nearestFerryGap = float.PositiveInfinity;
                using (var vehicles = _publicTransit.ToEntityArray(Allocator.Temp))
                {
                    for (int i=0; i<vehicles.Length; i++)
                        Evaluate(vehicles[i], vehicles[i], player, ref best, ref bestBody, ref bestKind, ref bestDistance,
                            ref scanned, ref found);
                }
                using (var carriages = _trainCarriages.ToEntityArray(Allocator.Temp))
                {
                    for (int i=0; i<carriages.Length; i++)
                    {
                        var carriage=carriages[i];
                        if (!EntityManager.Exists(carriage)) continue;
                        Entity controller = EntityManager.GetComponentData<Game.Vehicles.Controller>(carriage).m_Controller;
                        if (controller == Entity.Null || !EntityManager.Exists(controller) ||
                            !EntityManager.HasComponent<Game.Vehicles.PublicTransport>(controller)) continue;
                        Evaluate(carriage, controller, player, ref best, ref bestBody, ref bestKind, ref bestDistance,
                            ref scanned, ref found);
                    }
                }
                RWTransitState.Candidate = best;
                RWTransitState.PhysicalVehicle = bestBody;
                RWTransitState.Kind = bestKind;
                RWTransitState.Distance = bestDistance;
                RWTransitState.CandidateTime = best == Entity.Null ? 0f : now;
                RWTransitState.Scanned=scanned;
                RWTransitState.PositionFound=found;
                if (_nearCruise > 0 && now >= _nextShipDiagnostic)
                {
                    RWJournal.Trace("[RW-TRANSIT] SHIP-PROMPT-DIAG near=" + _nearCruise +
                        " heightRejected=" + _cruiseHeightRejected +
                        " rangeRejected=" + _cruiseRangeRejected +
                        " motionRejected=" + _cruiseMotionRejected +
                        " eligible=" + _cruiseEligible +
                        " nearestHullGap=" + _nearestCruiseGap.ToString("F2") +
                        " nearestBody=" + _nearestCruiseBody.Index + ":" + _nearestCruiseBody.Version +
                        " " + RWTransitMotion.WaterDiagnostic(_nearestCruiseBody, true) +
                        " promptReady=" + RWTransitState.Ready);
                    _nextShipDiagnostic = now + 2.5f;
                }
                if (_nearFerry > 0 && now >= _nextFerryDiagnostic)
                {
                    RWJournal.Trace("[RW-TRANSIT] FERRY-PROMPT-DIAG near=" + _nearFerry +
                        " heightRejected=" + _ferryHeightRejected +
                        " rangeRejected=" + _ferryRangeRejected +
                        " motionRejected=" + _ferryMotionRejected +
                        " eligible=" + _ferryEligible +
                        " nearestHullGap=" + _nearestFerryGap.ToString("F2") +
                        " nearestBody=" + _nearestFerryBody.Index + ":" + _nearestFerryBody.Version +
                        " " + RWTransitMotion.WaterDiagnostic(_nearestFerryBody, true) +
                        " promptReady=" + RWTransitState.Ready);
                    _nextFerryDiagnostic = now + 2.5f;
                }
                if(now>_nextSummary)
                {
                    RWJournal.Trace("[RW-TRANSIT] SCAN vRC12 checked="+scanned+" positioned="+found+
                        " candidate="+best.Index+":"+best.Version+" type="+bestKind+
                        " distance="+bestDistance.ToString("F2")+" nav="+RWWalkState.Mode+
                        " shipGap="+RWTransitRanges.ShipBoardingGap.ToString("F1"));
                    _nextSummary=now+12f;
                }
            }
            catch (Exception ex)
            {
                RWTransitState.Clear();
                if (!_errorReported) RWJournal.Error("[RW-TRANSIT] SCAN-ERROR (detection disabled): "+ex);
                _errorReported=true;
                Enabled=false;
            }
        }
        private void Evaluate(Entity physicalEntity, Entity followEntity, Vector3 player,
            ref Entity best, ref Entity bestBody, ref RWVehicleKind bestKind, ref float bestDistance, ref int scanned, ref int found)
        {
            scanned++;
            float3 pos;
            quaternion rot;
            if (!TryPosition(physicalEntity,out pos,out rot)) return;
            found++;
            var p=new Vector3(pos.x,pos.y,pos.z);
            // Check the ship type before rejecting a candidate by pivot distance:
            // a large passenger ship's pivot can be far beyond its gangway.
            float xDelta=Mathf.Abs(player.x-p.x), zDelta=Mathf.Abs(player.z-p.z);
            float yDelta=Mathf.Abs(player.y-p.y);
            if (xDelta>190f || zDelta>190f || yDelta>42f)return;
            RWVehicleKind kind=GetTransitKind(followEntity);
            bool largeShip=kind==RWVehicleKind.Ship;
            bool ferry=kind==RWVehicleKind.Ferry;
            bool waterVessel=largeShip || ferry;
            if (!largeShip && (xDelta>110f || zDelta>110f || yDelta>35f))return;
            // Watercraft bob at the quay, changing hull bounds slightly. A narrow
            // release-only band avoids flickering *after* an eligible first scan.
            // Ferry acquisition retains the ordinary 3.5m/4.5m range; cruise
            // ships retain their tested 10m/6m range. No on-the-move boarding.
            bool sameSelectedWater = waterVessel && RWTransitState.Kind == kind &&
                RWTransitState.PhysicalVehicle == physicalEntity && RWTransitState.Candidate != Entity.Null;
            float boardingGap = largeShip
                ? (sameSelectedWater ? RWTransitRanges.ShipBoardingGap + 1.5f : RWTransitRanges.ShipBoardingGap)
                : ferry && sameSelectedWater ? RWTransitRanges.OrdinaryBoardingGap + 0.75f
                : RWTransitRanges.OrdinaryBoardingGap;
            float maxVertical = largeShip
                ? (sameSelectedWater ? RWTransitRanges.ShipBoardingVerticalGap + 1f : RWTransitRanges.ShipBoardingVerticalGap)
                : ferry && sameSelectedWater ? RWTransitRanges.OrdinaryBoardingVerticalGap + 0.5f
                : RWTransitRanges.OrdinaryBoardingVerticalGap;
            float minX=p.x,maxX=p.x,minY=p.y,maxY=p.y,minZ=p.z,maxZ=p.z;
            if (EntityManager.HasComponent<PrefabRef>(physicalEntity))
            {
                var prefab=EntityManager.GetComponentData<PrefabRef>(physicalEntity).m_Prefab;
                if (prefab!=Entity.Null && EntityManager.Exists(prefab) &&
                    EntityManager.HasComponent<ObjectGeometryData>(prefab))
                {
                    var geo=EntityManager.GetComponentData<ObjectGeometryData>(prefab);
                    var bounds=Game.Objects.ObjectUtils.CalculateBounds(pos,rot,geo);
                    minX=bounds.min.x;maxX=bounds.max.x;
                    minY=bounds.min.y;maxY=bounds.max.y;
                    minZ=bounds.min.z;maxZ=bounds.max.z;
                }
            }
            // Height is always checked; even ships cannot board from a bridge or tunnel.
            float vertical = Mathf.Max(0f,Mathf.Max(minY-player.y,player.y-maxY));
            float dx=Mathf.Max(0f,Mathf.Max(minX-player.x,player.x-maxX));
            float dz=Mathf.Max(0f,Mathf.Max(minZ-player.z,player.z-maxZ));
            float distance=Mathf.Sqrt(dx*dx+dz*dz);
            if (ferry && distance <= 20f)
            {
                _nearFerry++;
                if (distance < _nearestFerryGap)
                {
                    _nearestFerryGap = distance;
                    _nearestFerryBody = physicalEntity;
                }
                if (vertical > maxVertical) _ferryHeightRejected++;
                else if (distance > boardingGap) _ferryRangeRejected++;
            }
            if (largeShip && distance <= 20f)
            {
                _nearCruise++;
                if (distance < _nearestCruiseGap)
                {
                    _nearestCruiseGap = distance;
                    _nearestCruiseBody = physicalEntity;
                }
                if (vertical > maxVertical) _cruiseHeightRejected++;
                else if (distance > boardingGap) _cruiseRangeRejected++;
            }
            if (vertical > maxVertical || distance > boardingGap) return;
            // A red light, traffic queue, station or quay are equivalent here:
            // only actual physical motion determines boarding eligibility.
            // Observe EVERY nearby body so a moving closer vehicle cannot hide
            // a stopped vehicle a little farther away.
            if (!RWTransitMotion.Observe(physicalEntity,pos,rot,UnityEngine.Time.realtimeSinceStartup,waterVessel))
            {
                if (largeShip) _cruiseMotionRejected++;
                if (ferry) _ferryMotionRejected++;
                return;
            }
            if (largeShip) _cruiseEligible++;
            if (ferry) _ferryEligible++;
            if (distance>=bestDistance) return;
            // No cargo vehicles, taxis or police: only entities carrying PublicTransport.
            best=ResolveDrivingHead(followEntity);
            bestBody=physicalEntity;
            bestKind=kind;
            bestDistance=distance;
        }
        private bool TryPosition(Entity e, out float3 p,out quaternion r)
        {
            p=default(float3);r=quaternion.identity;
            if (EntityManager.HasComponent<InterpolatedTransform>(e))
            {
                var t=EntityManager.GetComponentData<InterpolatedTransform>(e);
                p=t.m_Position;r=t.m_Rotation;return true;
            }
            if (EntityManager.HasComponent<Game.Objects.Transform>(e))
            {
                var t=EntityManager.GetComponentData<Game.Objects.Transform>(e);
                p=t.m_Position;r=t.m_Rotation;return true;
            }
            return false;
        }
        private Entity ResolveDrivingHead(Entity root)
        {
            // A subway controller need not carry the same Train components as a
            // passenger train. Inspect the vehicle layout even without Train tag.
            if (!EntityManager.Exists(root))return root;
            if(EntityManager.HasBuffer<Game.Vehicles.LayoutElement>(root))
            {
                var layout=EntityManager.GetBuffer<Game.Vehicles.LayoutElement>(root,true);
                bool reversed=EntityManager.HasComponent<Game.Vehicles.Train>(root) &&
                    (EntityManager.GetComponentData<Game.Vehicles.Train>(root).m_Flags & Game.Vehicles.TrainFlags.Reversed)!=0;
                for(int i=0;i<layout.Length;i++)
                {
                    Entity lead=layout[reversed?layout.Length-1-i:i].m_Vehicle;
                    if(IsValidRailCar(lead))return lead;
                }
            }
            // DLC and custom trains can expose no LayoutElement on their controller.
            // Then prefer a linked carriage with TrainNavigation; its interpolated
            // transform is the rendering-aware front anchor used by FPC.
            if(EntityManager.HasComponent<Game.Vehicles.TrainNavigation>(root) && IsValidRailCar(root))return root;
            RWVehicleKind kind=GetTransitKind(root);
            if(kind!=RWVehicleKind.Train && kind!=RWVehicleKind.Subway &&
                !EntityManager.HasComponent<Game.Vehicles.Train>(root) &&
                !EntityManager.HasComponent<Game.Vehicles.TrainCurrentLane>(root))return root;
            using(var carriages=_trainCarriages.ToEntityArray(Allocator.Temp))
            {
                Entity first=Entity.Null;
                for(int i=0;i<carriages.Length;i++)
                {
                    Entity car=carriages[i];
                    if(!IsValidRailCar(car))continue;
                    if(EntityManager.GetComponentData<Game.Vehicles.Controller>(car).m_Controller!=root)continue;
                    if(EntityManager.HasComponent<Game.Vehicles.TrainNavigation>(car))return car;
                    if(first==Entity.Null)first=car;
                }
                if(first!=Entity.Null)return first;
            }
            return root;
        }
        private bool IsValidRailCar(Entity car)
        {
            return car!=Entity.Null && EntityManager.Exists(car) &&
                   (EntityManager.HasComponent<InterpolatedTransform>(car) ||
                    EntityManager.HasComponent<Game.Objects.Transform>(car));
        }
        private RWVehicleKind GetTransitKind(Entity e)
        {
            if (!EntityManager.Exists(e) || !EntityManager.HasComponent<PrefabRef>(e))return RWVehicleKind.Unknown;
            var p=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            if (p==Entity.Null || !EntityManager.Exists(p) || !EntityManager.HasComponent<PublicTransportVehicleData>(p))
                return RWVehicleKind.Unknown;
            // The game's public transport prefab is the source of truth; never inspect display text.
            var kind=EntityManager.GetComponentData<PublicTransportVehicleData>(p).m_TransportType;
            switch (kind)
            {
                case TransportType.Bus: return RWVehicleKind.Bus;
                case TransportType.Tram: return RWVehicleKind.Tram;
                case TransportType.Train: return RWVehicleKind.Train;
                case TransportType.Subway: return RWVehicleKind.Subway;
                case TransportType.Ferry: return RWVehicleKind.Ferry;
                case TransportType.Ship: return RWVehicleKind.Ship;
                case TransportType.Airplane: return RWVehicleKind.Airplane;
                default: return RWVehicleKind.Unknown;
            }
        }
    }

}
