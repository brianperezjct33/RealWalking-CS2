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
    // Read-only adapter to the embedded FPC manual camera model.
    internal sealed partial class RWWalkingController
    {
        private static bool TryReadModel(out object model,out PropertyInfo height,out PropertyInfo pos,out string reason)
        {
            model=null;height=null;pos=null;reason="";
            if(_fpcType==null)
            {
                _fpcType=Type.GetType("FirstPersonCameraContinued.MonoBehaviours.FirstPersonCameraController, FirstPersonCameraContinued",false);
                if(_fpcType==null)
                {
                    foreach(var a in AppDomain.CurrentDomain.GetAssemblies())
                    {_fpcType=a.GetType("FirstPersonCameraContinued.MonoBehaviours.FirstPersonCameraController",false);if(_fpcType!=null)break;}
                }
                if(_fpcType==null){reason="FPC assembly unavailable";return false;}
                _modelField=_fpcType.GetField("_model",BindingFlags.Instance|BindingFlags.NonPublic);
                _getMode=_fpcType.GetMethod("GetMode",BindingFlags.Instance|BindingFlags.Public);
                if(_modelField==null||_getMode==null){reason="FPC controller API changed";return false;}
            }
            if(_fpcComponent==null)
            {
                _fpcComponent=UnityEngine.Object.FindObjectOfType(_fpcType) as Component;
                _fpcModel=null;
                if(_fpcComponent==null){reason="FPC controller inactive";return false;}
            }
            object m=_getMode.Invoke(_fpcComponent,null);
            if(m==null||m.ToString()!="Manual") {reason="FPC not in Manual mode";return false;}
            if(_fpcModel==null)
            {
                _fpcModel=_modelField.GetValue(_fpcComponent);
                if(_fpcModel==null){reason="FPC _model unavailable";return false;}
                var t=_fpcModel.GetType();
                _heightProperty=t.GetProperty("HeightOffset",Props);
                _positionProperty=t.GetProperty("Position",Props);
                _inTransition=t.GetProperty("IsTransitioningIn",Props);
                _outTransition=t.GetProperty("IsTransitioningOut",Props);
                if(_heightProperty==null||!_heightProperty.CanRead||!_heightProperty.CanWrite||_heightProperty.PropertyType!=typeof(float)||
                   _positionProperty==null||!_positionProperty.CanRead||!_positionProperty.CanWrite||_positionProperty.PropertyType!=typeof(float3)||
                   _inTransition==null||_outTransition==null)
                {reason="FPC model properties changed: refusing write";return false;}
            }
            if((bool)_inTransition.GetValue(_fpcModel,null)||(bool)_outTransition.GetValue(_fpcModel,null))
            {reason="FPC transition active";return false;}
            model=_fpcModel;height=_heightProperty;pos=_positionProperty;
            return true;
        }
        // Match a new camera position without any horizontal movement correction.
        // Keeping this check centralized ensures slide probes obey the same layer guard.
    }
}
