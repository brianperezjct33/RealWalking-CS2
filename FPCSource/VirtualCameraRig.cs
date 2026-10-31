using Cinemachine;
using FirstPersonCameraContinued.DataModels;
using Game.Rendering;
using Game.Simulation;
using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FirstPersonCameraContinued
{
    /// <summary>
    /// Handles game object creation and rigging.
    /// </summary>
    public class VirtualCameraRig
    {
        private float TRANSITION_DAMPEN = 3.5f;
        private float FOV_END = 70f;

        /// <summary>
        /// The core transform which handles position and rotation.
        /// </summary>
        public Transform Parent
        {
            get
            {
                return _target;
            }
        }

        /// <summary>
        /// A seperate GameObject that holds the virtual camera.
        /// </summary>
        public Transform CameraTransform
        {
            get;
            private set;
        }

        /// <summary>
        /// The child transform of the parent, used to represent
        /// pitch rotation.
        /// </summary>
        public Transform RigTransform
        {
            get;
            private set;
        }

        public float GetFOV()
        {
            return _virtualCamera.m_Lens.FieldOfView;
        }

        /// <summary>
        /// Event for transition end
        /// </summary>
        public Action<bool> OnTransitionComplete;

        private readonly CameraDataModel _model;
        private readonly Transform _target;
        private readonly CinemachineVirtualCamera _virtualCamera;
        private readonly CameraUpdateSystem _cameraUpdateSystem;

        private quaternion startRotation;
        private float3 startPosition;
        private float startFOV;

        // The normal orbit framing is overwritten while the FPS rig is active.
        // Save it on entry, and on exit aim that view near the actual last rendered
        // eye location. Never translate the entry aerial camera position: doing
        // so caused a long pan/teleport when ending first-person far from entry.
        private float3 _entryOrbitPivot;
        private float3 _entryOrbitRotation;
        private float _entryOrbitZoom;
        private float3 _exitOrbitPivot;
        private bool _hasOrbitSnapshot;

        public VirtualCameraRig( CameraDataModel model, Transform target ) 
        {
            _model = model;

            var world = World.DefaultGameObjectInjectionWorld;
            _cameraUpdateSystem = world.GetExistingSystemManaged<CameraUpdateSystem>( );
            _target = target;

            RigTransform = new GameObject( "FirstPersonCamera_SubObject" ).transform;
            RigTransform.parent = target;
            RigTransform.position = target.position;

            CameraTransform = new GameObject( "FirstPersonCamera_VCam" ).transform;
            _virtualCamera = CameraTransform.gameObject.AddComponent<CinemachineVirtualCamera>( );
            _virtualCamera.Follow = target; 

            Configure( );
        }

        /// <summary>
        /// Configure the rig settings.
        /// </summary>
        private void Configure( )
        {
            // Get settings from the working camera
            var workingCamera = ( CinemachineVirtualCamera ) _cameraUpdateSystem.orbitCameraController.virtualCamera;
            _virtualCamera.m_Lens = workingCamera.m_Lens;

            _virtualCamera.Priority = 0;
            _virtualCamera.m_Lens.NearClipPlane = 0.03f;

            startFOV = workingCamera.m_Lens.FieldOfView;
            _virtualCamera.m_Lens.FieldOfView = startFOV;

            CameraTransform.position = _cameraUpdateSystem.activeCamera.transform.position;
            Parent.position = CameraTransform.position;

            _model.Position = Parent.position;
            _model.Rotation = Parent.rotation;
            _model.ChildRotation = RigTransform.localRotation;
        }

        /// <summary>
        /// Update the rig transforms, applying linear interpolation
        /// if specified and transitions.
        /// </summary>
        /// <param name="shouldLerp"></param>
        public void Update( bool noLerp = false )
        {
            //change FOV transitioning to if specified in settings
            if (Mod.FirstPersonModSettings != null)
            {
                FOV_END = Mod.FirstPersonModSettings.FOV;
                TRANSITION_DAMPEN = Mod.FirstPersonModSettings.TransitionSpeedFactor * 3.5f;
            }

            // Check if transitioning, we dampen the position more for smoothness
            if ( _model.IsTransitioningIn || _model.IsTransitioningOut )
            {
                // Transition to the target position
                if ( _model.IsTransitioningIn )
                {
                    Parent.position = Vector3.Lerp( Parent.position, _model.Position, TRANSITION_DAMPEN * Time.deltaTime );

                    Parent.rotation = Quaternion.Lerp( Parent.rotation, _model.Rotation, TRANSITION_DAMPEN * Time.deltaTime );
                    RigTransform.localRotation = Quaternion.Lerp( RigTransform.localRotation, _model.ChildRotation, TRANSITION_DAMPEN * Time.deltaTime );

                    CameraTransform.transform.position = RigTransform.position;
                    CameraTransform.transform.rotation = RigTransform.rotation;
                    
                    // math.lerp doesn't clamp, fast factors with frame hitches overshoot the FOV
                    _virtualCamera.m_Lens.FieldOfView = math.lerp( _virtualCamera.m_Lens.FieldOfView, FOV_END, math.saturate( TRANSITION_DAMPEN * Time.deltaTime ) );

                    // We've arrived
                    if ( Vector3.Distance( Parent.position, _model.Position ) <= 0.125f )
                    {
                        _virtualCamera.m_Lens.FieldOfView = FOV_END;
                        _model.IsTransitioningIn = false;
                        OnTransitionComplete?.Invoke( false );
                    }
                }
                // Transition to the orbit camera
                else if ( _model.IsTransitioningOut )
                {
                    Parent.position = Vector3.Lerp( Parent.position, startPosition, TRANSITION_DAMPEN * Time.deltaTime );
                    
                    Parent.rotation = Quaternion.Lerp( Parent.rotation, startRotation, TRANSITION_DAMPEN * Time.deltaTime );
                    RigTransform.localRotation = Quaternion.Lerp( RigTransform.localRotation, Quaternion.identity, TRANSITION_DAMPEN * Time.deltaTime );

                    CameraTransform.transform.position = RigTransform.position;
                    CameraTransform.transform.rotation = RigTransform.rotation;
                    
                    _virtualCamera.m_Lens.FieldOfView = math.lerp( _virtualCamera.m_Lens.FieldOfView, startFOV, math.saturate( TRANSITION_DAMPEN * Time.deltaTime ) );

                    // We've arrived
                    if ( Vector3.Distance( Parent.position, startPosition ) <= 0.125f )
                    {
                        _virtualCamera.m_Lens.FieldOfView = startFOV;
                        _model.IsTransitioningOut = false;
                        _virtualCamera.Priority = 0;
                        OnTransitionComplete?.Invoke( true );
                    }
                }
            }
            else
            {
                if ( !noLerp )
                    Parent.position = Vector3.Lerp( Parent.position, _model.Position, 10f * Time.deltaTime );
                else
                    Parent.position = _model.Position;

                Parent.rotation = Quaternion.Lerp( Parent.rotation, _model.Rotation, 12f * Time.deltaTime );
                RigTransform.localRotation = Quaternion.Lerp( RigTransform.localRotation, _model.ChildRotation, 12f * Time.deltaTime );

                CameraTransform.transform.position = RigTransform.position;
                CameraTransform.transform.rotation = RigTransform.rotation;
            }
        }

        /// <summary>
        /// Set the rig to active or disabled
        /// </summary>
        /// <param name="isActive"></param>
        public void SetActive( bool isActive )
        {            
            if ( _model.IsTransitioningIn )
                _virtualCamera.Priority = 99999;
            //else if ( _model.IsTransitioningOut )
            //    _virtualCamera.Priority = 0;

            // Update base position of FPS camera
            if ( isActive )
            {
                var cam = _cameraUpdateSystem.activeCamera;
                var orbit = _cameraUpdateSystem.orbitCameraController;
                _entryOrbitPivot = orbit.pivot;
                _entryOrbitRotation = orbit.rotation;
                _entryOrbitZoom = orbit.zoom;
                _exitOrbitPivot = _entryOrbitPivot;
                _hasOrbitSnapshot = true;

                startRotation = cam.transform.rotation;
                startPosition = cam.transform.position;
                Parent.position = startPosition;
                _model.Position = startPosition;
            }
            else if (_hasOrbitSnapshot)
            {
                // The rig's Parent is the last *rendered* first-person location.
                // The model can be far ahead of it during entry transitions or
                // entity follow, so it must not be used to recenter the orbit.
                float3 eye = Parent.position;
                if (!math.all(math.isfinite(eye))) eye = _model.Position;
                if (!math.all(math.isfinite(eye))) eye = startPosition;

                float dx = eye.x - startPosition.x;
                float dz = eye.z - startPosition.z;
                if (math.all(math.isfinite(eye)) && dx * dx + dz * dz > 0.25f)
                {
                    // Default to the ground-level eye target. Underground
                    // stations and water should not drag an orbit pivot many
                    // meters below the city surface when leaving first person.
                    float eyeHeight = Mod.FirstPersonModSettings != null
                        ? Mod.FirstPersonModSettings.CimHeight : 1.7f;
                    float pivotY = eye.y - eyeHeight;
                    try
                    {
                        var world = World.DefaultGameObjectInjectionWorld;
                        var terrain = world?.GetExistingSystemManaged<TerrainSystem>();
                        if (terrain != null)
                        {
                            var heights = terrain.GetHeightData();
                            float sampledY = TerrainUtils.SampleHeight(ref heights,
                                new float3(eye.x, 0f, eye.z));
                            if (math.isfinite(sampledY) && sampledY > pivotY)
                                pivotY = sampledY;
                        }
                    }
                    catch (Exception) { /* terrain can unload while exiting a city */ }
                    _exitOrbitPivot = new float3(eye.x, pivotY, eye.z);
                }

                UnityEngine.Debug.Log("[RW-CAMERA] exit eye=" + eye +
                    " entryCamera=" + startPosition +
                    " entryPivot=" + _entryOrbitPivot +
                    " exitPivot=" + _exitOrbitPivot +
                    " savedZoom=" + _entryOrbitZoom);
            }
        }

        /// <summary>
        /// Cut the FPS virtual camera immediately. An outgoing transition toward
        /// a translated old aerial camera position was causing extreme flyaways.
        /// The default orbit camera takes over with its saved framing.
        /// </summary>
        public void CompleteExitImmediately()
        {
            _model.IsTransitioningIn = false;
            _model.IsTransitioningOut = false;
            _virtualCamera.Priority = 0;
        }

        /// <summary>
        /// Recenter the orbit camera at the last first-person world position,
        /// restoring the previous rotation and zoom without a second translation.
        /// Call once after the FPS module stops writing activeCameraController.
        /// </summary>
        public void RestoreOrbitAfterExit()
        {
            if (!_hasOrbitSnapshot) return;
            var orbit = _cameraUpdateSystem.orbitCameraController;
            orbit.rotation = _entryOrbitRotation;
            orbit.zoom = _entryOrbitZoom;
            orbit.pivot = _exitOrbitPivot;
            _hasOrbitSnapshot = false;
        }

        /// <summary>
        /// Update the camera near clip plane
        /// </summary>
        /// <param name="nearClipPlane"></param>
        public void UpdateNearClipPlane( float nearClipPlane = 0.03f )
        {
            _virtualCamera.m_Lens.NearClipPlane = nearClipPlane;
        }


    }
}
