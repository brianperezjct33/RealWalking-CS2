// FPC Continued original work © original authors, GPL-2.0.
// Real Walking S1.3: sole remaining FPC UI responsibility is the Alt+F input action.
using FirstPersonCameraContinued.MonoBehaviours;
using Game;
using Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonCameraContinued.Systems
{
    public partial class FirstPersonCameraUISystem : GameSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            var settings = Mod.FirstPersonModSettings;
            if (settings == null)
            {
                Mod.log.Warn("[RW-LITE] Keybinding settings missing during UI setup.");
                return;
            }
            ProxyAction action = settings.GetAction(Mod.kButtonActionName);
            if (action == null)
            {
                Mod.log.Warn("[RW-LITE] Alt+F action not found.");
                return;
            }
            action.shouldBeEnabled = true;
            action.onInteraction += (_, phase) =>
            {
                if (phase != InputActionPhase.Performed || !Application.isFocused) return;
                var camera = World.GetExistingSystemManaged<FirstPersonCameraSystem>();
                if (camera == null) return;
                try
                {
                    if (!camera.TryRecoverController()) return;
                    var controller = UnityEngine.Object.FindObjectOfType<FirstPersonCameraController>();
                    if (controller == null) return;
                    controller.GetCameraInput()?.Toggle();
                }
                catch (System.Exception ex)
                {
                    Mod.log.Error("[RW-LITE] Alt+F failed: " + ex);
                }
            };
            Mod.log.Info("[RW-LITE] Alt+F input registered (no FPC legacy panels).");
        }
        protected override void OnUpdate() { }
    }
}
