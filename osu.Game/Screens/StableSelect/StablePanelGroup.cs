// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Screens.SelectV2;
using osuTK.Graphics;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Screens.StableSelect;

public partial class StablePanelGroup : Panel
{
    public const float HEIGHT = PanelGroup.HEIGHT;

    private OsuSpriteText titleText = null!;
    private OsuSpriteText countText = null!;
    private Box accent = null!;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    public StablePanelGroup()
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
            Colour = new Color4(29, 33, 63, 255),
        };

        Content.Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(15, 18, 36, 230),
            },
            accent = new Box
            {
                RelativeSizeAxes = Axes.Y,
                Width = 5,
                Colour = colourProvider.Highlight1,
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Padding = new MarginPadding { Left = 18, Right = 18 },
                Children = new Drawable[]
                {
                    titleText = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.GetFont(size: 17, weight: FontWeight.Bold),
                        UseFullGlyphHeight = false,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                    new CircularContainer
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        AutoSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 8,
                        Child = new Container
                        {
                            AutoSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Horizontal = 10, Vertical = 3 },
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = Color4.Black.Opacity(0.35f),
                                },
                                countText = new OsuSpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                                    UseFullGlyphHeight = false,
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        if (Item?.Model is not GroupDefinition group)
            return;

        titleText.Text = group.Title;
        countText.Text = Item.NestedItemCount.ToString("N0");
    }

    protected override void Update()
    {
        base.Update();

        AccentColour = Expanded.Value || Selected.Value ? colourProvider.Highlight1 : new Color4(99, 119, 173, 255);
        accent.Colour = AccentColour ?? colourProvider.Highlight1;
    }

    public override MenuItem[]? ContextMenuItems =>
    [
        new OsuMenuItem(Expanded.Value ? WebCommonStrings.ButtonsCollapse.ToSentence() : WebCommonStrings.ButtonsExpand.ToSentence(), MenuItemType.Highlighted, () => TriggerClick())
    ];
}
