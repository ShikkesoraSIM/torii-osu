// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiExperimentalSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Access Codes";

        private Container codeInputContainer = null!;
        private OsuTextBox codeBox = null!;
        private Box codeInputGlow = null!;
        private OsuSpriteText statusText = null!;

        // Alpha feature toggles (only visible when their feature is unlocked)
        private Drawable alphaNavbarToggle = null!;

        private Bindable<bool> alphaToolbarEnabled = null!;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            alphaToolbarEnabled = config.GetBindable<bool>(OsuSetting.AlphaToolbarEnabled);

            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(6),
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "Access",
                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                            Colour = new Color4(220, 220, 220, 255),
                        },
                        codeInputContainer = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 36,
                            Masking = true,
                            CornerRadius = 6,
                            CornerExponent = 3f,
                            Children = new Drawable[]
                            {
                                codeBox = new OsuTextBox
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 36,
                                    PlaceholderText = "Enter access code",
                                },
                                codeInputGlow = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Alpha = 0,
                                },
                            }
                        },
                        statusText = new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 11),
                            Text = string.Empty,
                            Colour = new Color4(180, 180, 180, 255),
                        },
                    },
                },
                // Alpha features section — items here are hidden until their code is entered
                alphaNavbarToggle = new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Alpha Navbar",
                    Current = config.GetBindable<bool>(OsuSetting.AlphaToolbarUse),
                })
                {
                    Keywords = new[] { @"navbar", @"toolbar", @"alpha", @"torii" },
                    Alpha = alphaToolbarEnabled.Value ? 1f : 0f,
                },
            };

            codeBox.OnCommit += (_, __) => applyCode(codeBox.Current.Value);

            alphaToolbarEnabled.BindValueChanged(e =>
            {
                alphaNavbarToggle.Alpha = e.NewValue ? 1f : 0f;
            });
        }

        private void applyCode(string rawCode)
        {
            string code = rawCode.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(code))
                return;

            switch (code)
            {
                case "torii-nav":
                case "toriibar":
                case "alpha-nav":
                    if (alphaToolbarEnabled.Value)
                    {
                        statusText.Text = "Alpha Navbar already unlocked.";
                        statusText.Colour = new Color4(180, 180, 180, 255);
                        playCodeInputFeedback(false);
                    }
                    else
                    {
                        alphaToolbarEnabled.Value = true;
                        statusText.Text = "Alpha Navbar unlocked! Toggle it above.";
                        statusText.Colour = new Color4(129, 242, 145, 255);
                        playCodeInputFeedback(true);
                    }
                    break;

                default:
                    statusText.Text = "No active alpha feature for that code.";
                    statusText.Colour = new Color4(255, 165, 120, 255);
                    playCodeInputFeedback(false);
                    break;
            }

            codeBox.Current.Value = string.Empty;
        }

        private void playCodeInputFeedback(bool success)
        {
            codeInputContainer.ClearTransforms();
            codeInputContainer.MoveToX(-3, 25, Easing.OutSine)
                              .Then()
                              .MoveToX(3, 50, Easing.OutSine)
                              .Then()
                              .MoveToX(-2, 40, Easing.OutSine)
                              .Then()
                              .MoveToX(0, 35, Easing.OutQuint);

            codeInputGlow.ClearTransforms();
            codeInputGlow.Colour = success
                ? new Color4(129, 242, 145, 255)
                : new Color4(255, 165, 120, 255);
            codeInputGlow.FadeTo(0.28f, 70, Easing.OutQuint)
                         .Then()
                         .FadeOut(220, Easing.OutQuint);
        }
    }
}
