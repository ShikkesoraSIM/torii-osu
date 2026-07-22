// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.Rooms;
using osu.Game.Overlays;
using osu.Game.Screens.OnlinePlay.Match;
using osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay.Card;
using osu.Game.Screens.OnlinePlay.Multiplayer.Match;
using osu.Game.Screens.Select;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    public partial class GameplayWarmupScreen : RankedPlaySubScreen
    {
        public override bool ShowBeatmapBackground => true;

        public override LocalisableString StageHeading => "Gameplay";

        [Cached(typeof(IBindable<SongSelect.BeatmapSetLookupResult?>))]
        private readonly Bindable<SongSelect.BeatmapSetLookupResult?> lastLookupResult = new Bindable<SongSelect.BeatmapSetLookupResult?>();

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        private RankedPlayMatchInfo matchInfo { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider overlayColours { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private MusicController musicController { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> globalBeatmap { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> globalRuleset { get; set; } = null!;

        [Resolved]
        private Bindable<IReadOnlyList<Mod>> globalMods { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private IOverlayManager? overlayManager { get; set; }

        private Container<RankedPlayCard> cardColumn = null!;
        private Drawable separator = null!;
        private Drawable detailsColumn = null!;
        private Drawable wedgesContainer = null!;

        // torii: toggle de HD (en vez del mod-select completo) + aviso de que HD no da score extra.
        private Components.RankedPlayHiddenToggle hdToggle = null!;
        private OsuSpriteText hdWarning = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            APIBeatmap beatmap = beatmapLookupCache.GetBeatmapAsync(Client.Room!.CurrentPlaylistItem.BeatmapID).GetResultSafely()!;
            lastLookupResult.Value = SongSelect.BeatmapSetLookupResult.Completed(beatmap.BeatmapSet);

            var matchState = Client.Room?.MatchState as RankedPlayRoomState;
            Debug.Assert(matchState != null);

            Children =
            [
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Direction = FillDirection.Horizontal,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Width = 0.5f,
                    Spacing = new Vector2(20),
                    LayoutDuration = 500,
                    LayoutEasing = Easing.OutPow10,
                    Children = new[]
                    {
                        cardColumn = new Container<RankedPlayCard>
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            AutoSizeAxes = Axes.Both,
                        },
                        separator = new Box
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(2, 50),
                            Scale = new Vector2(1, 0),
                            Alpha = 0,
                            Colour = overlayColours.Colour0
                        },
                        detailsColumn = new Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Masking = true,
                            Scale = new Vector2(0.8f),
                            Alpha = 0,
                            Child = wedgesContainer = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Shear = OsuGame.SHEAR,
                                X = -20,
                                Padding = new MarginPadding
                                {
                                    Left = -SongSelect.CORNER_RADIUS_HIDE_OFFSET,
                                },
                                Child = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Spacing = new Vector2(0f, 4f),
                                    Direction = FillDirection.Vertical,
                                    Children =
                                    [
                                        new ShearAligningWrapper(new TitleWedge(beatmap))
                                        {
                                            Shear = -OsuGame.SHEAR,
                                        },
                                        new ShearAligningWrapper(new MetadataWedge(beatmap))
                                        {
                                            Shear = -OsuGame.SHEAR,
                                        },
                                    ]
                                }
                            }
                        }
                    }
                },
                // torii: toggle de HD (playstyle) + aviso amarillo de que HD no da score extra.
                new FillFlowContainer
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Margin = new MarginPadding { Bottom = 30 },
                    Children = new Drawable[]
                    {
                        hdWarning = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "HD does not give extra Score in ranked play",
                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                            Colour = new Color4(1f, 0.84f, 0.22f, 1f),
                            Alpha = 0,
                        },
                        hdToggle = new Components.RankedPlayHiddenToggle
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            OnToggle = toggleHidden,
                        },
                    }
                }
            ];
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            MultiplayerPlaylistItem item = Client.Room!.CurrentPlaylistItem;

            RulesetInfo ruleset = rulesets.GetRuleset(item.RulesetID)!;
            BeatmapInfo? localBeatmap = beatmapManager.QueryOnlineBeatmapId(item.BeatmapID);

            globalBeatmap.Value = beatmapManager.GetWorkingBeatmap(localBeatmap);
            globalRuleset.Value = ruleset;
            updateGlobalMods();

            // torii: recomponer los global mods + sincronizar el toggle cada vez que cambian los
            // mods del jugador (el toggle dispara ChangeUserMods, el server los eco-broadcastea, y
            // ahi actualizamos lo que se juega/submitea + el estado visual del toggle).
            Client.UserModsChanged += onUserModsChanged;
            syncHiddenState();

            // Play the new track from its preview point.
            globalBeatmap.Value.PrepareTrackForPreview(false);
            musicController.Play(true);

            Client.ChangeState(MultiplayerUserState.Ready).FireAndForget();
        }

        // torii FREEMODS: global Mods = free-mods elegidos por el jugador + required mods de la carta.
        // el player/submission leen el global Mods bindable, asi los mods llegan a gameplay + score.
        private void updateGlobalMods()
        {
            MultiplayerPlaylistItem? item = Client.Room?.CurrentPlaylistItem;
            if (item == null)
                return;

            Ruleset rulesetInstance = rulesets.GetRuleset(item.RulesetID)!.CreateInstance();
            IEnumerable<APIMod> userMods = Client.LocalUser?.Mods ?? Enumerable.Empty<APIMod>();

            globalMods.Value = userMods.Concat(item.RequiredMods)
                                       .Select(m => m.ToMod(rulesetInstance)).ToArray();
        }

        private void onUserModsChanged(MultiplayerRoomUser user)
        {
            if (user.UserID == Client.LocalUser?.UserID)
            {
                updateGlobalMods();
                syncHiddenState();
            }
        }

        // torii: el click del toggle pide prender/apagar HD. Mandamos ChangeUserMods; el estado
        // visual del toggle lo sincroniza syncHiddenState cuando el server eco-broadcastea.
        private void toggleHidden(bool wantHidden)
        {
            Ruleset? ruleset = globalRuleset.Value?.CreateInstance();
            if (ruleset == null)
                return;

            if (wantHidden)
            {
                Mod? hd = ruleset.CreateModFromAcronym(@"HD");
                if (hd != null)
                    Client.ChangeUserMods(new[] { hd }).FireAndForget();
            }
            else
                Client.ChangeUserMods(Array.Empty<Mod>()).FireAndForget();
        }

        // torii: refleja el estado real de los mods del jugador (server-authoritative) en el toggle
        // + el aviso amarillo. HD = tiene el mod HD prendido.
        private void syncHiddenState()
        {
            bool hasHidden = Client.LocalUser?.Mods.Any(m => m.Acronym == @"HD") ?? false;
            hdToggle.SetActive(hasHidden);
            hdWarning.FadeTo(hasHidden ? 1 : 0, 150, Easing.OutQuint);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (Client.IsNotNull())
                Client.UserModsChanged -= onUserModsChanged;
        }

        public override void OnEntering(RankedPlaySubScreen? previous)
        {
            base.OnEntering(previous);

            if (matchInfo.LastPlayedCard == null)
                return;

            RankedPlayCard? card = null;

            switch (previous)
            {
                case PickScreen pick:
                {
                    if (pick.CenterRow.RemoveCard(matchInfo.LastPlayedCard, out card, out var screenSpaceDrawQuad))
                        card.MatchScreenSpaceDrawQuad(screenSpaceDrawQuad, cardColumn);
                    break;
                }

                case OpponentPickScreen opponentPick:
                {
                    if (opponentPick.CenterRow.RemoveCard(matchInfo.LastPlayedCard, out card, out var screenSpaceDrawQuad))
                        card.MatchScreenSpaceDrawQuad(screenSpaceDrawQuad, cardColumn);
                    break;
                }
            }

            if (card == null)
            {
                Logger.Log($"Played card {matchInfo.LastPlayedCard.Card.ID} was not on the screen.", level: LogLevel.Error);

                card = new RankedPlayCard(matchInfo.LastPlayedCard)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
            }

            cardColumn.Add(card);

            separator.AlwaysPresent = true;
            detailsColumn.AlwaysPresent = true;

            using (BeginDelayedSequence(500))
            {
                separator.FadeIn();
                separator.ScaleTo(Vector2.One, 1000, Easing.OutPow10);

                using (BeginDelayedSequence(200))
                {
                    detailsColumn.FadeIn(800, Easing.OutPow10);
                    wedgesContainer.MoveToX(0, 1000, Easing.OutPow10);
                }
            }
        }
    }
}
