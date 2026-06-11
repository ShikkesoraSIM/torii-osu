// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Overlays.Cosmetics;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Visual coverage for the gift reveal, focused on text layout. The
    /// long-message / long-sender cases are the regression guard: a long gift
    /// message used to render as a single non-wrapping line and overflow both
    /// sides of the panel. Open the gift in the browser and confirm every line
    /// of text wraps inside the glass.
    /// </summary>
    public partial class TestSceneToriiGiftOverlay : OsuTestScene
    {
        private ToriiGiftOverlay overlay;

        [SetUp]
        public void SetUp() => Schedule(() => Child = overlay = new ToriiGiftOverlay());

        [Test]
        public void LongMessageAndSender()
        {
            AddStep("show gift: long message + long sender", () => overlay.Display(
                "ShikkesoraWithAnAbsurdlyLongSenderNameThatShouldWrap",
                "Your score didn't get submitted. Your score has been manually submitted, because of this little inconvenience, take this small pouch of points as an apology. With Love -Shikkesora.",
                100,
                null));
        }

        [Test]
        public void ShortMessage()
        {
            AddStep("show gift: short message", () => overlay.Display("Torii", "enjoy! :)", 250, null));
        }

        [Test]
        public void NoMessage()
        {
            AddStep("show gift: no message", () => overlay.Display("Torii", null, 500, null));
        }
    }
}
