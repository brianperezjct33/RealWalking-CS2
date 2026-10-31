// SPDX-License-Identifier: MIT
using System;
using System.Reflection;
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    internal static class ExistingFpcAdapter
    {
        private const string ControllerTypeName = "FirstPersonCameraContinued.MonoBehaviours.FirstPersonCameraController";
        private static Type _controllerType;
        private static MethodInfo _getMode;

        internal static bool TryGetFreeCamera(out Vector3 position, out Vector3 forward, out float fpcHeightOffset, out string why)
        {
            position = default(Vector3);
            forward = Vector3.forward;
            fpcHeightOffset = float.NaN;
            why = null;
            if (_controllerType == null)
            {
                _controllerType = Type.GetType(ControllerTypeName + ", FirstPersonCameraContinued", false);
                if (_controllerType == null)
                {
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (int a = 0; a < assemblies.Length && _controllerType == null; a++)
                        _controllerType = assemblies[a].GetType(ControllerTypeName, false);
                }
                if (_controllerType == null)
                {
                    why = "Embedded FPC controller type missing from standalone assembly; check build.";
                    return false;
                }
                _getMode = _controllerType.GetMethod("GetMode", BindingFlags.Public | BindingFlags.Instance);
                if (_getMode == null)
                {
                    why = "FPC API changed: public GetMode() not found; adapter disabled safely.";
                    return false;
                }
            }

            UnityEngine.Object obj = UnityEngine.Object.FindObjectOfType(_controllerType);
            if (obj == null)
            {
                why = "FPC controller not instantiated yet; enter a city and activate FPC.";
                return false;
            }
            Component controller = obj as Component;
            if (controller == null)
            {
                why = "FPC controller is not a Unity Component.";
                return false;
            }
            object mode = _getMode.Invoke(controller, null);
            if (mode == null || !string.Equals(mode.ToString(), "Manual", StringComparison.Ordinal))
            {
                why = "FPC is not in Manual/free mode (currently " + (mode == null ? "null" : mode.ToString()) + "). Use Alt+F, not follow mode.";
                return false;
            }
            // FPC's VirtualCameraRig.Parent is the controller's own transform.
            // Reading transform position is safer than Camera.main (which can be in a different context).
            // FPC heightOffset is a *manual R/F offset*, not a replacement for eye height.
            // Optional diagnostic reflection only; this does not write to FPC model.
            try
            {
                FieldInfo f = _controllerType.GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic);
                object model = f == null ? null : f.GetValue(controller);
                if (model != null)
                {
                    PropertyInfo hp = model.GetType().GetProperty("HeightOffset", BindingFlags.Instance | BindingFlags.Public);
                    if (hp != null) fpcHeightOffset = Convert.ToSingle(hp.GetValue(model, null));
                }
            }
            catch { /* FPC versions may change private model; position is still valid. */ }
            Transform t = controller.transform;
            position = t.position;
            forward = t.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            else forward.Normalize();
            return true;
        }
    }
}
