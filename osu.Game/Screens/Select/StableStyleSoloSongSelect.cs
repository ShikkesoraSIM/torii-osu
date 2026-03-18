// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Play;
using osu.Game.Users;
using osu.Game.Utils;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// "Stable-style" solo song select built on top of the legacy select stack.
    /// This is intentionally isolated behind a config toggle to avoid affecting the default flow.
    /// </summary>
    public partial class StableStyleSoloSongSelect : SongSelect
    {
        protected override UserActivity InitialActivity => new UserActivity.ChoosingBeatmap();

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        private Sample? sampleConfirmSelection { get; set; }

        private PlayerLoader? playerLoader;
        private IReadOnlyList<Mod>? modsAtGameplayStart;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            sampleConfirmSelection = audio.Samples.Get(@"SongSelect/confirm-selection");
            AddInternal(new SongSelectTouchInputDetector());
        }

        protected override BeatmapDetailArea CreateBeatmapDetailArea() => new PlayBeatmapDetailArea();

        public override MenuItem[] CreateForwardNavigationMenuItemsForBeatmap(Func<BeatmapInfo> getBeatmap) => new MenuItem[]
        {
            new OsuMenuItem(ButtonSystemStrings.Play.ToSentence(), MenuItemType.Highlighted, () => FinaliseSelection(getBeatmap())) { Icon = FontAwesome.Solid.Check },
            new OsuMenuItem(ButtonSystemStrings.Edit.ToSentence(), MenuItemType.Standard, () => Edit(getBeatmap())) { Icon = FontAwesome.Solid.PencilAlt },
        };

        protected override bool OnStart()
        {
            if (playerLoader != null)
                return false;

            modsAtGameplayStart = Mods.Value.Select(m => m.DeepClone()).ToArray();

            // Ctrl+Enter should start map with autoplay enabled.
            if (GetContainingInputManager()?.CurrentState?.Keyboard.ControlPressed == true)
            {
                var autoInstance = getAutoplayMod();

                if (autoInstance == null)
                {
                    notifications?.Post(new SimpleNotification
                    {
                        Text = NotificationsStrings.NoAutoplayMod,
                    });
                    return false;
                }

                var mods = Mods.Value.Append(autoInstance).ToArray();

                if (!ModUtils.CheckCompatibleSet(mods, out var invalid))
                    mods = mods.Except(invalid).Append(autoInstance).ToArray();

                Mods.Value = mods;
            }

            sampleConfirmSelection?.Play();

            this.Push(playerLoader = new PlayerLoader(createPlayer));
            return true;

            Player createPlayer()
            {
                var replayGeneratingMod = Mods.Value.OfType<ICreateReplayData>().FirstOrDefault();

                if (replayGeneratingMod != null)
                    return new ReplayPlayer(replayGeneratingMod.CreateScoreFromReplayData);

                return new SoloPlayer();
            }
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            revertMods();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            if (base.OnExiting(e))
                return true;

            revertMods();
            return false;
        }

        private ModAutoplay? getAutoplayMod() => Ruleset.Value.CreateInstance().GetAutoplayMod();

        private void revertMods()
        {
            if (playerLoader == null)
                return;

            Mods.Value = modsAtGameplayStart;
            playerLoader = null;
        }

        private partial class PlayerLoader : Play.PlayerLoader
        {
            public override bool ShowFooter => !QuickRestart;

            public PlayerLoader(Func<Player> createPlayer)
                : base(createPlayer)
            {
            }
        }
    }
}
