// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using NUnit.Framework;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Online.Chat;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.Online
{
    /// <summary>
    /// Visual A/B sandbox for the Torii Briefing overlay. Each <c>[Test]</c>
    /// applies a different recipe to the panel's <see cref="BriefingGlass"/>
    /// shadow + surface so you can flip through variants in the test
    /// browser sidebar without rebuilding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tests are grouped alphabetically in the sidebar by prefix:
    /// </para>
    /// <list type="bullet">
    ///     <item><description><b>Sample*</b> — basic visibility / close behaviour.</description></item>
    ///     <item><description><b>ShadowBlack_*</b> — neutral black shadow recipes (macOS / Windows modal vocabulary).</description></item>
    ///     <item><description><b>ShadowColor_*</b> — alt-brand-colour shadows (cyan, mint, coral, amber, sky).</description></item>
    ///     <item><description><b>ShadowNone_*</b> — no drop shadow at all; depends entirely on border + scrim contrast.</description></item>
    ///     <item><description><b>ShadowPink_*</b> — every Torii-pink recipe (signature halo, contact, symmetric, long drop, etc.).</description></item>
    ///     <item><description><b>ShadowSurface_*</b> — shadows tinted with the surface palette (deep navy, warm).</description></item>
    /// </list>
    /// <para>
    /// All recipes only mutate the panel's BriefingGlass shadow at runtime
    /// — production defaults stay at whatever <c>ToriiBriefingOverlay.load()</c>
    /// hardcodes. Once a winning recipe is picked, copy its values into
    /// that load method and delete the rest.
    /// </para>
    /// </remarks>
    public partial class TestSceneToriiBriefingOverlay : OsuTestScene
    {
        private ToriiBriefingOverlay briefing;
        private ChannelManager channelManager;

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            Child = new DependencyProvidingContainer
            {
                RelativeSizeAxes = Axes.Both,
                CachedDependencies = new (Type, object)[]
                {
                    (typeof(ChannelManager), channelManager = new ChannelManager(API)),
                },
                Children = new Drawable[]
                {
                    channelManager,
                    briefing = new ToriiBriefingOverlay(channelManager),
                },
            };
        });

        // ============================================================
        //  Sample / behaviour tests (kept for regression coverage)
        // ============================================================

        [Test]
        public void SampleBriefingShows()
        {
            AddStep("show sample briefing", () => briefing.ShowSampleBriefing());
            AddUntilStep("briefing visible", () => briefing.State.Value == Visibility.Visible);
        }

        [Test]
        public void SampleBriefingCanClose()
        {
            AddStep("show sample briefing", () => briefing.ShowSampleBriefing());
            AddUntilStep("briefing visible", () => briefing.State.Value == Visibility.Visible);
            AddStep("hide briefing", () => briefing.Hide());
            AddUntilStep("briefing hidden", () => briefing.State.Value == Visibility.Hidden);
        }

        // ============================================================
        //  Pink — every Torii-pink recipe worth seeing
        // ============================================================

        [Test]
        public void ShadowPink_01_Signature_WideHalo()
        {
            // The first pass: very wide halo. Strong brand presence; can feel
            // like an alert/announcement rather than a calm dialog.
            applyShadow(BriefingTheme.AccentPink, opacity: 0.34f, radius: 38f, roundness: 8f, offsetY: 14f);
            show();
        }

        [Test]
        public void ShadowPink_02_Subtle_Default()
        {
            // The current production default. Tight pink, "signature" without screaming.
            applyShadow(BriefingTheme.AccentPink, opacity: 0.18f, radius: 24f, roundness: 8f, offsetY: 8f);
            show();
        }

        [Test]
        public void ShadowPink_03_Contact_VeryTight()
        {
            // Tight enough that the shadow barely extends past the corner radius.
            // Reads as a coloured edge tint rather than a halo.
            applyShadow(BriefingTheme.AccentPink, opacity: 0.32f, radius: 10f, roundness: 4f, offsetY: 4f);
            show();
        }

        [Test]
        public void ShadowPink_04_Wide_Soft_Spotlight()
        {
            // Big radius + low opacity = "premium spotlight" feel. Like the
            // panel is sitting on a soft pink stage light.
            applyShadow(BriefingTheme.AccentPink, opacity: 0.10f, radius: 50f, roundness: 14f, offsetY: 0f);
            show();
        }

        [Test]
        public void ShadowPink_05_Symmetric_NoOffset()
        {
            // No offset → shadow extends equally in all directions. The panel
            // looks like it's ringed by a halo rather than dropping a shadow.
            applyShadow(BriefingTheme.AccentPink, opacity: 0.22f, radius: 22f, roundness: 8f, offsetY: 0f);
            show();
        }

        [Test]
        public void ShadowPink_06_LongDrop_Newspaper()
        {
            // Narrow blur + big offset = the shadow lives mostly below the
            // panel, like a printed page sitting on the desk. Slightly
            // editorial / newspapery.
            applyShadow(BriefingTheme.AccentPink, opacity: 0.30f, radius: 12f, roundness: 6f, offsetY: 24f);
            show();
        }

        [Test]
        public void ShadowPink_07_LitFromBelow()
        {
            // Negative Y offset → shadow appears ABOVE the panel. Reads as if
            // a light source is below the panel shining up. Unusual but striking.
            applyShadow(BriefingTheme.AccentPink, opacity: 0.20f, radius: 26f, roundness: 8f, offsetY: -10f);
            show();
        }

        [Test]
        public void ShadowPink_08_Darkened_Rooted()
        {
            // Pink darkened toward magenta-burgundy. Feels grounded / warmer.
            applyShadow(BriefingTheme.AccentPink.Darken(0.35f), opacity: 0.40f, radius: 26f, roundness: 8f, offsetY: 10f);
            show();
        }

        [Test]
        public void ShadowPink_09_Lightened_Airy()
        {
            // Pink lightened toward salmon. Lighter, more "morning" feel.
            applyShadow(BriefingTheme.AccentPink.Lighten(0.25f), opacity: 0.18f, radius: 30f, roundness: 10f, offsetY: 8f);
            show();
        }

        // ============================================================
        //  Black — neutral / professional recipes
        // ============================================================

        [Test]
        public void ShadowBlack_01_ModalSoft()
        {
            // macOS modal vocabulary. Clean, neutral, doesn't compete with brand.
            applyShadow(Color4.Black, opacity: 0.45f, radius: 26f, roundness: 10f, offsetY: 12f);
            show();
        }

        [Test]
        public void ShadowBlack_02_HeavyDramatic()
        {
            // Windows-11-modal heavy. Strong elevation cue, almost a vignette.
            applyShadow(Color4.Black, opacity: 0.70f, radius: 32f, roundness: 12f, offsetY: 16f);
            show();
        }

        [Test]
        public void ShadowBlack_03_TightSticker()
        {
            // Tight + sharp = the panel reads as a sticker pasted on the screen.
            // Very different vibe; useful as a comparison baseline.
            applyShadow(Color4.Black, opacity: 0.60f, radius: 4f, roundness: 2f, offsetY: 2f);
            show();
        }

        [Test]
        public void ShadowBlack_04_SoftDeepDistant()
        {
            // Big radius + big offset + low opacity = the panel feels far away
            // from whatever's behind it. Premium / cinematic.
            applyShadow(Color4.Black, opacity: 0.30f, radius: 60f, roundness: 16f, offsetY: 24f);
            show();
        }

        // ============================================================
        //  Alt brand colours — non-pink recipes
        // ============================================================

        [Test]
        public void ShadowColor_01_CyanGlow()
        {
            // Cool, calmer, more "tech briefing" than "alert dispatch".
            applyShadow(BriefingTheme.AccentCyan, opacity: 0.22f, radius: 28f, roundness: 10f, offsetY: 10f);
            show();
        }

        [Test]
        public void ShadowColor_02_MintGain()
        {
            // Mint = positive / "good news". Could fit a session that's mostly gains.
            applyShadow(BriefingTheme.AccentGain, opacity: 0.22f, radius: 28f, roundness: 10f, offsetY: 10f);
            show();
        }

        [Test]
        public void ShadowColor_03_CoralWarn()
        {
            // Coral / loss. Warning vibe — wouldn't ship as default but interesting.
            applyShadow(BriefingTheme.AccentLoss, opacity: 0.22f, radius: 28f, roundness: 10f, offsetY: 10f);
            show();
        }

        [Test]
        public void ShadowColor_04_AmberAlert()
        {
            // Amber. Warm, "important but not urgent" alert tone.
            applyShadow(BriefingTheme.AccentAmber, opacity: 0.20f, radius: 26f, roundness: 10f, offsetY: 8f);
            show();
        }

        [Test]
        public void ShadowColor_05_SkyTech()
        {
            // Cool blue, separate from cyan. Clean / corporate / technical.
            applyShadow(BriefingTheme.AccentSky, opacity: 0.22f, radius: 28f, roundness: 10f, offsetY: 10f);
            show();
        }

        [Test]
        public void ShadowColor_06_WhiteBacklit()
        {
            // White outer halo — looks like the panel is backlit. Outer
            // bound: probably too loud for prod but useful as a sanity check
            // for "what would white feel like".
            applyShadow(Color4.White, opacity: 0.10f, radius: 30f, roundness: 12f, offsetY: 0f);
            show();
        }

        // ============================================================
        //  Surface palette — shadows tinted with the same colours
        //  the panel surface itself uses
        // ============================================================

        [Test]
        public void ShadowSurface_01_NavyAmbient()
        {
            // Deep navy at higher opacity — same colour as the panel base, so
            // the shadow reads as "more of the same colour" rather than a
            // separate halo. Monolithic, clean.
            applyShadow(BriefingTheme.SurfaceBase, opacity: 0.65f, radius: 32f, roundness: 12f, offsetY: 14f);
            show();
        }

        [Test]
        public void ShadowSurface_02_WarmTint()
        {
            // Warm panel tone for the shadow. Dark-red-ish purple tinge.
            applyShadow(BriefingTheme.SurfaceWarm, opacity: 0.50f, radius: 28f, roundness: 10f, offsetY: 12f);
            show();
        }

        // ============================================================
        //  No shadow — minimal recipes (the panel relies on its border
        //  and the scrim contrast for separation)
        // ============================================================

        [Test]
        public void ShadowNone_01_Pure()
        {
            // No drop shadow at all. Panel is a clean rectangle on the scrim.
            applyShadow(Color4.Black, opacity: 0f, radius: 0f, roundness: 0f, offsetY: 0f);
            show();
        }

        // ============================================================
        //  Helpers
        // ============================================================

        /// <summary>
        /// Wraps the panel-shadow mutation in an AddStep so the recipe is
        /// recorded in the test browser's step list (you can read off the
        /// numbers after the fact when picking a winner).
        /// </summary>
        private void applyShadow(Color4 colour, float opacity, float radius, float roundness, float offsetY)
        {
            string label = $"shadow: {describe(colour)} a={opacity:F2} r={radius:F0} round={roundness:F0} y={offsetY:F0}";

            AddStep(label, () =>
            {
                var p = briefing.PanelGlass;
                if (p == null) return;
                p.ShadowColor = colour;
                p.ShadowOpacity = opacity;
                p.ShadowRadius = radius;
                p.ShadowRoundness = roundness;
                p.ShadowOffset = new Vector2(0, offsetY);
            });
        }

        private void show() => AddStep("show sample briefing", () => briefing.ShowSampleBriefing());

        private static string describe(Color4 c)
        {
            // Short tag so the test-step label stays scannable. Picks up the
            // common branded colours by RGB match; falls back to a hex stub
            // for anything bespoke (like .Lighten / .Darken outputs).
            if (eq(c, BriefingTheme.AccentPink)) return "pink";
            if (eq(c, BriefingTheme.AccentCyan)) return "cyan";
            if (eq(c, BriefingTheme.AccentGain)) return "mint";
            if (eq(c, BriefingTheme.AccentLoss)) return "coral";
            if (eq(c, BriefingTheme.AccentAmber)) return "amber";
            if (eq(c, BriefingTheme.AccentSky)) return "sky";
            if (eq(c, BriefingTheme.SurfaceBase)) return "navy";
            if (eq(c, BriefingTheme.SurfaceWarm)) return "warm";
            if (eq(c, Color4.Black)) return "black";
            if (eq(c, Color4.White)) return "white";

            // Bespoke colour — short hex.
            return $"#{(int)(c.R * 255):X2}{(int)(c.G * 255):X2}{(int)(c.B * 255):X2}";
        }

        private static bool eq(Color4 a, Color4 b) =>
            Math.Abs(a.R - b.R) < 0.005f &&
            Math.Abs(a.G - b.G) < 0.005f &&
            Math.Abs(a.B - b.B) < 0.005f;
    }
}
