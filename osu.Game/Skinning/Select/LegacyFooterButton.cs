// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Audio;

namespace osu.Game.Skinning.Select
{
    public partial class LegacyFooterButton : ClickableContainer
    {
        private readonly string kind;

        private Sprite hoverSprite = null!;
        private SkinnableSound hoverSound = null!;
        private SkinnableSound clickSound = null!;

        /// <summary>Texture source for the button graphics. Null = the ambient (current) skin.</summary>
        public ISkin? TextureSource { get; init; }

        /// <summary>
        /// Stable's "old" footer layout (used for custom skins): the button sprite is
        /// anchored to the TOP and grows downward, instead of the bottom-anchored new layout.
        /// </summary>
        public bool LegacyTopLeftLayout { get; init; }

        public LegacyFooterButton(string kind)
        {
            this.kind = kind;

            Enabled.Value = true;
        }

        [BackgroundDependencyLoader]
        private void load(ISkinSource skin)
        {
            AutoSizeAxes = Axes.Both;

            ISkin source = TextureSource ?? skin;
            Anchor spriteAnchor = LegacyTopLeftLayout ? Anchor.TopLeft : Anchor.BottomLeft;

            Children = new Drawable[]
            {
                new Sprite
                {
                    Anchor = spriteAnchor,
                    Origin = spriteAnchor,
                    Texture = source.GetTexture($"selection-{kind}"),
                    // to match stable, the button input area should not be taken from this sprite. it should be taken from the hover sprite below.
                    // see: https://github.com/peppy/osu-stable-reference/blob/c34a74fb61c17c5667486a12548485d1f03baa2e/osu!/GameModes/Select/SongSelection.cs#L340-L349
                    BypassAutoSizeAxes = Axes.Both,
                },
                hoverSprite = new Sprite
                {
                    Anchor = spriteAnchor,
                    Origin = spriteAnchor,
                    Texture = source.GetTexture($"selection-{kind}-over"),
                    Alpha = 0f,
                    AlwaysPresent = true,
                },
                hoverSound = new SkinnableSound(new SampleInfo("click-short")),
                clickSound = new SkinnableSound(new SampleInfo("click-short-confirm")),
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hoverSprite.FadeIn(100);
            hoverSound.Play();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hoverSprite.FadeOut(100);
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            clickSound.Play();
            return base.OnClick(e);
        }
    }
}
