// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    /// <summary>
    /// torii: opciones que SOLO afectan el song select. el stable song select vive aca; el "strictly
    /// vertical UI" tambien esta espejado en Settings -> User Interface -> Song Select (mismo bindable).
    /// </summary>
    public partial class ToriiSongSelectSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Song Select";

        // el toggle stable (que ademas trae el footer legacy bundleado). cuando esta prendido, el footer
        // legacy va si o si, asi que el toggle de abajo queda forzado prendido y deshabilitado.
        // se asignan en load() con el bindable del config DIRECTO (no new + BindTo a un temporal): las
        // bindings de osu!framework son weak-ref y un bound-copy temporal sin guardar se lo come el GC,
        // y ahi el toggle dejaba de reaccionar (quedaba grisado aunque apagaras el stable).
        private Bindable<bool> stableSongSelect = null!;

        // la preferencia REAL del usuario para el footer legacy standalone (lo que se guarda en config).
        private Bindable<bool> footerPref = null!;

        // lo que ve el checkbox: refleja la pref cuando stable esta apagado, o queda checked+disabled
        // cuando stable esta prendido. el writeback a footerPref solo persiste clicks REALES del usuario.
        private readonly BindableBool footerDisplay = new BindableBool();

        // suppress flag (mismo patron que PotatoModeToggleAndRestart / ToriiAuraSettings): cuando
        // syncDisplay() escribe footerDisplay.Value programaticamente, el evento de cambio se dispara
        // SINCRONO. sin esta bandera, el writeback de abajo correria con Disabled todavia en false
        // (lo seteamos despues) y pisaria footerPref. con la bandera solo guardamos clicks del usuario.
        private bool syncingDisplay;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            stableSongSelect = config.GetBindable<bool>(OsuSetting.ToriiLegacyFooterUseSkin);
            footerPref = config.GetBindable<bool>(OsuSetting.ToriiLegacySongSelectFooter);

            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Legacy (stable-style) song select",
                    Current = config.GetBindable<bool>(OsuSetting.ToriiLegacyFooterUseSkin),
                    HintText = "Makes song select look like osu!stable: a skinnable legacy footer (back / mode / mods / random / options "
                               + "+ your rank panel) and the modern filter/sort bar and info wedges hidden. Turn off for the standard lazer UI.",
                    NewFeatureId = NewFeatureRegistry.LegacyFooterSkin,
                })
                {
                    Keywords = new[] { @"footer", @"skin", @"song", @"select", @"legacy", @"bottom", @"buttons", @"torii", @"stable" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Legacy footer",
                    Current = footerDisplay,
                    HintText = "Shows the stable-style song-select footer (back / mode / mods / random / options + your rank panel) "
                               + "over the normal lazer UI. It is always on and included when \"Legacy (stable-style) song select\" is enabled, so this can only be changed while that option is off.",
                    NewFeatureId = NewFeatureRegistry.LegacySongSelectFooter,
                })
                {
                    Keywords = new[] { @"footer", @"legacy", @"stable", @"bottom", @"buttons", @"song", @"select", @"torii" },
                },
                // mirror del toggle de Settings -> User Interface -> Song Select (mismo bindable, en sync).
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.UnslantedSongSelectUI,
                    HintText = UserInterfaceStrings.UnslantedSongSelectUIDescription,
                    Current = config.GetBindable<bool>(OsuSetting.UnslantedSongSelectUI),
                })
                {
                    Keywords = new[] { @"slant", @"unslant", @"vertical", @"straight", @"shear", @"song", @"select", @"torii" },
                },
            };

            stableSongSelect.BindValueChanged(_ => syncDisplay(), true);
            footerPref.BindValueChanged(_ => syncDisplay());

            // writeback: solo persistimos cuando el cambio vino de un click real del usuario
            // (no durante syncDisplay, y solo posible cuando el checkbox no esta disabled).
            footerDisplay.BindValueChanged(e =>
            {
                if (!syncingDisplay)
                    footerPref.Value = e.NewValue;
            });
        }

        private void syncDisplay()
        {
            syncingDisplay = true;

            bool stableOn = stableSongSelect.Value;

            // limpiamos Disabled antes de tocar Value asi nunca escribimos sobre un bindable bloqueado.
            footerDisplay.Disabled = false;
            footerDisplay.Value = stableOn || footerPref.Value;
            // con stable prendido el footer va si o si: checked y bloqueado.
            footerDisplay.Disabled = stableOn;

            syncingDisplay = false;
        }
    }
}
