// FPC Continued original work © original authors, GPL-2.0.
// Real Walking S1.3: minimal camera settings only; no PiP or FPC data-panel options.
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.Input;

namespace FirstPersonCameraContinued
{
    [FileLocation("RealWalkingStandaloneCamera")]
    [SettingsUIShowGroupName(CameraSettingsGroup, KeybindingSettingsGroup)]
    public class Setting : ModSetting
    {
        public const string CameraSettingsGroup = "CameraSettingsGroup";
        public const string KeybindingSettingsGroup = "KeybindingSettingsGroup";
        public const string GeneralSettingsTab = "GeneralSettingsTab";
        public const string FreeModeKeybindName = "FreeModeKeybind";

        public Setting(IMod mod) : base(mod) { SetDefaults(); }

        [SettingsUISlider(min = 10, max = 180, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        [SettingsUISection(GeneralSettingsTab, CameraSettingsGroup)]
        public int FOV { get; set; }

        [SettingsUISlider(min = .05f, max = 2f, step = .05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(GeneralSettingsTab, CameraSettingsGroup)]
        public float MovementSpeed { get; set; }

        [SettingsUISlider(min = .05f, max = 2f, step = .05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(GeneralSettingsTab, CameraSettingsGroup)]
        public float RunSpeed { get; set; }

        [SettingsUISlider(min = .05f, max = 5f, step = .05f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(GeneralSettingsTab, CameraSettingsGroup)]
        public float CimHeight { get; set; }

        [SettingsUISlider(min = .8f, max = 3f, step = .1f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(GeneralSettingsTab, CameraSettingsGroup)]
        public float TransitionSpeedFactor { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.F, Mod.kButtonActionName, alt: true)]
        [SettingsUISection(GeneralSettingsTab, KeybindingSettingsGroup)]
        public ProxyBinding FreeModeKeybind { get; set; }

        [SettingsUIMultilineText]
        [SettingsUISection(GeneralSettingsTab, KeybindingSettingsGroup)]
        public string Shortcuts => "Alt+F camera | WASD move | Shift sprint | Mouse look | R/F height | E board or leave | Escape exit";

        // Legacy serialized setting retained for compatibility with existing user profiles.
        // Real Walking now manages the game HUD directly; this option is not read.
        [SettingsUIHidden]
        public bool ShowGameUI { get; set; }

        public override void SetDefaults()
        {
            FOV = 70;
            MovementSpeed = .1f;
            RunSpeed = .35f;
            CimHeight = 1.7f;
            TransitionSpeedFactor = 1.5f;
            ShowGameUI = true;
        }
    }
}
