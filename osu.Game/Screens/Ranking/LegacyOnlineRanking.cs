// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// el RankingDialog de osu!stable 1:1 (la pagina "below the fold" del ranking): el fondo es
// el 3-slice ranking-dialog-left/middle/right (las bandas LOCAL/ONLINE/BEATMAP con sus
// watermarks son ARTE, no cajas dibujadas), y encima van — con las coords del RankingDialog.cs
// de stable x1.6 — el texto de local ranking (y=20, gold), el panel de usuario (leftCentre-100,
// 60), los checkboxes de save-replay/favourite (WidthScaled-160, 10/35), la tabla compacta
// (LEFT_PADDING=3+(W-640)/2, headers y=170 font 9, filas y=180 font 11, cell-boxes de 24
// unidades alternando (40,40,40)/(20,20,20), verde/rojo en los deltas), el texto del beatmap
// en (170, 301) y el logo del modo aditivo en TopRight (100, 230) al 10%. columnas del
// cuttingedge actual: Overall/Accuracy/Max Combo/Ranked Score/Total Score/Performance.
// el rate-beatmap de stable queda afuera a proposito. solo para plays propias.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Skinning;
using osu.Game.Skinning.Select;
using osu.Game.Users;
using osuTK;
using osuTK.Graphics;
using Realms;

namespace osu.Game.Screens.Ranking
{
    public partial class LegacyOnlineRanking : CompositeDrawable
    {
        /// <summary>
        /// la altura del dialog de stable: las texturas ranking-dialog-* miden 709px (443
        /// unidades x1.6). la pagina es fija, como el BaseOffset de una pantalla de stable.
        /// </summary>
        public const float DIALOG_HEIGHT = 709;

        // paleta de stable (RankingDialog.cs).
        private static readonly Color4 gold = new Color4(255, 203, 33, 255);
        private static readonly Color4 colour_increase = new Color4(103, 157, 17, 255);
        private static readonly Color4 delta_green = new Color4(150, 207, 70, 255);
        private static readonly Color4 delta_red = new Color4(220, 80, 60, 255);
        private static readonly Color4 label_orange = new Color4(255, 165, 0, 255);
        private static readonly Color4 row_dark = new Color4(40, 40, 40, 255);
        private static readonly Color4 row_darker = new Color4(20, 20, 20, 255);

        // tabla (RankingDialog.cs x1.6): LEFT_PADDING = (3 + (853.75-640)/2) * 1.6.
        private const float table_x = 176;
        private const float label_width = 160;
        private static readonly float[] column_widths = { 112, 112, 112, 176, 176, 160 };
        private const float header_y = 272;
        private const float row1_y = 288;
        private const float row2_y = 327;
        private const float cell_height = 38;

        private static readonly string[] column_headers = { "Overall", "Accuracy", "Max Combo", "Ranked Score", "Total Score", "Performance" };

        private readonly ScoreInfo score;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [Resolved]
        private ScoreManager scoreManager { get; set; } = null!;

        [Resolved]
        private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

        [Resolved]
        private LocalUserStatisticsProvider? statisticsProvider { get; set; }

        [Resolved]
        private UserStatisticsWatcher? statisticsWatcher { get; set; }

        private readonly IBindable<ScoreBasedUserStatisticsUpdate?> latestUpdate = new Bindable<ScoreBasedUserStatisticsUpdate?>();

        private OsuSpriteText localRankText = null!;

        // celdas de la tabla: [columna] para cada fila, con su linea "sub" abajo.
        private readonly OsuSpriteText[] beatmapMain = new OsuSpriteText[6];
        private readonly OsuSpriteText[] beatmapSub = new OsuSpriteText[6];
        private readonly OsuSpriteText[] overallMain = new OsuSpriteText[6];
        private readonly OsuSpriteText[] overallSub = new OsuSpriteText[6];

        // el highlight verde del total score cuando el submit sumo puntos (como stable).
        private Box totalScoreHighlight = null!;

        private OsuSpriteText beatmapInfoLine = null!;

        private CancellationTokenSource? cancellation;

        public LegacyOnlineRanking(ScoreInfo score)
        {
            this.score = score;
        }

