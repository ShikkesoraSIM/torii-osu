// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online.API;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    /// <summary>
    /// Mini preview card used by the aura settings picker — renders the
    /// currently-selected <see cref="AuraPreset"/> behind a sample username
    /// (the local user's, when logged in) so changes in the dropdown have
    /// an immediately-visible result without leaving the settings overlay.
    /// </summary>
    public partial class AuraSettingsPreview : CompositeDrawable
    {
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private Container particleHost = null!;
        private OsuSpriteText usernameText = null!;
        private OsuSpriteText emptyHint = null!;

        private ParticleAuraEmitter? currentEmitter;

        public AuraSettingsPreview()
        {
            RelativeSizeAxes = Axes.X;
            Height = 90;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    // Subtle dark backplate so red/cyan particles are easier
                    // to read against the settings panel background.
                    Colour = Color4.Black.Opacity(0.35f),
                },
                particleHost = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    // Particle emitter slots in here on Show(); username
                    // sits on top so embers/leaves render BEHIND the text.
                    Children = new Drawable[]
                    {
                        usernameText = new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = OsuFont.GetFont(size: 22, weight: FontWeight.SemiBold),
                            Text = "Preview",
                        },
                    },
                },
                emptyHint = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.Regular, italics: true),
                    // Soft grey so it reads as a hint, not body text.
                    Colour = new Color4(0.6f, 0.6f, 0.6f, 1f),
                    Text = "(no aura — your name will render plain)",
                    Alpha = 0,
                    // Sits below the username vertically so it doesn't overlap.
                    Y = 22,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            // Use the real local username when available so the preview
            // matches what the user actually sees in chat / profile.
            api.LocalUser.BindValueChanged(u =>
            {
                if (u.NewValue != null && !string.IsNullOrEmpty(u.NewValue.Username))
                    usernameText.Text = u.NewValue.Username;
            }, true);
        }

        /// <summary>
        /// Swap to the given preset. Pass null for "no aura" — the username
        /// renders plain and a small hint reminds the user nothing's selected.
        /// </summary>
        public void Show(AuraPreset? preset)
        {
            if (currentEmitter != null)
            {
                particleHost.Remove(currentEmitter, disposeImmediately: true);
                currentEmitter = null;
            }

            if (preset == null)
            {
                emptyHint.FadeIn(180, Easing.OutQuad);
                return;
            }

            emptyHint.FadeOut(120, Easing.OutQuad);

            currentEmitter = new ParticleAuraEmitter(preset)
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                // Higher Depth = drawn FIRST = renders behind the username
                // text (which is at default Depth 0). Set before Add so the
                // initial draw order is correct without a re-sort.
                Depth = 1f,
            };

            particleHost.Add(currentEmitter);
        }
    }
}
