// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.SelectV2;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.StableSelect;

public partial class StablePanelBeatmap : Panel
{
    public const float HEIGHT = PanelBeatmap.HEIGHT;

    private StarCounter starCounter = null!;
    private ConstrainedIconContainer difficultyIcon = null!;
    private OsuSpriteText keyCountText = null!;
    private StarRatingDisplay starRatingDisplay = null!;
    private PanelLocalRankDisplay localRank = null!;
    private OsuSpriteText difficultyText = null!;
    private OsuSpriteText authorText = null!;
    private FillFlowContainer mainFill = null!;
    private Box accent = null!;
    private Box backgroundDifficultyTint = null!;

    private IBindable<StarDifficulty>? starDifficultyBindable;
    private CancellationTokenSource? starDifficultyCancellationSource;
    private double starRatingCalculationDebounce;
    private bool starRatingCalculationQueued;

    [Resolved]
    private IRulesetStore rulesets { get; set; } = null!;

    [Resolved]
    private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    [Resolved]
    private ISongSelect? songSelect { get; set; }

    private BeatmapInfo beatmap => ((GroupedBeatmap)Item!.Model).Beatmap;

    public StablePanelBeatmap()
    {
        PanelXOffset = -28f;
    }

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        Height = HEIGHT;

        Icon = difficultyIcon = new ConstrainedIconContainer
        {
            Size = new Vector2(10f),
            Margin = new MarginPadding { Left = 2f, Right = 2f },
            Colour = new Color4(240, 242, 255, 255),
        };

