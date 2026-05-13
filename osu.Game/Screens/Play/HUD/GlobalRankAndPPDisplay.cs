using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Online;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Overlays;


namespace osu.Game.Screens.Play.HUD
{
    /// <summary>
    /// Zeigt permanent die zuletzt empfangenen globalen Rang‑ und PP‑Werte (inkl. Differenzen) an.
    /// </summary>
    public partial class GlobalRankAndPPDisplay : CompositeDrawable
    {
        private OsuSpriteText rankText = null!;
        private OsuSpriteText ppText = null!;
        private OsuSpriteText deltaText = null!;

        [Resolved(canBeNull: true)]
        private UserStatisticsWatcher? watcher { get; set; }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private readonly IBindable<ScoreBasedUserStatisticsUpdate?> latest = new Bindable<ScoreBasedUserStatisticsUpdate?>();

        public GlobalRankAndPPDisplay()
        {
            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.TopRight;
            Origin = Anchor.TopRight;
            Margin = new MarginPadding { Top = 10, Right = 10 };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Padding = new MarginPadding(8),
                CornerRadius = 6,
                Masking = true,
                Child = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new osuTK.Vector2(2),
                    Children = new Drawable[]
                    {
                        rankText = new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                            Colour = colourProvider.Content1
                        },
                        ppText = new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 14),
                            Colour = colourProvider.Content2
                        },
                        deltaText = new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 12),
                            Colour = colourProvider.Light1
                        }
                    }
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (watcher != null)
            {
                latest.BindTo(watcher.LatestUpdate);
                latest.BindValueChanged(e => Schedule(() => applyUpdate(e.NewValue)), true);
            }
            else
            {
                // Falls kein Watcher verfügbar ist, nichts tun.
                ClearDisplay();
            }
        }

        private void applyUpdate(ScoreBasedUserStatisticsUpdate? update)
        {
            if (update == null)
                return;

            var before = update.Before;
            var after = update.After;

            // Global rank (kann null sein)
            string rankStr = after.GlobalRank != null ? $"#{after.GlobalRank.Value:N0}" : "-";
            rankText.Text = $"Global Rank: {rankStr}";

            // PP (kann null sein, falls nicht vorhanden)
            string ppStr = after.PP.HasValue ? $"{after.PP.Value:N2} pp" : "-";
            ppText.Text = $"PP: {ppStr}";

            // Deltas anzeigen (PP-Differenz & Platz‑Änderung)
            string ppDelta = after.PP.HasValue && before.PP.HasValue
                ? (after.PP.Value - before.PP.Value).ToString("+0.00;-0.00;0")
                : string.Empty;

            string rankDelta;
            if (before.GlobalRank == null && after.GlobalRank != null)
                rankDelta = $"Neuer Rang: {rankStr}";
            else if (before.GlobalRank != null && after.GlobalRank == null)
                rankDelta = $"Rang entfernt";
            else if (before.GlobalRank != null && after.GlobalRank != null)
            {
                int diff = (before.GlobalRank.Value - after.GlobalRank.Value); // kleinerer Wert = besser
                rankDelta = diff == 0 ? string.Empty : (diff > 0 ? $"+{diff} Plätze" : $"{diff} Plätze");
            }
            else
                rankDelta = string.Empty;

            deltaText.Text = $"{(string.IsNullOrEmpty(ppDelta) ? "" : $"ΔPP: {ppDelta}")}{(string.IsNullOrEmpty(ppDelta) || string.IsNullOrEmpty(rankDelta) ? "" : "  ")}{rankDelta}";
        }

        private void ClearDisplay()
        {
            rankText.Text = "Global Rank: -";
            ppText.Text = "PP: -";
            deltaText.Text = string.Empty;
        }
    }
}
