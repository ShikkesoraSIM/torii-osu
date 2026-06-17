// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Audio;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Skinning.Select
{
    public partial class LegacyFooterUser : CompositeDrawable
    {
        private Box background = null!;
        private OsuSpriteText usernameText = null!;
        private OsuTextFlowContainer infoText = null!;
        private OsuSpriteText rankText = null!;
        private Sprite rulesetIcon = null!;
        private Box levelBar = null!;
        private UpdateableAvatar avatar = null!;
        private SkinnableSound hoverSound = null!;

        private const float panel_width = 340;
        private const float panel_height = 82;
        private const float level_bar_max_width = 232;

        [Resolved]
        private LocalUserStatisticsProvider userStatisticsProvider { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            // Torii: the upstream layout (osu-stable reference coordinates with negative
            // positions + OriginPosition offsets) renders incorrectly inside lazer's old
            // song-select footer, so this is laid out cleanly inside a fixed-size masked
            // panel. Same data + visual intent (stable-style user panel), sane coordinates.
            Size = new Vector2(panel_width, panel_height);

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(20, 20, 20, 255),
                        Alpha = 0.7f,
                    },
                    avatar = new UpdateableAvatar(isInteractive: false, showUserPanelOnHover: false)
                    {
                        Size = new Vector2(panel_height - 12),
                        Position = new Vector2(6, 6),
                        Masking = true,
                        CornerRadius = 4,
                    },
                    usernameText = new OsuSpriteText
                    {
                        Position = new Vector2(panel_height + 4, 6),
                        Font = OsuFont.GetFont(size: 22, weight: FontWeight.SemiBold),
                    },
                    infoText = new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: 15))
                    {
                        Position = new Vector2(panel_height + 4, 32),
                        AutoSizeAxes = Axes.Both,
                    },
                    rulesetIcon = new Sprite
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Position = new Vector2(-8, 8),
                        Size = new Vector2(24),
                        Colour = Color4.White.Opacity(110),
                    },
                    rankText = new OsuSpriteText
                    {
                        Anchor = Anchor.BottomRight,
                        Origin = Anchor.BottomRight,
                        Position = new Vector2(-8, -10),
                        Font = OsuFont.GetFont(size: 34, weight: FontWeight.Bold),
                    },
                    new Box
                    {
                        // level bar background
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Position = new Vector2(panel_height + 4, -5),
                        Size = new Vector2(level_bar_max_width, 4),
                        Colour = Color4.White.Opacity(40),
                    },
                    levelBar = new Box
                    {
                        // level bar fill
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Position = new Vector2(panel_height + 4, -5),
                        Size = new Vector2(0, 4),
                        Colour = new Color4(252, 184, 6, 255),
                    },
                    hoverSound = new SkinnableSound(new SampleInfo("click-short")),
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            userStatisticsProvider.StatisticsUpdated += onStatisticsUpdated;
            ruleset.BindValueChanged(_ => updateDisplay(), true);
        }

        private void onStatisticsUpdated(UserStatisticsUpdate statistics)
        {
            if (ruleset.Value.Equals(statistics.Ruleset))
                updateDisplay();
        }

        private void updateDisplay()
        {
            var statistics = userStatisticsProvider.GetStatisticsFor(ruleset.Value);
            // Torii: GetUserRequest does not populate UserStatistics.User in this base,
            // so source the username/avatar from the local user (the stats are always
            // the local user's anyway). Falls back gracefully to a guest/offline state.
            var user = statistics?.User ?? api.LocalUser.Value;

            if (statistics == null || user == null || user.Id <= 1)
            {
                usernameText.Text = string.Empty;
                infoText.Text = string.Empty;
                rankText.Text = string.Empty;
                rulesetIcon.Hide();
                avatar.User = null;
                levelBar.Hide();
            }
            else
            {
                usernameText.Text = user.Username;
                infoText.Clear();
                infoText.AddText($"Performance:{statistics.PP:N0}pp");
                infoText.NewLine();
                infoText.AddText($"Accuracy:{statistics.DisplayAccuracy}");
                infoText.NewLine();
                infoText.AddText($"Lv{statistics.Level.Current}");

                if (!statistics.GlobalRank.HasValue)
                    rankText.Hide();
                else
                {
                    int rank = statistics.GlobalRank.Value;

                    rankText.Show();
                    rankText.Text = $"#{rank}";

                    // Stable's faded rank-tier colours, bumped to stay readable on our panel.
                    if (rank > 100000)
                        rankText.Colour = new Color4(255, 255, 255, 130);
                    else if (rank > 50000)
                        rankText.Colour = new Color4(255, 255, 255, 160);
                    else if (rank > 1000)
                        rankText.Colour = new Color4(255, 255, 255, 190);
                    else if (rank > 10)
                        rankText.Colour = new Color4(255, 255, 255, 220);
                    else if (rank > 1)
                        rankText.Colour = new Color4(244, 218, 73, 255);
                    else
                        rankText.Colour = new Color4(88, 171, 248, 255);
                }

                rulesetIcon.Show();
                rulesetIcon.Alpha = 110 / 255f;
                rulesetIcon.Texture = skins.DefaultClassicSkin.GetTexture($"mode-{ruleset.Value.ShortName}-small");

                if (statistics.Level.Progress == 0)
                    levelBar.Hide();
                else
                {
                    levelBar.Width = level_bar_max_width * statistics.Level.Progress / 100f;
                    levelBar.Show();
                }

                avatar.User = user;
            }
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(new Color4(41, 41, 41, 255), 200);
            hoverSound.Play();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(new Color4(1, 1, 1, 255), 200);
            base.OnHoverLost(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (userStatisticsProvider.IsNotNull())
                userStatisticsProvider.StatisticsUpdated -= onStatisticsUpdated;

            base.Dispose(isDisposing);
        }
    }
}
