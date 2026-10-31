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
    internal static class RWTransitFpc
    {
        private static Component _controller;
        private static Type _controllerType;
        private static FieldInfo _modelField;
        private static PropertyInfo _positionProperty;
        private static MethodInfo _getMode;
        private static PropertyInfo _modelPos;
        internal static bool TryGetManualPosition(out Vector3 position)
        {
            position=Vector3.zero;
            try
            {
                if(!TryConnect())return false;
                object mode=_getMode.Invoke(_controller,null);
                if(mode==null||mode.ToString()!="Manual")return false;
                object model=_modelField.GetValue(_controller);
                if(model==null)return false;
                if(_positionProperty==null)_positionProperty=model.GetType().GetProperty("Position");
                if(_positionProperty==null||_positionProperty.PropertyType!=typeof(float3))return false;
                var p=(float3)_positionProperty.GetValue(model,null);
                position=new Vector3(p.x,p.y,p.z);
                return !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
                       !float.IsNaN(p.y) && !float.IsInfinity(p.y) &&
                       !float.IsNaN(p.z) && !float.IsInfinity(p.z);
            }
            catch{return false;}
        }
        private static bool TryConnect()
        {
            if(_controller!=null)return true;
            if(_controllerType==null)
            {
                _controllerType=Type.GetType("FirstPersonCameraContinued.MonoBehaviours.FirstPersonCameraController, FirstPersonCameraContinued",false);
                if(_controllerType==null)
                    foreach(var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        _controllerType=a.GetType("FirstPersonCameraContinued.MonoBehaviours.FirstPersonCameraController",false);
                        if(_controllerType!=null)break;
                    }
                if(_controllerType==null)return false;
                _modelField=_controllerType.GetField("_model",BindingFlags.Instance|BindingFlags.NonPublic);
                _getMode=_controllerType.GetMethod("GetMode",BindingFlags.Instance|BindingFlags.Public);
                if(_modelField==null||_getMode==null)return false;
            }
            _controller=UnityEngine.Object.FindObjectOfType(_controllerType) as Component;
            return _controller!=null;
        }
        internal static bool TryBoard(Entity vehicle)
        {
            // Boarding is possible ONLY from navigation walking, never while
            // free flying or when another Real Walking ride is already active.
            if(RWWalkState.Mode!=RWTravelMode.Navigation || RWTransitRide.Active ||
               vehicle==Entity.Null || vehicle!=RWTransitState.Candidate ||
               RWTransitState.PhysicalVehicle==Entity.Null ||
               !UnityEngine.Application.isFocused || !RWTransitState.Ready || !TryConnect())return false;
            try
            {
                if(_getMode.Invoke(_controller,null).ToString()!="Manual")return false;
                World w=World.DefaultGameObjectInjectionWorld;
                if(w==null || !w.EntityManager.Exists(vehicle))return false;
                var cameras=w.GetExistingSystemManaged<CameraUpdateSystem>();
                if(cameras==null || cameras.orbitCameraController==null)return false;
                // Recheck motion immediately before ANY follow-camera mutation.
                // If the vehicle has just started moving, E must not alter the
                // camera's height offset or start a new ride.
                if (!RWTransitMotion.IsStoppedNow(RWTransitState.PhysicalVehicle, (RWTransitState.Kind == RWVehicleKind.Ship || RWTransitState.Kind == RWVehicleKind.Ferry))) return false;
                // The same public game setter used by FPC's own follow feature. FPC's patch
                // switches Manual->Follow and applies vehicle-specific forward-camera offset.
                // RC13: Navigation no longer changes HeightOffset. A player can
                // still enter Navigation from a high free flight, so do not pass
                // that *native flight* offset into a vehicle-follow camera.
                // Reset for all types (including ships and ferries) before Follow.
                object cameraModel=_modelField.GetValue(_controller);
                var heightProperty=cameraModel==null?null:
                    cameraModel.GetType().GetProperty("HeightOffset");
                if(heightProperty!=null && heightProperty.CanWrite &&
                   heightProperty.PropertyType==typeof(float))
                    heightProperty.SetValue(cameraModel,0f,null);
                cameras.orbitCameraController.followedEntity=vehicle;
                RWTransitRide.Start(vehicle, RWTransitState.PhysicalVehicle, RWTransitState.Kind);
                RWJournal.Warn("[RW-TRANSIT] BOARD-GEOMETRY trainNav="+
                    w.EntityManager.HasComponent<Game.Vehicles.TrainNavigation>(vehicle)+
                    " isRailCar="+w.EntityManager.HasComponent<Game.Vehicles.TrainCurrentLane>(vehicle)+
                    " body="+RWTransitState.PhysicalVehicle.Index+":"+RWTransitState.PhysicalVehicle.Version);
                RWJournal.Warn("[RW-TRANSIT] BOARD entity="+vehicle.Index+":"+vehicle.Version+
                    " result=Follow requested");
                RWTransitState.Clear();
                // Free here disables ground navigation while FPC is in FOLLOW.
                // This is a riding state, NOT user-controlled free flight.
                RWWalkState.ForceFree("transit ride: native FPC follow");
                return true;
            }
            catch(Exception e)
            {
                RWJournal.Error("[RW-TRANSIT] BOARD-ERROR "+e);
                return false;
            }
        }
        // Explicit flight release is NOT station disembarkation: the user may
        // choose to fly from a moving bus, train, ferry, or an elevated station.
        // Keep the current world-space camera position instead of reusing the
        // pre-boarding manual position. Free flight keeps its absolute world Y
        // and does not need the former terrain/water HeightOffset baseline.
        internal static bool TryLeaveRideForFreeFlight(out string reason)
        {
            reason = "";
            if(!RWTransitRide.Active || !TryConnect())
            {reason="no active Real Walking ride or camera";return false;}
            try
            {
                if(!IsFollowingRide(RWTransitRide.Followed))
                {reason="camera is no longer following our ride";return false;}
                object model=_modelField.GetValue(_controller);
                if(model==null){reason="camera model unavailable";return false;}
                var type=model.GetType();
                var position=type.GetProperty("Position");
                var height=type.GetProperty("HeightOffset");
                var followOffset=type.GetProperty("PositionFollowOffset");
                if(position==null || !position.CanWrite || position.PropertyType!=typeof(float3) ||
                   height==null || !height.CanWrite || height.PropertyType!=typeof(float))
                {reason="FPC model layout changed";return false;}
                float3 eye=(float3)position.GetValue(model,null);
                if(!math.all(math.isfinite(eye)))
                {reason="invalid current camera position";return false;}
                var world=World.DefaultGameObjectInjectionWorld;
                var cameras=world==null?null:world.GetExistingSystemManaged<CameraUpdateSystem>();
                if(cameras==null || cameras.orbitCameraController==null)
                {reason="camera unavailable";return false;}

                // Free flight uses absolute world height. Keep the currently
                // rendered follow eye and reset the R/F input accumulator.
                // The first manual transform frame rebases the input delta.
                const float manualOffset = 0f;

                // The existing FPC Harmony callback switches Follow -> Manual.
                cameras.orbitCameraController.followedEntity=Entity.Null;
                object mode=_getMode.Invoke(_controller,null);
                if(mode==null || mode.ToString()!="Manual")
                {reason="FPC did not return to Manual mode";return false;}
                position.SetValue(model,eye,null);
                height.SetValue(model,manualOffset,null);
                if(followOffset!=null && followOffset.CanWrite &&
                   followOffset.PropertyType==typeof(float2))
                    followOffset.SetValue(model,float2.zero,null);

                RWTransitRide.End("manual free-flight release");
                RWTransitState.Clear();
                RWWalkState.ForceFree("user switched from transit to free flight");
                RWJournal.Warn("[RW-TRANSIT] FREE-FLIGHT-RELEASE eye="+eye+" height="+manualOffset);
                return true;
            }
            catch(Exception e)
            {reason=e.GetType().Name+": "+e.Message;return false;}
        }

        internal static bool IsFollowingRide(Entity vehicle)
        {
            try
            {
                if(!TryConnect()||_getMode.Invoke(_controller,null).ToString()!="Follow")return false;
                object model=_modelField.GetValue(_controller);
                var prop=model.GetType().GetProperty("FollowEntity");
                return prop!=null && (Entity)prop.GetValue(model,null)==vehicle;
            }
            catch{return false;}
        }
        internal static bool TryDisembark(Vector3 landingEye, out string reason)
        {
            reason="";
            try
            {
                if(!TryConnect()){reason="FPC unavailable";return false;}
                if(!IsFollowingRide(RWTransitRide.Followed)){reason="not following our ride";return false;}
                World world=World.DefaultGameObjectInjectionWorld;
                var cameras=world==null?null:world.GetExistingSystemManaged<CameraUpdateSystem>();
                if(cameras==null||cameras.orbitCameraController==null){reason="camera unavailable";return false;}
                var model=_modelField.GetValue(_controller);
                if(model==null){reason="camera model unavailable";return false;}
                var type=model.GetType();
                if(_modelPos==null)_modelPos=type.GetProperty("Position");
                if(_modelPos==null||!_modelPos.CanWrite||_modelPos.PropertyType!=typeof(float3))
                {reason="FPC model layout changed";return false;}
                // FPC's own OnFollowChanged patch switches Follow -> Manual. Only
                // after that set Manual camera position, then restore RW navigation.
                cameras.orbitCameraController.followedEntity=Entity.Null;
                object mode=_getMode.Invoke(_controller,null);
                if(mode==null||mode.ToString()!="Manual")
                {reason="FPC didn't switch to Manual";return false;}
                _modelPos.SetValue(model,new float3(landingEye.x,landingEye.y,landingEye.z),null);
                // RC13: seed the verified platform world height into the
                // navigation bridge immediately after returning to Manual.
                // Do not sample terrain/water or calculate an FPC offset here.
                RWWalkState.EnableNavigation();
                if(!RWCameraHeight.Publish(landingEye.y))
                {reason="unable to seed disembark navigation height";return false;}
                RWWalkingController.Ensure();
                return true;
            }
            catch(Exception ex){reason=ex.GetType().Name+": "+ex.Message;return false;}
        }
    }

}
