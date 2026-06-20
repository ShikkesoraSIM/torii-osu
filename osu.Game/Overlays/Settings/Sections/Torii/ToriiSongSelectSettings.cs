// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
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

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
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
        }
    }
}
