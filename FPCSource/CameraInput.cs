// FPC Continued original work © original authors, GPL-2.0.
// Real Walking S1.3 input: movement / sprint / mouse / free-flight height / Escape.
using FirstPersonCameraContinued.DataModels;
using FirstPersonCameraContinued.Enums;
using Game.Input;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonCameraContinued
{
    public class CameraInput
    {
        public Action OnToggle { get; set; }
        public Action OnPrepareManualEntry { get; set; }
        public Action OnFollow { get; set; }
        public Action OnUnfollow { get; set; }
        private readonly CameraDataModel _model;
        private readonly List<InputAction> _actions = new List<InputAction>();
        private InputBarrier _barrier;

        internal CameraInput(CameraDataModel model)
        {
            _model = model;
            Configure();
        }

        private void Add(InputAction action)
        {
            action.Disable();
            _actions.Add(action);
        }

        private void Configure()
        {
            var move = new InputAction("RW_Movement", binding: "<Gamepad>/leftStick");
            move.AddCompositeBinding("Dpad")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.performed += ctx =>
            {
                // A Real Walking transit ride is an intentional, persistent camera
                // follow. Movement input must neither end the ride nor create
                // walking/bobbing motion while seated. Keep upstream-style
                // WASD-to-unfollow for unrelated FPC follow targets.
                if (_model.Mode == CameraMode.Follow &&
                    CS2RealWalkingG1Bridge.RWTransitRide.Active)
                {
                    _model.Movement = float2.zero;
                    return;
                }
                _model.Movement = ctx.ReadValue<Vector2>();
                if (_model.Mode == CameraMode.Follow) OnUnfollow?.Invoke();
            };
            move.canceled += ctx => _model.Movement = float2.zero;
            Add(move);

            var heightUp = new InputAction("RW_HeightUp", binding: "<Keyboard>/r");
            heightUp.performed += ctx =>
            {
                if (_model.Mode == CameraMode.Manual &&
                    CS2RealWalkingG1Bridge.RWWalkState.Mode == CS2RealWalkingG1Bridge.RWTravelMode.Navigation)
                    CS2RealWalkingG1Bridge.RWWalkState.ForceFree("R manual elevation: free flight");
                _model.HeightOffset += _model.Mode == CameraMode.Follow ? .25f : 1f;
            };
            Add(heightUp);

            var heightDown = new InputAction("RW_HeightDown", binding: "<Keyboard>/f");
            heightDown.performed += ctx =>
            {
                if (Keyboard.current == null || !Keyboard.current.altKey.isPressed)
                {
                    if (_model.Mode == CameraMode.Manual &&
                        CS2RealWalkingG1Bridge.RWWalkState.Mode == CS2RealWalkingG1Bridge.RWTravelMode.Navigation)
                        CS2RealWalkingG1Bridge.RWWalkState.ForceFree("F manual elevation: free flight");
                    _model.HeightOffset -= _model.Mode == CameraMode.Follow ? .25f : 1f;
                }
            };
            Add(heightDown);

            var look = new InputAction("RW_MouseLook", binding: "<Mouse>/delta");
            look.performed += ctx => _model.Look = ctx.ReadValue<Vector2>();
            look.canceled += ctx => _model.Look = float2.zero;
            Add(look);

            var sprint = new InputAction("RW_Sprint", binding: "<Keyboard>/leftShift");
            sprint.performed += ctx => _model.IsSprinting = true;
            sprint.canceled += ctx => _model.IsSprinting = false;
            Add(sprint);

            var escape = new InputAction("RW_Exit", binding: "<Keyboard>/escape");
            escape.performed += ctx =>
            {
                Disable();
                CS2RealWalkingG1Bridge.RWAutoEntry.Cancel();
                CS2RealWalkingG1Bridge.RWWalkState.ForceFree("Esc exited first person");
                OnToggle?.Invoke();
                _model.HeightOffset = 0f;
                _model.PositionFollowOffset = float2.zero;
            };
            Add(escape);
        }

        public void Enable()
        {
            _model.Mode = _model.FollowEntity != Entity.Null ? CameraMode.Follow : CameraMode.Manual;
            foreach (var action in _actions) action.Enable();
            var shortcuts = InputManager.instance?.FindActionMap("Shortcuts");
            if (shortcuts != null)
            {
                var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Speed 1", "Speed 2", "Speed 3" };
                var blocked = shortcuts.actions.Where(kv => !allow.Contains(kv.Key)).Select(kv => kv.Value).ToList();
                _barrier = new InputBarrier("RealWalking: temporarily block vanilla shortcuts", blocked,
                    InputManager.DeviceType.Keyboard | InputManager.DeviceType.Gamepad, blocked: true);
            }
        }

        private void Disable()
        {
            foreach (var action in _actions) action.Disable();
            if (_barrier != null)
            {
                _barrier.blocked = false;
                _barrier.Dispose();
                _barrier = null;
            }
            _model.Movement = float2.zero;
            _model.Look = float2.zero;
            _model.IsSprinting = false;
        }

        public void Toggle()
        {
            if (_model.IsTransitioningIn || _model.IsTransitioningOut) return;
            if (_model.Mode != CameraMode.Disabled)
            {
                Disable();
                CS2RealWalkingG1Bridge.RWAutoEntry.Cancel();
                CS2RealWalkingG1Bridge.RWWalkState.ForceFree("camera exited");
            }
            else
            {
                // Exit while riding can leave FPC's internal FollowEntity and
                // FollowEntityFinalTransform alive even when vanilla orbit is
                // no longer following anything. Clear them BEFORE Enable(),
                // which otherwise re-enters directly into Follow mode.
                OnPrepareManualEntry?.Invoke();
                // RC18: record the real orbit camera position BEFORE FPC is
                // enabled. Never capture the transitional first-person camera.
                if (_model.FollowEntity == Entity.Null)
                    CS2RealWalkingG1Bridge.RWAutoEntry.Request();
                Enable();
                if (_model.Mode != CameraMode.Manual)
                    CS2RealWalkingG1Bridge.RWAutoEntry.Cancel();
            }
            OnToggle?.Invoke();
        }

        public void SetEntity(Entity entity) { _model.FollowEntity = entity; }
        public void InvokeOnFollow() { OnFollow?.Invoke(); }
    }
}
