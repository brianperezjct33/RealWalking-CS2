using FirstPersonCameraContinued.DataModels;
using FirstPersonCameraContinued.Enums;
using FirstPersonCameraContinued.Patches;
using FirstPersonCameraContinued.Systems;
using FirstPersonCameraContinued.Transforms;
using Game.Audio;
using Game.Citizens;
using Game.Rendering;
using Game.Simulation;
using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FirstPersonCameraContinued.MonoBehaviours
{
    /// <summary>
    /// A basic FPS controller toggled via CTRL + F.
    /// </summary>
    /// <remarks>
    /// (Is smoother and slower/more realistic as an FPS controller than Photo mode. This
    /// could be expanded much further as a proper mod.)
    /// </remarks>
    public class FirstPersonCameraController : MonoBehaviour
    {
        private bool IsActive
        {
            get;
            set;
        }

        public CameraTransformer GetTransformer()
        {
            return _transformer;
        }

        public CameraInput GetCameraInput()
        {
            return _input;
        }

        public Entity GetFollowEntity()
        {
            return _model.FollowEntity;
        }

        public quaternion GetViewRotation()
        {
            return _model.Rotation;
        }

        public CameraMode GetMode()
        {
            return _model.Mode;
        }

        public VehicleType GetScopeVehicle()
        {
            return _model.ScopeVehicle;
        }

        public VirtualCameraRig GetRig()
        {
            return _rig;
        }

private FirstPersonCameraSystem _firstPersonCameraSystem;
        private TerrainSystem terrainSystem;
        private RenderingSystem renderingSystem;

        private float storedLODScale;
        private float storedAmbienceVolume;

        private CameraDataModel _model;
        private CameraInput _input;
        private CameraTransformer _transformer;
        private VirtualCameraRig _rig;
        private CameraRaycaster _raycaster;
        private CameraUpdateSystem _cameraUpdateSystem;

        public void Awake()
        {
            _model = new CameraDataModel( );
            _input = new CameraInput( _model );
            _rig = new VirtualCameraRig( _model, transform );
            _transformer = new CameraTransformer( _rig, _model );
            _raycaster = new CameraRaycaster( );

            var world = World.DefaultGameObjectInjectionWorld;
            _cameraUpdateSystem = world.GetExistingSystemManaged<CameraUpdateSystem>( );
            terrainSystem = world.GetExistingSystemManaged<TerrainSystem>( );
            renderingSystem = world.GetOrCreateSystemManaged<RenderingSystem>( );
            _firstPersonCameraSystem = world.GetExistingSystemManaged<FirstPersonCameraSystem>( );
            SetupEvents( );
        }

        /// <summary>
        /// Setup events for the camera
        /// </summary>
        private void SetupEvents( )
        {
            _input.OnToggle = Toggle;
            _input.OnPrepareManualEntry = () => ResetFollowingForWalk("entry");
            _input.OnFollow = ( ) => { FollowTrigger( true ); };
            _input.OnUnfollow = ( ) => { FollowTrigger( false ); };
            _rig.OnTransitionComplete = OnTransitionComplete;
            _transformer.OnScopeChanged = OnScopeChanged;
        }

        private void Start( )
        {
            DontDestroyOnLoad( this );
        }

        /// <summary>
        /// Update the camera
        /// </summary>
        public void UpdateCamera( )
        {
            if ( terrainSystem == null || !IsActive )
                return;

            InternalUpdate( );
        }

        /// <summary>
        /// Internal update logic
        /// </summary>
        private void InternalUpdate( )
        {
            _transformer.Apply( );
            _rig.Update( _model.Mode == CameraMode.Follow );

            AudioManager.instance?.UpdateAudioListener( transform.position, _rig.RigTransform.rotation );
            _cameraUpdateSystem.activeCameraController.rotation = _rig.RigTransform.rotation.eulerAngles;
            _cameraUpdateSystem.activeCameraController.pivot = transform.position;
            _cameraUpdateSystem.activeCameraController.zoom = 0f;

        }

        /// <summary>
        /// Exit / re-entry guard for an abandoned public-transport camera follow.
        /// Explicitly clears the game orbit target, both FPC entity references,
        /// and the FollowEntityFinalTransform even if the orbit setter's Harmony
        /// event was skipped.  Never moves the camera or changes the player's save.
        /// </summary>
        private void ResetFollowingForWalk(string phase)
        {
            var orbit = _cameraUpdateSystem.orbitCameraController;
            Entity previousOrbit = orbit.followedEntity;
            Entity previousModel = _model.FollowEntity;
            if (previousOrbit != Entity.Null)
                orbit.followedEntity = Entity.Null;
            _transformer.ResetFollowForManual();
            _model.Movement = float2.zero;
            _model.Look = float2.zero;
            _model.HeightOffset = 0f;
            CS2RealWalkingG1Bridge.RWTransitRide.End("camera " + phase);
            CS2RealWalkingG1Bridge.RWTransitState.Clear();
            if (previousOrbit != Entity.Null || previousModel != Entity.Null)
                Debug.Log("[RW-CAMERA] FOLLOW-CLEAR phase=" + phase +
                    " orbit=" + previousOrbit.Index + ":" + previousOrbit.Version +
                    " fpc=" + previousModel.Index + ":" + previousModel.Version);
        }

        /// <summary>
        /// Toggle the camera on or off
        /// </summary>
        public void Toggle( )
        {
            // Exit is an instant handoff to the vanilla orbit camera. The old
            // outbound interpolation aimed at a shifted pre-entry aerial-camera
            // position and could fly a huge distance before restoring the view.
            if (IsActive)
            {
                _model.IsTransitioningIn = false;
                _model.IsTransitioningOut = false;
                _rig.SetActive(false); // snapshot the current *rendered* eye

                // Snapshot the rendered eye FIRST. Detach every follow state
                // before returning control to the vanilla orbit camera.
                ResetFollowingForWalk("exit");

                _rig.CompleteExitImmediately();
                OrbitCameraController_UpdateCameraPatch.overrideUpdate = false;
                try
                {
                    OnTransitionComplete(true);
                }
                finally
                {
                    _rig.RestoreOrbitAfterExit();
                }
                return;
            }

            // Preserve the proven first-person entry transition. Only exit uses
            // an immediate handoff to avoid traveling toward obsolete coordinates.
            _model.IsTransitioningIn = true;
            _model.IsTransitioningOut = false;
            IsActive = true;
            _firstPersonCameraSystem.EntryInfo.Activated = true;
            Debug.Log("IsTransitioningIn");
            _rig.SetActive(true);

            Cursor.lockState = CursorLockMode.Locked;
            storedLODScale = renderingSystem.levelOfDetail;
            renderingSystem.levelOfDetail = storedLODScale * 0.85f;

            storedAmbienceVolume = AudioManager.instance.ambienceVolume;
            AudioManager.instance.ambienceVolume = storedAmbienceVolume * 0.35f;
            _cameraUpdateSystem.orbitCameraController.inputEnabled = false;
            OrbitCameraController_UpdateCameraPatch.overrideUpdate = true;
            _firstPersonCameraSystem.ToggleUI(true);
        }

        /// <summary>
        /// Sets the followed entity
        /// </summary>
        /// <param name="follow"></param>
        private void FollowTrigger( bool follow )
        {
            if ( follow && _raycaster.TryRaycast( out var hit ) )
                _cameraUpdateSystem.orbitCameraController.followedEntity = hit.m_Owner;
            else if ( !follow && _model.Mode == CameraMode.Follow )
                _cameraUpdateSystem.orbitCameraController.followedEntity = Entity.Null;
        }

        /// <summary>
        /// When the scope changes adjust parameters accordingly
        /// </summary>
        private void OnScopeChanged( )
        {
            switch ( _model.Scope )
            {
                case CameraScope.Citizen:
                    _rig.UpdateNearClipPlane( 0.3f );
                    break;

                default:
                    _rig.UpdateNearClipPlane( );
                    break;
            }
        }

        /// <summary>
        /// Occurs when a transition is finished
        /// </summary>
        /// <param name="isTransitionOut"></param>
        private void OnTransitionComplete( bool isTransitionOut )
        {
            // Restore vanilla settings
            if ( isTransitionOut )
            {
                Cursor.lockState = CursorLockMode.None;
                renderingSystem.levelOfDetail = storedLODScale;
                AudioManager.instance.ambienceVolume = storedAmbienceVolume;
                _cameraUpdateSystem.orbitCameraController.inputEnabled = true;
                _firstPersonCameraSystem.ToggleUI( false );
                IsActive = false; // Update the IsActive status to off now
                _firstPersonCameraSystem.EntryInfo.Activated = false;
                GameObject.Destroy(GameObject.Find("toastTextFPC"));
                _model.Mode = CameraMode.Disabled;
            }
        }
    }
}
