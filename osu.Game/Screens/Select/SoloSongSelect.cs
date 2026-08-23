// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Play;
using osu.Game.Users;
using osu.Game.Utils;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Screens.Select
{
    public partial class SoloSongSelect : SongSelect
    {
        protected override UserActivity InitialActivity => new UserActivity.ChoosingBeatmap();

        // Torii: the legacy stable-style footer (back button + mode/mods/random/options
        // + user rank/pp banner) takes over here when a legacy skin is active.
        public override bool AllowLegacyFooterSkinning => true;

        private PlayerLoader? playerLoader;
        private IReadOnlyList<Mod>? modsAtGameplayStart;

        [Resolved]
        private BeatmapSetOverlay? beatmapOverlay { get; set; }

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved]
        private OsuGame? game { get; set; }

        [Resolved(canBeNull: true)]
        private Mapperatorinator.MapperatorinatorGenerationManager? generationManager { get; set; }

        private Sample? sampleConfirmSelection { get; set; }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            sampleConfirmSelection = audio.Samples.Get(@"SongSelect/confirm-selection");

            AddInternal(new SongSelectTouchInputDetector());
        }

        /// <summary>
        /// torii: abre la pantalla de mapperatorinator para el audio de este beatmap.
        /// Publico porque los paneles del carousel tambien lo ofrecen y no pueden pushear ellos.
        /// </summary>
        public void OpenMapperatorinator(BeatmapInfo beatmap) => this.Push(new Mapperatorinator.MapperatorinatorScreen(beatmap));

        /// <summary>
        /// torii: las entradas de menu de mapperatorinator para un beatmap. Para mapas
        /// que ya salieron de la IA (tag marcador) ofrece regenerar con los mismos
        /// settings, sumar una diff al set, o arrancar un set nuevo; para el resto,
        /// la entrada simple de siempre. Compartido con los paneles del carousel.
        /// </summary>
        public IEnumerable<OsuMenuItem> CreateMapperatorinatorMenuItems(BeatmapInfo beatmap)
        {
            // un celular no puede correr el modelo (python + varios gb); la opcion ni
            // aparece en vez de prometer algo que termina en error.
            if (RuntimeInfo.IsMobile)
                yield break;

            if (!Mapperatorinator.MapperatorinatorGenerationManager.IsGeneratedMap(beatmap))
            {
                yield return new OsuMenuItem("Use this song for Mapperatorinator", MenuItemType.Highlighted,
                    () => OpenMapperatorinator(beatmap)) { Icon = FontAwesome.Solid.Magic };

                yield break;
            }

            yield return new OsuMenuItem("Mapperatorinator", MenuItemType.Highlighted)
            {
                Icon = FontAwesome.Solid.Magic,
                Items = new MenuItem[]
                {
                    new OsuMenuItem("Regenerate (same settings, new seed)", MenuItemType.Standard,
                        () => generationManager?.QuickRegenerate(beatmap)),
                    // ver con que se hizo (y poder guardarlo como preset) sin tener que
                    // adivinar: el mapa se lleva sus settings adentro del set.
                    new OsuMenuItem("See the settings this map used", MenuItemType.Standard,
                        () => this.Push(new Mapperatorinator.MapperatorinatorScreen(beatmap, addToExistingSet: true, reviewOnly: true))),
                    new OsuMenuItem("New difficulty (tweak settings)", MenuItemType.Standard,
                        () => this.Push(new Mapperatorinator.MapperatorinatorScreen(beatmap, addToExistingSet: true))),
                    new OsuMenuItem("Generate as a new set", MenuItemType.Standard,
                        () => OpenMapperatorinator(beatmap)),
                },
            };
        }

        public override IEnumerable<OsuMenuItem> GetForwardActions(BeatmapInfo beatmap)
        {
            yield return new OsuMenuItem(ButtonSystemStrings.Play.ToSentence(), MenuItemType.Highlighted, () => SelectAndRun(beatmap, OnStart)) { Icon = FontAwesome.Solid.Check };
            yield return new OsuMenuItem(ButtonSystemStrings.Edit.ToSentence(), MenuItemType.Standard, () => Edit(beatmap)) { Icon = FontAwesome.Solid.PencilAlt };

            yield return new OsuMenuItemSpacer();

            if (beatmap.OnlineID > 0)
            {
                yield return new OsuMenuItem(CommonStrings.Details, MenuItemType.Standard, () => beatmapOverlay?.FetchAndShowBeatmap(beatmap.OnlineID));

                if (beatmap.GetOnlineURL(api, Ruleset.Value) is string url)
                    yield return new OsuMenuItem(CommonStrings.CopyLink, MenuItemType.Standard, () => game?.CopyToClipboard(url));

                // torii: link al osu! oficial tambien desde la dificultad individual
                // (ya estaba en la tarjeta del set, faltaba aca).
                if (beatmap.GetOfficialOnlineURL(Ruleset.Value) is string osuUrl)
                    yield return new OsuMenuItem("Copy osu! link", MenuItemType.Standard, () => game?.CopyToClipboard(osuUrl));

                yield return new OsuMenuItemSpacer();
            }

            // torii: entradas de mapperatorinator. un mapa ya generado con la IA abre
            // un submenu (regenerar / nueva diff); el resto, la entrada simple.
            foreach (var item in CreateMapperatorinatorMenuItems(beatmap))
                yield return item;

            yield return new OsuMenuItemSpacer();

            foreach (var i in CreateCollectionMenuActions(beatmap))
                yield return i;

            if (beatmap.LastPlayed == null)
                yield return new OsuMenuItem(SongSelectStrings.MarkAsPlayed, MenuItemType.Standard, () => beatmaps.MarkPlayed(beatmap)) { Icon = FontAwesome.Solid.TimesCircle };
            else
                yield return new OsuMenuItem(SongSelectStrings.RemoveFromPlayed, MenuItemType.Standard, () => beatmaps.MarkNotPlayed(beatmap)) { Icon = FontAwesome.Solid.TimesCircle };

            yield return new OsuMenuItem(SongSelectStrings.ClearAllLocalScores, MenuItemType.Standard, () => dialogOverlay?.Push(new BeatmapClearScoresDialog(beatmap)))
            {
                Icon = FontAwesome.Solid.Eraser
            };

            if (beatmaps.CanHide(beatmap))
                yield return new OsuMenuItem(WebCommonStrings.ButtonsHide.ToSentence(), MenuItemType.Destructive, () => beatmaps.Hide(beatmap));

            // torii: borrar una sola dificultad. estaba solo en el editor (File > delete
            // difficulty) y nadie lo encontraba. la ultima que queda no se puede borrar:
            // un set sin dificultades no existe, para eso esta borrar el mapa entero.
            if (beatmap.BeatmapSet?.Beatmaps.Count > 1)
            {
                yield return new OsuMenuItem("Delete difficulty", MenuItemType.Destructive,
                    () => dialogOverlay?.Push(new BeatmapDifficultyDeleteDialog(beatmap))) { Icon = FontAwesome.Solid.Trash };
            }
        }

        protected override void OnStart()
        {
            if (playerLoader != null) return;

            modsAtGameplayStart = Mods.Value.Select(m => m.DeepClone()).ToArray();

            // Ctrl+Enter should start map with autoplay enabled.
            if (GetContainingInputManager()?.CurrentState?.Keyboard.ControlPressed == true)
            {
                var autoInstance = getAutoplayMod();

                if (autoInstance == null)
                {
                    notifications?.Post(new SimpleNotification
                    {
                        Text = NotificationsStrings.NoAutoplayMod
                    });
                    return;
                }

                var mods = Mods.Value.Append(autoInstance).ToArray();

                if (!ModUtils.CheckCompatibleSet(mods, out var invalid))
                    mods = mods.Except(invalid).Append(autoInstance).ToArray();

                Mods.Value = mods;
            }

            sampleConfirmSelection?.Play();

            this.Push(playerLoader = new PlayerLoader(createPlayer));

            Player createPlayer()
            {
                Player player;

                var replayGeneratingMod = Mods.Value.OfType<ICreateReplayData>().FirstOrDefault();

                if (replayGeneratingMod != null)
                {
                    player = new ReplayPlayer(replayGeneratingMod.CreateScoreFromReplayData);
                }
                else
                {
                    player = new SoloPlayer();
                }

                return player;
            }
        }

        public void Edit(BeatmapInfo beatmap)
        {
            if (!this.IsCurrentScreen())
                return;

            SelectAndRun(beatmap, () => this.Push(new EditorLoader()));
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
            if (playerLoader == null) return;

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
