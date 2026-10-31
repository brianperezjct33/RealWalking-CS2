// FPC Continued original work © original authors, GPL-2.0.
// Real Walking S1.3 camera lifecycle; no PiP, zoom, or legacy FPC UI dependencies.
using FirstPersonCameraContinued.DataModels;
using FirstPersonCameraContinued.MonoBehaviours;
using Game;
using Game.Common;
using Game.Rendering;
using Game.Tools;
using Game.SceneFlow;
using UnityEngine;

namespace FirstPersonCameraContinued.Systems
{
    public partial class FirstPersonCameraSystem : GameSystemBase
    {
        public EntryInfo EntryInfo { get; set; }
        private FirstPersonCameraController Controller { get; set; }
        private bool _loggedMissingController;

        protected override void OnCreate()
        {
            base.OnCreate();
            EntryInfo = new EntryInfo { Activated = false };
            // Defer camera MonoBehaviour creation until UIUpdate. Its Awake requires
            // game rendering/scene systems, which may be unavailable during OnCreate.
            Mod.log.Info("[RW-LITE] Camera ECS system created; waiting for UIUpdate recovery.");
        }
        protected override void OnUpdate() { }

        public void UpdateCamera()
        {
            if (Controller == null)
            {
                if (!_loggedMissingController)
                {
                    _loggedMissingController = true;
                    Mod.log.Warn("[RW-LITE] CAMERA-MISSING; PreCulling skipped.");
                }
                return;
            }
            _loggedMissingController = false;
            Controller.UpdateCamera();
        }

        public bool TryRecoverController()
        {
            if (Controller != null) return true;
            // The rendering and camera-update systems must be present before
            // constructing FirstPersonCameraController's Unity Awake lifecycle.
            if (World.GetExistingSystemManaged<Game.Rendering.CameraUpdateSystem>() == null ||
                World.GetExistingSystemManaged<Game.Simulation.TerrainSystem>() == null)
                return false;
            var existingObj = GameObject.Find(nameof(FirstPersonCameraController));
            Controller = existingObj != null ? existingObj.GetComponent<FirstPersonCameraController>() : null;
            if (Controller == null) Controller = UnityEngine.Object.FindObjectOfType<FirstPersonCameraController>();
            if (Controller == null)
            {
                if (existingObj != null) Controller = existingObj.AddComponent<FirstPersonCameraController>();
                else Controller = new GameObject(nameof(FirstPersonCameraController)).AddComponent<FirstPersonCameraController>();
            }
            if (Controller != null)
            {
                _loggedMissingController = false;
                Mod.log.Info("[RW-LITE] CAMERA-CONTROLLER-READY.");
            }
            return Controller != null;
        }

        // S1.7.6: Alt+F enters a CLEAN pedestrian first-person view.
        // Ported from upstream FirstPersonCameraContinued ToggleUI (GPLv2),
        // without its obsolete hard-coded UI/CSS assumptions or other UI modules.
        // No ECS/save edits; preserve pre-existing overlay/raycast states on exit.
        private bool _rwUiHidden;
        private bool _rwPreviousOverlayHidden;
        private bool _rwPreviousRaycastDisabled;

        public void ToggleUI(bool hidden)
        {
            if (hidden == _rwUiHidden) return;
            // Track lifecycle even if an optional game UI service throws.
            // That way camera exit can always attempt to restore the HUD.
            _rwUiHidden = hidden;
            try
            {
                var rendering = World.GetExistingSystemManaged<RenderingSystem>();
                var raycast = World.GetExistingSystemManaged<ToolRaycastSystem>();
                if (hidden)
                {
                    _rwPreviousOverlayHidden = rendering != null && rendering.hideOverlay;
                    _rwPreviousRaycastDisabled = raycast != null &&
                        (raycast.raycastFlags & RaycastFlags.FreeCameraDisable) != 0;
                    // Disable vanilla object highlighting and selection behind FPC.
                    if (rendering != null) rendering.hideOverlay = true;
                    if (raycast != null) raycast.raycastFlags |= RaycastFlags.FreeCameraDisable;
                    var tools = World.GetExistingSystemManaged<ToolSystem>();
                    var defaultTool = World.GetExistingSystemManaged<DefaultToolSystem>();
                    if (tools != null && defaultTool != null) tools.activeTool = defaultTool;
                }
                else
                {
                    if (rendering != null) rendering.hideOverlay = _rwPreviousOverlayHidden;
                    if (raycast != null)
                    {
                        if (_rwPreviousRaycastDisabled)
                            raycast.raycastFlags |= RaycastFlags.FreeCameraDisable;
                        else
                            raycast.raycastFlags &= ~RaycastFlags.FreeCameraDisable;
                    }
                }
            }
            catch (System.Exception e)
            {
                Mod.log.Warn("[RW-LITE] Clean camera overlay selection: " + e.Message);
            }
            try
            {
                // Original FPC used a fixed hashed CSS classname.  Locate the
                // app root by class prefix instead; never toggle with a fake key.
                // An inline data attribute records the prior visibility so a
                // player who had already hidden the UI stays hidden on exit.
                // Original upstream accessed the same chain; the catch below
                // handles a missing/initializing UI service without blocking FPC.
                // Avoid a build-time dependency on cohtml.Net.View. The existing
                // Colossal.UI UIView reference is sufficient for this UI entry point.
                // Resolve the nested Cohtml view only when toggling the HUD.
                var uiView = GameManager.instance.userInterface.view;
                var view = uiView?.GetType()
                    .GetProperty("View", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(uiView);
                var executeScript = view?.GetType().GetMethod("ExecuteScript", new[] { typeof(string) });
                if (view != null && executeScript != null)
                {
                    if (hidden)
                    {
                        executeScript.Invoke(view, new object[] { @"(function(){
                            var root=document.querySelector('[class^=""app-container_""], [class*="" app-container_""]');
                            if(!root || root.getAttribute('data-rw-fp-hidden')==='1')return;
                            root.setAttribute('data-rw-fp-old-visibility',root.style.visibility||'');
                            root.setAttribute('data-rw-fp-hidden','1');
                            root.style.visibility='hidden';
                        })();" });
                    }
                    else
                    {
                        executeScript.Invoke(view, new object[] { @"(function(){
                            var root=document.querySelector('[class^=""app-container_""], [class*="" app-container_""]');
                            if(!root || root.getAttribute('data-rw-fp-hidden')!=='1')return;
                            root.style.visibility=root.getAttribute('data-rw-fp-old-visibility')||'';
                            root.removeAttribute('data-rw-fp-old-visibility');
                            root.removeAttribute('data-rw-fp-hidden');
                        })();" });
                    }
                }
                else if (view != null)
                {
                    Mod.log.Warn("[RW-LITE] Cohtml ExecuteScript(string) unavailable; native overlay suppression remains active.");
                }
            }
            catch (System.Exception e)
            {
                // A UI class-name change must not disable the camera or walking.
                Mod.log.Warn("[RW-LITE] Vanilla UI visibility fallback: " + e.Message);
            }
        }
        protected override void OnDestroy()
        {
            if (_rwUiHidden) ToggleUI(false);
            if (Controller != null)
            {
                UnityEngine.Object.Destroy(Controller.gameObject);
                Controller = null;
            }
            base.OnDestroy();
        }
    }
}
