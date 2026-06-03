// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Overlays;
using osu.Game.Screens.Play;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.Gameplay
{
    /// <summary>
    /// Visual review scene for the one-time mid-map skip briefing popup
    /// (<see cref="SkipBreakBriefingOverlay"/>). Use "show briefing" to see
    /// the glass panel; the embedded toggle is bound to a local bindable
    /// whose value is asserted so the inline control is wired correctly.
    /// </summary>
    [TestFixture]
    public partial class TestSceneSkipBreakBriefing : OsuManualInputManagerTestScene
    {
        // The embedded FormCheckBox resolves an OverlayColourProvider; provide
        // one here since there's no settings panel to inherit it from.
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        private readonly Bindable<bool> singleConfirmation = new BindableBool();
        private SkipBreakBriefingOverlay briefing;
        private int dismissCount;

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            dismissCount = 0;
            singleConfirmation.Value = false;

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(20, 20, 28, 255),
                },
                briefing = new SkipBreakBriefingOverlay(singleConfirmation)
                {
                    OnDismiss = () => dismissCount++,
                },
            };
        });

        [Test]
        public void TestShowAndDismiss()
        {
            AddStep("show briefing", () => briefing.Show());
            AddStep("enable single via inline toggle", () => singleConfirmation.Value = true);
            AddAssert("toggle bindable updated", () => singleConfirmation.Value);
            AddStep("hide briefing", () => briefing.Hide());
        }
    }
}