        [BackgroundDependencyLoader]
        private void load(ISkinSource skin)
        {
            RelativeSizeAxes = Axes.X;

            // solo para plays del usuario local: el dialog habla de TU perfil.
            if (api.LocalUser.Value == null || score.UserID != api.LocalUser.Value.OnlineID)
            {
                Height = 0;
                return;
            }

            Height = DIALOG_HEIGHT;

            Texture? tex(string name) => skin.GetTexture(name)
                                         ?? skins.DefaultClassicSkin.GetTexture(name);

            var children = new System.Collections.Generic.List<Drawable>();

            // fondo: el 3-slice del dialog de stable, blanco al 86% (Color(255,255,255,220)).
            var bgLeft = tex(@"ranking-dialog-left");
            var bgMiddle = tex(@"ranking-dialog-middle");
            var bgRight = tex(@"ranking-dialog-right");

            children.Add(new Sprite
            {
                Texture = bgLeft,
                Alpha = 220 / 255f,
            });
            children.Add(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = bgLeft?.DisplayWidth ?? 270, Right = bgRight?.DisplayWidth ?? 280 },
                Child = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Texture = bgMiddle,
                    Alpha = 220 / 255f,
                },
            });
            children.Add(new Sprite
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Texture = bgRight,
                Alpha = 220 / 255f,
            });

            // "You achieved the #N score on local rankings!" — sobre la franja dorada
            // (stable: leftCentre, y=20, font 22, gold, origen centro).
            children.Add(localRankText = new OsuSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.Centre,
                Y = 32,
                Font = LegacyFonts.Get(35),
                Colour = gold,
                Shadow = true,
                Alpha = 0,
            });

            // el panel de usuario de stable, reusado del footer del song select
            // (stable: GameBase.User.DrawAt(leftCentre - 100, 60)).
            children.Add(new LegacyFooterUser
            {
                Position = new Vector2(523, 96),
            });

            // checkboxes de la esquina superior derecha (stable: WidthScaled-160, y 10 / 35).
            children.Add(new LegacyCheckbox(@"Save replay to Replays folder", onExportReplay)
            {
                Anchor = Anchor.TopRight,
                X = -256,
                Y = 16,
            });

            if (score.BeatmapInfo?.BeatmapSet != null && score.BeatmapInfo.BeatmapSet.OnlineID > 0)
            {
                children.Add(new LegacyCheckbox(@"Add as online favourite", onToggleFavourite)
                {
                    Anchor = Anchor.TopRight,
                    X = -256,
                    Y = 56,
                });
            }

            // logo del modo, aditivo y sutil (stable: TopRight origen centro (100, 230),
            // scale 0.8, fade a 0.1, rotacion leve).
            var modeTex = tex($@"mode-{score.Ruleset.ShortName}");

            if (modeTex != null)
            {
                var logo = new Sprite
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    Position = new Vector2(-160, 368),
                    Texture = modeTex,
                    Scale = new Vector2(0.8f),
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                };
                children.Add(logo);
                logo.OnLoadComplete += d =>
                {
                    d.FadeTo(0.1f, 2000);
                    d.RotateTo(0.1f * 57.3f, 5000, Easing.Out);
                };
            }

            buildTable(children);

            // texto del beatmap sobre la franja BEATMAP (stable: (170, 301), dos lineas).
            var meta = score.BeatmapInfo?.Metadata;

            children.Add(new OsuSpriteText
            {
                Position = new Vector2(272, 482),
                Font = LegacyFonts.Get(24),
                Shadow = true,
                Text = $"{meta?.Artist} - {meta?.Title} [{score.BeatmapInfo?.DifficultyName}] by {meta?.Author.Username}",
            });
            children.Add(beatmapInfoLine = new OsuSpriteText
            {
                Position = new Vector2(272, 512),
                Font = LegacyFonts.Get(19),
                Colour = Color4.White.Opacity(0.55f),
                Shadow = true,
                Text = string.Empty,
            });

            InternalChildren = children.ToArray();

            fillOverallRow(statisticsProvider?.GetStatisticsFor(score.Ruleset), null);

            fetchLocalRank();
            fetchBestOnBeatmap();
            fetchBeatmapInfo();
            computeAchievedPp();

            // deltas post-submit (el watcher lo registra SubmittingPlayer): cuando llega el
            // update de ESTE score, refrescamos la fila Overall con before/after.
            if (statisticsWatcher != null)
            {
                latestUpdate.BindTo(statisticsWatcher.LatestUpdate);
                latestUpdate.BindValueChanged(update =>
                {
                    var u = update.NewValue;

                    if (u != null && u.Score.MatchesOnlineID(score))
                        Schedule(() => fillOverallRow(u.After, u));
                }, true);
            }
        }

        #region tabla

        private static float columnX(int column)
        {
            float x = label_width;

            for (int i = 0; i < column; i++)
                x += column_widths[i];

            return x;
        }

        private void buildTable(System.Collections.Generic.List<Drawable> children)
        {
            var table = new System.Collections.Generic.List<Drawable>();

            // headers (stable: font 9 bold, centrados por columna, sin fondo, y=170).
            for (int i = 0; i < column_headers.Length; i++)
            {
                table.Add(new OsuSpriteText
                {
                    Origin = Anchor.TopCentre,
                    Position = new Vector2(columnX(i) + column_widths[i] / 2, header_y - row1_y),
                    Font = LegacyFonts.Get(14.4f, Graphics.FontWeight.Bold),
                    Alpha = 0.9f,
                    Shadow = true,
                    Text = column_headers[i],
                });
            }

            // cell-boxes: cada celda su caja oscura con 2px de aire, alternando el tono por
            // fila como stable (row1 (40,40,40), row2 (20,20,20)).
            for (int row = 0; row < 2; row++)
            {
                float y = row == 0 ? 0 : row2_y - row1_y;
                var colour = row == 0 ? row_dark : row_darker;

                table.Add(new Box
                {
                    Position = new Vector2(0, y),
                    Size = new Vector2(label_width - 2, cell_height - 2),
                    Colour = colour,
                    Alpha = 0.9f,
                });

                for (int i = 0; i < 6; i++)
                {
                    table.Add(new Box
                    {
                        Position = new Vector2(columnX(i), y),
                        Size = new Vector2(column_widths[i] - 2, cell_height - 2),
                        Colour = colour,
                        Alpha = 0.9f,
                    });
                }
            }

            // highlight verde del total score (colourIncrease de stable) — tapa la caja gris.
            table.Add(totalScoreHighlight = new Box
            {
                Position = new Vector2(columnX(4), row2_y - row1_y),
                Size = new Vector2(column_widths[4] - 2, cell_height - 2),
                Colour = colour_increase,
                Alpha = 0,
            });

            // labels naranjas bold (stable: chartName, font 11 bold, Color.Orange).
            table.Add(new OsuSpriteText
            {
                Origin = Anchor.TopCentre,
                Position = new Vector2(label_width / 2, 4),
                Font = LegacyFonts.Get(18, Graphics.FontWeight.Bold),
                Colour = label_orange,
                Shadow = true,
                Text = @"Beatmap Ranking",
            });
            table.Add(new OsuSpriteText
            {
                Origin = Anchor.TopCentre,
                Position = new Vector2(label_width / 2, row2_y - row1_y + 4),
                Font = LegacyFonts.Get(18, Graphics.FontWeight.Bold),
                Colour = label_orange,
                Shadow = true,
                Text = @"Overall Ranking",
            });

            for (int i = 0; i < 6; i++)
            {
                table.Add(beatmapMain[i] = cell(i, 3, 18, Color4.White));
                table.Add(beatmapSub[i] = cell(i, 23, 12.8f, Color4.White.Opacity(0.55f)));
                table.Add(overallMain[i] = cell(i, row2_y - row1_y + 3, 18, Color4.White));
                table.Add(overallSub[i] = cell(i, row2_y - row1_y + 23, 12.8f, Color4.White.Opacity(0.55f)));

                beatmapSub[i].Text = @"-";
                overallSub[i].Text = @"-";
            }

            // fila Beatmap Ranking: los valores de ESTA play (el rank del mapa de esta play no
            // lo sabemos client-side: "-"; el score de la play va en Ranked Score, como stable).
            beatmapMain[0].Text = @"-";
            beatmapMain[1].Text = $"{score.Accuracy:0.00%}";
            beatmapMain[2].Text = $"{score.MaxCombo:N0}x";
            beatmapMain[3].Text = $"{score.TotalScore:N0}";
            beatmapMain[4].Text = @"-";
            beatmapMain[5].Text = score.PP != null ? $"{score.PP:N0}pp" : @"-";

            children.Add(new Container
            {
                Position = new Vector2(table_x, row1_y),
                AutoSizeAxes = Axes.Both,
                Children = table,
            });
        }

        private static OsuSpriteText cell(int column, float y, float size, Color4 colour) => new OsuSpriteText
        {
            Origin = Anchor.TopCentre,
            Position = new Vector2(columnX(column) + column_widths[column] / 2, y),
            Font = LegacyFonts.Get(size),
            Colour = colour,
            Shadow = true,
        };

        #endregion

        #region data

        private void fetchLocalRank()
        {
            var beatmapId = score.BeatmapInfo?.ID;

            if (beatmapId == null)
                return;

            // posicion de este score entre los locales del mapa (mismo ruleset, por total score),
            // como el ScoreManager.InsertScore de stable.
            int rank = realm.Run(r =>
            {
                var localScores = r.All<ScoreInfo>()
                                   .Filter(@"BeatmapInfo.ID == $0 AND DeletePending == false", beatmapId.Value)
                                   .ToList()
                                   .Where(s => s.Ruleset.ShortName == score.Ruleset.ShortName)
                                   .OrderByDescending(s => s.TotalScore)
                                   .ToList();

                return localScores.FindIndex(s => s.ID == score.ID) + 1;
            });

            if (rank >= 1)
            {
                localRankText.Text = $"You achieved the #{rank} score on local rankings!";
                localRankText.FadeIn(400);
            }
        }

        private void fetchBestOnBeatmap()
        {
            if (score.BeatmapInfo == null || score.BeatmapInfo.OnlineID <= 0 || !api.IsLoggedIn)
                return;

            var req = new GetScoresRequest(score.BeatmapInfo, score.Ruleset);

            req.Success += response => Schedule(() =>
            {
                var best = response.UserScore;

                if (best?.Score == null)
                    return;

                // el rank del mapa de la play sale del best: si esta play ES el best, el
                // position aplica a la fila principal tambien.
                if (best.Position != null && best.Score.MatchesOnlineID(score))
                    beatmapMain[0].Text = $"#{best.Position:N0}";

                if (best.Position != null)
                    beatmapSub[0].Text = $"your best: #{best.Position:N0}";

                beatmapSub[1].Text = $"your best: {best.Score.Accuracy:0.00%}";
                beatmapSub[2].Text = $"your best: {best.Score.MaxCombo:N0}x";
                beatmapSub[3].Text = $"your best: {best.Score.TotalScore:N0}";

                if (best.Score.PP != null)
                    beatmapSub[5].Text = $"your best: {best.Score.PP:N0}pp";
            });

            api.Queue(req);
        }

        private void fetchBeatmapInfo()
        {
            if (score.BeatmapInfo == null || score.BeatmapInfo.OnlineID <= 0 || !api.IsLoggedIn)
                return;

            var req = new GetBeatmapRequest(score.BeatmapInfo);

            req.Success += beatmap => Schedule(() =>
            {
                if (beatmap.PlayCount <= 0)
                {
                    beatmapInfoLine.Text = @"no plays recorded on this beatmap yet";
                    return;
                }

                double passRate = beatmap.PassCount * 100.0 / beatmap.PlayCount;
                string since = beatmap.BeatmapSet != null ? $" since {beatmap.BeatmapSet.Submitted:MMM yyyy}" : string.Empty;

                beatmapInfoLine.Text = $"{beatmap.PlayCount:N0} plays{since} | {passRate:0}% pass rate";
            });

            api.Queue(req);
        }

        /// <summary>
        /// el pp conseguido por ESTA play para la celda Performance de la fila Beatmap Ranking.
        /// los scores frescos todavia no traen pp del server, asi que lo calculamos local.
        /// </summary>
        private void computeAchievedPp()
        {
            if (score.PP != null || score.BeatmapInfo == null)
                return;

            var calculator = score.Ruleset.CreateInstance().CreatePerformanceCalculator();

            if (calculator == null)
                return;

            var token = (cancellation = new CancellationTokenSource()).Token;

            Task.Run(async () =>
            {
                var stars = await difficultyCache.GetDifficultyAsync(score.BeatmapInfo, score.Ruleset, score.Mods, token).ConfigureAwait(false);

                if (stars?.DifficultyAttributes == null)
                    return;

                var achieved = await calculator.CalculateAsync(score, stars.Value.DifficultyAttributes, token).ConfigureAwait(false);

                Schedule(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    beatmapMain[5].Text = $"{achieved.Total:N0}pp";
                });
            }, token);
        }

        private void fillOverallRow(UserStatistics? stats, ScoreBasedUserStatisticsUpdate? update)
        {
            overallMain[0].Text = stats?.GlobalRank != null ? $"#{stats.GlobalRank:N0}" : @"-";
            overallMain[1].Text = stats != null ? $"{stats.Accuracy / 100:0.00%}" : @"-";
            overallMain[2].Text = stats != null ? $"{stats.MaxCombo:N0}x" : @"-";
            overallMain[3].Text = stats != null ? $"{stats.RankedScore:N0}" : @"-";
            overallMain[4].Text = stats != null ? $"{stats.TotalScore:N0}" : @"-";
            overallMain[5].Text = stats?.PP != null ? $"{stats.PP:N0}pp" : @"-";

            if (update == null)
                return;

            // deltas before -> after, "(+N)" verde / rojo como stable.
            setDelta(overallSub[0], (update.Before.GlobalRank ?? 0) - (update.After.GlobalRank ?? 0), @"#");
            setDelta(overallSub[1], Math.Round((update.After.Accuracy - update.Before.Accuracy) / 100, 4), @"%");
            setDelta(overallSub[2], update.After.MaxCombo - update.Before.MaxCombo, @"x");
            setDelta(overallSub[3], update.After.RankedScore - update.Before.RankedScore, string.Empty);
            setDelta(overallSub[4], update.After.TotalScore - update.Before.TotalScore, string.Empty);
            setDelta(overallSub[5], (double)((update.After.PP ?? 0) - (update.Before.PP ?? 0)), @"pp");

            // el total score sumado se resalta con la caja verde de stable; el delta pasa a
            // blanco para que se lea sobre el verde.
            bool gainedScore = update.After.TotalScore - update.Before.TotalScore > 0;
            totalScoreHighlight.Alpha = gainedScore ? 0.9f : 0;

            if (gainedScore)
                overallSub[4].Colour = Color4.White;
        }

        private static void setDelta(OsuSpriteText text, double delta, string kind)
        {
            if (delta == 0)
            {
                text.Text = @"-";
                text.Colour = Color4.White.Opacity(0.55f);
                return;
            }

            string formatted = kind switch
            {
                @"%" => $"({delta:+0.00%;-0.00%})",
                @"pp" => $"({delta:+0.0;-0.0}pp)",
                @"#" => $"({delta:+#,0;-#,0})",
                @"x" => $"({delta:+#,0;-#,0})",
                _ => $"({delta:+#,0;-#,0})",
            };

            text.Text = formatted;
            text.Colour = delta > 0 ? delta_green : delta_red;
        }

        #endregion

        #region acciones

        private bool replayExported;

        private void onExportReplay(bool ticked)
        {
            // stable exporta el replay al cerrar el dialog si el checkbox quedo marcado;
            // nosotros exportamos al tildar (una sola vez).
            if (!ticked || replayExported)
                return;

            replayExported = true;
            scoreManager.Export(score);
        }

        private void onToggleFavourite(bool ticked)
        {
            var setId = score.BeatmapInfo?.BeatmapSet?.OnlineID ?? 0;

            if (setId <= 0 || !api.IsLoggedIn)
                return;

            api.Queue(new PostBeatmapFavouriteRequest(setId, ticked ? BeatmapFavouriteAction.Favourite : BeatmapFavouriteAction.UnFavourite));
        }

        #endregion

        /// <summary>
        /// el pCheckbox de stable: circulito + label, click togglea. minimalista pero suficiente
        /// para las dos opciones de la esquina del dialog.
        /// </summary>
        private partial class LegacyCheckbox : OsuClickableContainer
        {
            private readonly string label;
            private readonly Action<bool> onToggle;

            private CircularContainer circle = null!;
            private Box fill = null!;
            private bool ticked;

            public LegacyCheckbox(string label, Action<bool> onToggle)
            {
                this.label = label;
                this.onToggle = onToggle;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                AutoSizeAxes = Axes.Both;

                Children = new Drawable[]
                {
                    circle = new CircularContainer
                    {
                        Size = new Vector2(19),
                        Masking = true,
                        BorderThickness = 2.5f,
                        BorderColour = Color4.White,
                        Child = fill = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.White,
                            Alpha = 0,
                            AlwaysPresent = true,
                        },
                    },
                    new OsuSpriteText
                    {
                        Position = new Vector2(26, 0),
                        Font = LegacyFonts.Get(19),
                        Shadow = true,
                        Text = label,
                    },
                };

                Action = () =>
                {
                    ticked = !ticked;
                    fill.FadeTo(ticked ? 0.9f : 0, 120);
                    onToggle(ticked);
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                circle.BorderColour = gold;
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                circle.BorderColour = Color4.White;
                base.OnHoverLost(e);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            cancellation?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
