using Game.Modding;
using Game;
using HarmonyLib;
using Colossal.Logging;
using Colossal.IO.AssetDatabase;
using Game.SceneFlow;
using Game.Settings;
using static Cinemachine.CinemachineTriggerAction;
using Game.Input;
using FirstPersonCameraContinued.Systems;
using Game.Rendering;
using Unity.Entities;

namespace FirstPersonCameraContinued
{
    // Standalone integration (2026-10-29): this is a camera module, not a second IMod entry.
    public class Mod
    {
        private Harmony? _harmony;

        public static Setting? FirstPersonModSettings;

        public static ILog log = LogManager.GetLogger($"{nameof(FirstPersonCameraContinued)}.{nameof(Mod)}").SetShowsErrorsInUI(true);

        public const string kButtonActionName = "EnterFreeModeBinding";

        public void OnLoad(UpdateSystem updateSystem, IMod owner)
        {
            _harmony = new("RealWalkingStandalone.EmbeddedFPC");
            _harmony.PatchAll(typeof(Mod).Assembly);

            FirstPersonModSettings = new Setting(owner);
            FirstPersonModSettings.RegisterInOptionsUI();

            Localization.LoadTranslations(FirstPersonModSettings, log);

            FirstPersonModSettings.RegisterKeyBindings();

            AssetDatabase.global.LoadSettings("RealWalkingStandaloneCamera", FirstPersonModSettings, new Setting(owner));

        }

        public void OnDispose()
        {
            _harmony?.UnpatchAll("RealWalkingStandalone.EmbeddedFPC");
        }
    }
}
