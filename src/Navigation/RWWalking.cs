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
    // Navigation keeps the established Net/Area and boundary solver untouched.
    // LateUpdate publishes absolute world eye height. FPC's manual final transform
    // consumes that value without sampling terrain/water in Navigation mode.
    // FPC continues to own horizontal movement, yaw/pitch and camera interpolation.
    internal sealed partial class RWWalkingController : MonoBehaviour
    {
        private static RWWalkingController _instance;
        private static readonly BindingFlags Props=BindingFlags.Instance|BindingFlags.Public;
        private const float AssumedEyeHeight=1.7f; // Matches G1 read-only diagnostics, configurable FPC height is a future feature.
        private const float AreaEdgeTolerance=.40f; // initial acquisition stays conservative
        private const float SeamStepHeight=.90f;
        // A short virtual RUN-UP only for rising, sloped triangles joined to a known Net lane.
        // Does NOT alter game Nav meshes, allow arbitrary Area expansion, or relax 2m floor guards.
        private const float RampReach=1.75f;
        private const float RampMaxRise=1.80f;
        private const float RampMinGrade=.08f;
        private const float RampMaxGrade=1.65f;
        private Vector3 _lastBoundaryTangent=Vector3.forward;
        private bool _hasSafeXZ;
        private float3 _lastSafePosition;
        private float _currentWidth;
        private bool _failed;
        private GUIStyle _textStyle;
        private bool _hasTarget;
        private float _lastTargetEyeY;
        private float _nextErrorLog;
        private int _generation=-1;
        private static Type _fpcType;
        private static Component _fpcComponent;
        private static object _fpcModel;
        private static FieldInfo _modelField;
        private static PropertyInfo _heightProperty,_positionProperty,_inTransition,_outTransition;
        private static MethodInfo _getMode;

        internal static void Ensure()
        {
            if(_instance!=null)return;
            var go=new GameObject("RW_G1_5_Height_Controller");
            go.hideFlags=HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance=go.AddComponent<RWWalkingController>();
        }
        internal static void Release()
        {
            if(_instance!=null){Destroy(_instance.gameObject);_instance=null;}
        }
        private void LateUpdate()
        {
            if(_generation!=RWWalkState.Generation)
            { _generation=RWWalkState.Generation;_hasTarget=false;
              _hasSafeXZ=false;_currentWidth=0f;_lastBoundaryTangent=Vector3.forward; }
            if (RWWalkState.Mode == RWTravelMode.Free) return;
            try
            {
                if(!UnityEngine.Application.isFocused)return;
                // Explicit manual elevation while navigation is enabled immediately releases camera control.
                var k=Keyboard.current;
                if(RWWalkState.Mode == RWTravelMode.Navigation && k!=null && (k.rKey.isPressed || k.fKey.isPressed))
                {RWWalkState.ForceFree("R/F manual elevation: free flight");return;}
                object model;PropertyInfo hProp,pProp;string reason;
                if(!TryReadModel(out model,out hProp,out pProp,out reason))
                {
                    if(reason=="FPC not in Manual mode" || reason=="FPC controller inactive")
                    {
                        RWWalkState.ForceFree("FPC Manual ended / camera released");
                        return;
                    }
                    RWWalkState.Status="Not attached: "+reason;
                    _hasTarget=false;
                    if(UnityEngine.Time.realtimeSinceStartup>_nextErrorLog)
                    {RWJournal.Warn("[RW-G1] WALK-FPC-NOT-READY v0.9.0 "+reason);_nextErrorLog=UnityEngine.Time.realtimeSinceStartup+8f;}
                    return;
                }
                float offset=(float)hProp.GetValue(model,null);
                float3 position=(float3)pProp.GetValue(model,null);
                if(!IsFinite(offset)||!IsFinite(position.y)||!IsFinite(position.x)||!IsFinite(position.z))
                {RWWalkState.ForceFree("non-finite FPC position or offset");return;}
                float referenceY=_hasTarget?_lastTargetEyeY-AssumedEyeHeight:position.y-AssumedEyeHeight;
                float targetY=position.y;
                float now=UnityEngine.Time.realtimeSinceStartup;
                // Navigation is the only active ground solver mode.
                {
                    var snap=RWOverlayState.Latest;
                    float footSurface=0f,dist=0f,dy=0f,width=0f;string source="none";
                    Vector3 candidateTangent=Vector3.forward;
                    Vector3 queryPos=new Vector3(position.x,position.y,position.z);
                    bool fresh=snap!=null && now-snap.SampleAt<1.25f;
                    float previousFootY=_lastTargetEyeY-AssumedEyeHeight;
                    // If the player has not moved, reuse the last valid surface.
                    bool stationary=_hasTarget&&_hasSafeXZ&&
                        Mathf.Abs(position.x-_lastSafePosition.x)+Mathf.Abs(position.z-_lastSafePosition.z)<.003f;
                    RWWalkState.NavigationMove=_hasSafeXZ?
                        new Vector3(position.x-_lastSafePosition.x,0f,position.z-_lastSafePosition.z):Vector3.zero;
                    bool matched=false;
                    if(stationary)
                    {
                        matched=true;footSurface=previousFootY;
                        source=RWWalkState.Surface;dist=RWWalkState.SurfaceDistance;
                        dy=RWWalkState.Gap;width=_currentWidth;
                        candidateTangent=_lastBoundaryTangent;
                    }
                    else if(fresh)
                    {
                        matched=TryValidCandidate(snap,queryPos,referenceY,_hasTarget,previousFootY,
                            out footSurface,out source,out dist,out dy,out width,out candidateTangent);
                    }
                    bool clampedToBoundary=false,sliding=false;
                    if(!matched && _hasSafeXZ && fresh)
                    {
                        Vector3 accepted;
                        if(TrySlideAlongEdge(snap,_lastSafePosition,queryPos,_lastBoundaryTangent,
                            referenceY,previousFootY,
                            out accepted,out footSurface,out source,out dist,out dy,out width,out candidateTangent))
                        {
                            position.x=accepted.x;position.z=accepted.z;
                            sliding=true;matched=true;
                        }
                        else
                        {
                            // Full stop only when the input has no viable tangential component.
                            position.x=_lastSafePosition.x;position.z=_lastSafePosition.z;
                            matched=TryValidCandidate(snap,new Vector3(position.x,position.y,position.z),
                                referenceY,_hasTarget,previousFootY,
                                out footSurface,out source,out dist,out dy,out width,out candidateTangent);
                        }
                        pProp.SetValue(model,position,null);
                        clampedToBoundary=true;
                    }
                    else if(!matched && _hasSafeXZ && !fresh)
                    {
                        // A stale ECS snapshot must not silently switch layers.
                        position.x=_lastSafePosition.x;position.z=_lastSafePosition.z;
                        pProp.SetValue(model,position,null);
                        clampedToBoundary=true;
                    }
                    if(matched)
                    {
                        targetY=footSurface+AssumedEyeHeight;
                        // G1.9: NEVER low-pass a valid surface height. Fast descent
                        // must stay exactly at the Area interpolation, not hover above it.
                        if(source.StartsWith("NET "))
                        {
                            int netId;
                            if(int.TryParse(source.Substring(4),out netId))RWWalkState.LastNetId=netId;
                        }
                        _hasTarget=true;_lastTargetEyeY=targetY;
                        _lastSafePosition=position;_hasSafeXZ=true;_currentWidth=width;
                        _lastBoundaryTangent=candidateTangent;
                        RWWalkState.Locked=true;RWWalkState.TargetFootY=footSurface;
                        RWWalkState.Surface=source;RWWalkState.SurfaceDistance=dist;RWWalkState.Gap=dy;
                        RWWalkState.Status=clampedToBoundary?(sliding?"NAV SLIDE":"NAV HOLD AT EDGE"):
                            "NAV LOCK / bounded free walk";
                    }
                    else
                    {
                        if(_hasTarget)
                        {
                            // Never auto-switch to flight just because the current
                            // sidewalk or quay has ended. User presses J explicitly.
                            targetY=_lastTargetEyeY;
                            RWWalkState.Status=clampedToBoundary?"NAV HOLD AT EDGE":"NAV HOLD (surface unavailable)";
                            RWWalkState.Locked=false;
                        }
                        else
                        {
                            RWWalkState.Status="NAV SEEK (no verified surface, Ctrl+Shift+J to fly)";
                            RWWalkState.Locked=false;
                            return;
                        }
                    }
                }
                // Publish the validated world-space eye height without terrain/water offset compensation.
                if(!IsFinite(targetY))
                {RWWalkState.ForceFree("unsafe navigation height");return;}
                if(!RWCameraHeight.Publish(targetY))
                {RWWalkState.ForceFree("navigation height unavailable");return;}
            }
            catch(Exception ex)
            {
                if(!_failed)RWJournal.Error("[RW-G1] WALK-ERROR v0.9.0: "+ex);
                _failed=true;
                RWWalkState.ForceFree("controller error: "+ex.GetType().Name);
            }
        }
    }
}
