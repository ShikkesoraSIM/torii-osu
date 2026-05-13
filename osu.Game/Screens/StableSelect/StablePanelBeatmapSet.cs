// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Screens.SelectV2;
using osuTK;
using osuTK.Graphics;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Screens.StableSelect;

public partial class StablePanelBeatmapSet : Panel
{
    public const float HEIGHT = PanelBeatmapSet.HEIGHT;

    private PanelSetBackground setBackground = null!;
    private ScheduledDelegate? scheduledBackgroundRetrieval;
    private OsuSpriteText titleText = null!;
    private OsuSpriteText artistText = null!;
    private OsuSpriteText mapperText = null!;
    private BeatmapSetOnlineStatusPill statusPill = null!;
    private PanelBeatmapSet.SpreadDisplay spreadDisplay = null!;
    private Box accent = null!;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    private GroupedBeatmapSet groupedBeatmapSet => (GroupedBeatmapSet)Item!.Model;

    public StablePanelBeatmapSet()
    {
        PanelXOffset = -72f;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Height = HEIGHT;

        Background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = new Color4(24, 28, 52, 255),
        };

        Content.Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(12, 15, 28, 235),
            },
            accent = new Box
            {
                RelativeSizeAxes = Axes.Y,
                Width = 6,
                Colour = new Color4(255, 156, 66, 255),
            },
            new Container
            {
                RelativeSizeAxes = Axes.Y,
                Width = 92,
                Masking = true,
                CornerRadius = CORNER_RADIUS,
                Padding = new MarginPadding { Left = 10, Top = 10, Bottom = 10 },
                Child = setBackground = new PanelSetBackground
                {
                    RelativeSizeAxes = Axes.Both,
                }
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Left = 110, Right = 18, Top = 12, Bottom = 12 },
                Spacing = new Vector2(2),
                Children = new Drawable[]
                {
                    titleText = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                        UseFullGlyphHeight = false,
                    },
                    artistText = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                        Colour = new Color4(208, 212, 229, 255),
                    },
                    mapperText = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.Regular),
                        Colour = new Color4(154, 161, 189, 255),
                    },
                    new FillFlowContainer
                    {
                        Direction = FillDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(6),
                        Margin = new MarginPadding { Top = 7 },
                        Children = new Drawable[]
                        {
                            statusPill = new BeatmapSetOnlineStatusPill
                            {
                                Animated = false,
                                TextSize = 11,
                            },
                            spreadDisplay = new PanelBeatmapSet.SpreadDisplay
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                        }
                    }
                }
            }
        };
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        var beatmapSet = groupedBeatmapSet.BeatmapSet;

        scheduledBackgroundRetrieval = Scheduler.AddDelayed(s => setBackground.Beatmap = beatmaps.GetWorkingBeatmap(s.Beatmaps.MinBy(b => b.OnlineID)), beatmapSet, 50);

        titleText.Text = new RomanisableString(beatmapSet.Metadata.TitleUnicode, beatmapSet.Metadata.Title);
        artistText.Text = new RomanisableString(beatmapSet.Metadata.ArtistUnicode, beatmapSet.Metadata.Artist);
        mapperText.Text = $"mapped by {beatmapSet.Metadata.Author.Username}";
        statusPill.Status = beatmapSet.Status;
        spreadDisplay.BeatmapSet.Value = beatmapSet;
        spreadDisplay.Expanded.Value = Expanded.Value;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Expanded.BindValueChanged(v => spreadDisplay.Expanded.Value = v.NewValue, true);
    }

    protected override void FreeAfterUse()
    {
        base.FreeAfterUse();

        scheduledBackgroundRetrieval?.Cancel();
        scheduledBackgroundRetrieval = null;
        setBackground.Beatmap = null;
        spreadDisplay.BeatmapSet.Value = null;
    }

    protected override void Update()
    {
        base.Update();

        AccentColour = Expanded.Value ? colourProvider.Highlight1 : new Color4(255, 156, 66, 255);
        accent.Colour = AccentColour ?? colourProvider.Highlight1;
    }

    public override MenuItem[]? ContextMenuItems =>
    [
        new OsuMenuItem(Expanded.Value ? WebCommonStrings.ButtonsCollapse.ToSentence() : WebCommonStrings.ButtonsExpand.ToSentence(), MenuItemType.Highlighted, () => TriggerClick())
    ];
}
