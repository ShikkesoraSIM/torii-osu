// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Graphics.Containers;
using osuTK.Graphics;

namespace osu.Game.Skinning
{
    public partial class LegacyKiaiFlashingDrawable : BeatSyncedContainer
    {
        public Color4 KiaiGlowColour
        {
            get => FlashingDrawable.Colour;
            set => FlashingDrawable.Colour = value;
        }

        private readonly Drawable mainDrawable;
        public readonly Drawable FlashingDrawable;

        private const float flash_opacity = 0.3f;

        public LegacyKiaiFlashingDrawable(Func<Drawable?> creationFunc)
        {
            AutoSizeAxes = Axes.Both;

            Children = new[]
            {
                mainDrawable = (creationFunc.Invoke() ?? Empty()).With(d =>
                {
                    d.Anchor = Anchor.Centre;
                    d.Origin = Anchor.Centre;
                }),
                FlashingDrawable = (creationFunc.Invoke() ?? Empty()).With(d =>
                {
                    d.Anchor = Anchor.Centre;
                    d.Origin = Anchor.Centre;
                    d.Alpha = 0;
                    d.Blending = BlendingParameters.Additive;
                })
            };
        }

        /// <summary>
        /// Torii: swap the texture of BOTH inner sprites (the main one + the kiai-flash
        /// overlay) at runtime. Used by <see cref="osu.Game.Rulesets.Osu.Skinning.Legacy.LegacyMainCirclePiece"/>
        /// to pick a different hitcircle texture per combo color slot
        /// (<c>hitcircle1.png</c>, <c>hitcircle2.png</c>, …) without rebuilding the
        /// drawable tree on every pool-acquisition. No-op for non-<see cref="Sprite"/>
        /// inner children — the factory the caller passes us must produce sprites for
        /// the texture swap to land; if it produced something else (a composite, an
        /// animation), this returns silently without throwing.
        /// </summary>
        public void SetTexture(Texture? texture)
        {
            if (mainDrawable is Sprite mainSprite)
                mainSprite.Texture = texture;
            if (FlashingDrawable is Sprite flashSprite)
                flashSprite.Texture = texture;
        }

        protected override void OnNewBeat(int beatIndex, TimingControlPoint timingPoint, EffectControlPoint effectPoint, ChannelAmplitudes amplitudes)
        {
            if (!effectPoint.KiaiMode)
                return;

            FlashingDrawable
                .FadeTo(flash_opacity)
                .Then()
                .FadeOut(Math.Max(80, timingPoint.BeatLength - 80), Easing.OutSine);
        }
    }
}