        Background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = new Color4(22, 25, 46, 255),
        };

        Content.Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(14, 16, 31, 245),
            },
            backgroundDifficultyTint = new Box
            {
                RelativeSizeAxes = Axes.Both,
            },
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientHorizontal(Color4.White.Opacity(0.03f), Color4.Transparent),
            },
            accent = new Box
            {
                RelativeSizeAxes = Axes.Y,
                Width = 4,
                Colour = colourProvider.Highlight1,
            },
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Spacing = new Vector2(6),
                Margin = new MarginPadding { Left = 12 },
                Direction = FillDirection.Horizontal,
                Children = new Drawable[]
                {
                    localRank = new PanelLocalRankDisplay
                    {
                        Scale = new Vector2(0.82f),
                        Origin = Anchor.CentreLeft,
                        Anchor = Anchor.CentreLeft,
                    },
                    mainFill = new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Vertical,
                        AutoSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Bottom = 2 },
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                Direction = FillDirection.Horizontal,
                                AutoSizeAxes = Axes.Both,
                                Spacing = new Vector2(4),
                                Children = new Drawable[]
                                {
                                    keyCountText = new OsuSpriteText
                                    {
                                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                                        Alpha = 0,
                                    },
                                    difficultyText = new OsuSpriteText
                                    {
                                        Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold),
                                        UseFullGlyphHeight = false,
                                    },
                                }
                            },
                            authorText = new OsuSpriteText
                            {
                                Colour = new Color4(191, 198, 225, 255),
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                            },
                            new FillFlowContainer
                            {
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(3),
                                AutoSizeAxes = Axes.Both,
                                Children = new Drawable[]
                                {
                                    starRatingDisplay = new StarRatingDisplay(default, StarRatingDisplaySize.Small, animated: true)
                                    {
                                        Origin = Anchor.CentreLeft,
                                        Anchor = Anchor.CentreLeft,
                                        Scale = new Vector2(0.82f),
                                    },
                                    starCounter = new StarCounter
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Scale = new Vector2(0.38f)
                                    }
                                },
                            }
                        }
                    }
                }
            }
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ruleset.BindValueChanged(_ => updateKeyCount());
        mods.BindValueChanged(_ => updateKeyCount(), true);
        ToriiPpVariantState.UsePpDevVariantBindable.BindValueChanged(_ =>
        {
            if (Item == null)
                return;

            if (Item.IsVisible)
                computeStarRating();
            else
            {
                starRatingCalculationQueued = true;
                starRatingCalculationDebounce = 0;
            }
        });
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        difficultyIcon.Icon = getRulesetIcon(beatmap.Ruleset);
        localRank.Beatmap = beatmap;
        difficultyText.Text = beatmap.DifficultyName;
        authorText.Text = BeatmapsetsStrings.ShowDetailsMappedBy(beatmap.Metadata.Author.Username);

        starRatingCalculationQueued = true;
        starRatingCalculationDebounce = 0;
        updateKeyCount();
    }

    protected override void FreeAfterUse()
    {
        base.FreeAfterUse();
        localRank.Beatmap = null;
        starDifficultyBindable = null;
        starDifficultyCancellationSource?.Cancel();
    }

    private Drawable getRulesetIcon(RulesetInfo rulesetInfo)
    {
        var rulesetInstance = rulesets.GetRuleset(rulesetInfo.ShortName)?.CreateInstance();
        return rulesetInstance?.CreateIcon() ?? new SpriteIcon { Icon = FontAwesome.Regular.QuestionCircle };
    }

    private void computeStarRating()
    {
        starDifficultyCancellationSource?.Cancel();
        starDifficultyCancellationSource = new CancellationTokenSource();

        if (Item == null)
            return;

        starDifficultyBindable = difficultyCache.GetBindableDifficulty(beatmap, starDifficultyCancellationSource.Token, SongSelect.DIFFICULTY_CALCULATION_DEBOUNCE);
        starDifficultyBindable.BindValueChanged(starDifficulty =>
        {
            starRatingDisplay.Current.Value = starDifficulty.NewValue;
            starCounter.Current = (float)starDifficulty.NewValue.Stars;
        }, true);
    }

    protected override void Update()
    {
        base.Update();

        if (Item?.IsVisible != true)
        {
            starDifficultyCancellationSource?.Cancel();
            starDifficultyCancellationSource = null;
            starRatingCalculationQueued = false;
            starRatingCalculationDebounce = 0;
        }
        else if (starRatingCalculationQueued)
        {
            starRatingCalculationDebounce += Clock.ElapsedFrameTime;
            if (starRatingCalculationDebounce >= SongSelect.DIFFICULTY_CALCULATION_DEBOUNCE)
            {
                computeStarRating();
                starRatingCalculationQueued = false;
            }
        }

        mainFill.Margin = new MarginPadding { Left = 1 / starRatingDisplay.Scale.X * (localRank.HasRank ? 0 : -3) };

        var diffColour = starRatingDisplay.DisplayedDifficultyColour;
        if (AccentColour != diffColour)
        {
            AccentColour = diffColour;
            accent.Colour = diffColour;
            starCounter.Colour = diffColour;
            backgroundDifficultyTint.Colour = ColourInfo.GradientHorizontal(diffColour.Opacity(0.18f), diffColour.Opacity(0f));
        }

        difficultyIcon.Colour = starRatingDisplay.DisplayedDifficultyTextColour;
    }

    private void updateKeyCount()
    {
        if (Item == null)
            return;

        if (ruleset.Value.OnlineID == 3)
        {
            ILegacyRuleset legacyRuleset = (ILegacyRuleset)ruleset.Value.CreateInstance();
            int keyCount = legacyRuleset.GetKeyCount(beatmap, mods.Value);

            keyCountText.Alpha = 1;
            keyCountText.Text = $"[{keyCount}K]";
        }
        else
            keyCountText.Alpha = 0;
    }

    public override MenuItem[]? ContextMenuItems
    {
        get
        {
            if (Item == null || songSelect == null)
                return Array.Empty<MenuItem>();

            List<MenuItem> items = new List<MenuItem>();
            items.AddRange(songSelect.GetForwardActions(beatmap));
            return items.ToArray();
        }
    }
}
