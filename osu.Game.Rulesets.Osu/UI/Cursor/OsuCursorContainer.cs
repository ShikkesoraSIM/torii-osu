// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Cosmetics;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.Osu.UI.Cursor
{
    public partial class OsuCursorContainer : GameplayCursorContainer, IKeyBindingHandler<OsuAction>
    {
        public new OsuCursor ActiveCursor => (OsuCursor)base.ActiveCursor;

        protected override Drawable CreateCursor() => new OsuCursor();
        protected override Container<Drawable> Content => fadeContainer;

        private readonly Container<Drawable> fadeContainer;

        private readonly Bindable<bool> showTrail = new Bindable<bool>(true);

        private readonly SkinnableDrawable cursorTrail;

        private readonly CursorRippleVisualiser rippleVisualiser;

        // Torii: an equipped store cursor-trail cosmetic replaces the skin's
        // trail (the skin trail is hidden, the cosmetic renders below the head).
        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        // Used to isolate the playfield's own scale from the global UI scale when
        // sizing the cosmetic trail (see Update).
        [Resolved(canBeNull: true)]
        private DrawableRuleset drawableRuleset { get; set; }

        private Drawable cosmeticTrail;

        public OsuCursorContainer()
        {
            InternalChild = fadeContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new CompositeDrawable[]
                {
                    cursorTrail = new SkinnableDrawable(new OsuSkinComponentLookup(OsuSkinComponents.CursorTrail), _ => new DefaultCursorTrail(), confineMode: ConfineMode.NoScaling),
                    rippleVisualiser = new CursorRippleVisualiser(),
                    new SkinnableDrawable(new OsuSkinComponentLookup(OsuSkinComponents.CursorParticles), confineMode: ConfineMode.NoScaling),
                }
            };
        }

        [BackgroundDependencyLoader(true)]
        private void load(OsuRulesetConfigManager rulesetConfig)
        {
            rulesetConfig?.BindWith(OsuRulesetSetting.ShowCursorTrail, showTrail);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            showTrail.BindValueChanged(_ => updateSkinTrailVisibility());

            if (cosmetics != null)
            {
                cosmetics.EquippedTrailId.BindValueChanged(_ => Schedule(rebuildCosmeticTrail), true);
                cosmetics.CustomisationChanged += onCustomisationChanged;
            }
            else
                updateSkinTrailVisibility();

            ActiveCursor.CursorScale.BindValueChanged(e =>
            {
                var newScale = new Vector2(e.NewValue);

                rippleVisualiser.CursorScale = newScale;
                updateTrailScale();
            }, true);
            cursorTrail.OnSkinChanged += updateTrailScale;
        }

        private void updateTrailScale()
        {
            if (cursorTrail.Drawable is CursorTrail trail) trail.CursorScale = new Vector2(ActiveCursor.CursorScale.Value);
        }

        private void rebuildCosmeticTrail()
        {
            if (cosmeticTrail != null)
            {
                fadeContainer.Remove(cosmeticTrail, true);
                cosmeticTrail = null;
            }

            var created = cosmetics?.CreateEquippedTrail();
            if (created != null)
            {
                cosmeticTrail = created;
                cosmeticTrail.Depth = 1f; // behind the cursor head
                fadeContainer.Add(cosmeticTrail);
            }

            updateSkinTrailVisibility();
        }

        // Live slider tweak from the store: if the changed trail is the one
        // we're showing, re-apply its length/density to the existing instance
        // (no rebuild, so it updates under the cursor as you drag).
        private void onCustomisationChanged(string id) => Schedule(() =>
        {
            if (cosmeticTrail != null && cosmetics != null && id == cosmetics.EquippedTrailId.Value)
                cosmetics.ApplyCustomisationTo(cosmeticTrail, id);
        });

        private void updateSkinTrailVisibility()
        {
            // When a cosmetic trail is equipped, hide the skin trail entirely
            // (same effect as the cursor-trail setting being off).
            bool cosmeticActive = cosmeticTrail != null;
            cursorTrail.FadeTo(!cosmeticActive && showTrail.Value ? 1 : 0, 200);
        }

        private int downCount;

        private void updateExpandedState()
        {
            if (downCount > 0)
                ActiveCursor.Expand();
            else
                ActiveCursor.Contract();
        }

        protected override void Update()
        {
            base.Update();

            if (cursorTrail.Drawable is CursorTrail trail)
            {
                trail.NewPartScale = ActiveCursor.CurrentExpandedScale;
                trail.PartRotation = ActiveCursor.CurrentRotation;
            }

            // Torii: the gameplay cursor lives INSIDE the scaled playfield, so an
            // equipped cosmetic trail renders larger here than in the menu (1:1).
            // Counter ONLY the playfield's own scale, not the global UI scale:
            //   cursorScale  = global UI scale * playfield scale  (this container)
            //   screenScale  = global UI scale                    (the ruleset root)
            //   playfield    = cursorScale / screenScale
            // Scaling the trail by 1/playfield lands it at the same on-screen size
            // as the 1:1 menu. (Countering the full cursorScale also removed the UI
            // scale, which made it a touch too small.)
            if (cosmeticTrail != null)
            {
                float cursorScale = DrawWidth > 0 ? ScreenSpaceDrawQuad.Width / DrawWidth : 1f;
                float screenScale = drawableRuleset != null && drawableRuleset.DrawWidth > 0
                    ? drawableRuleset.ScreenSpaceDrawQuad.Width / drawableRuleset.DrawWidth
                    : cursorScale;

                if (cursorScale > 0.0001f)
                    cosmeticTrail.Scale = new Vector2(screenScale / cursorScale);
            }
        }

        public bool OnPressed(KeyBindingPressEvent<OsuAction> e)
        {
            switch (e.Action)
            {
                case OsuAction.LeftButton:
                case OsuAction.RightButton:
                    downCount++;
                    updateExpandedState();
                    break;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<OsuAction> e)
        {
            switch (e.Action)
            {
                case OsuAction.LeftButton:
                case OsuAction.RightButton:
                    // Todo: Math.Max() is required as a temporary measure to address https://github.com/ppy/osu-framework/issues/2576
                    downCount = Math.Max(0, downCount - 1);

                    if (downCount == 0)
                        updateExpandedState();
                    break;
            }
        }

        public override bool HandlePositionalInput => true; // OverlayContainer will set this false when we go hidden, but we always want to receive input.

        protected override void PopIn()
        {
            fadeContainer.FadeTo(1, 300, Easing.OutQuint);
            ActiveCursor.ScaleTo(1f, 400, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            fadeContainer.FadeTo(0.05f, 450, Easing.OutQuint);
            ActiveCursor.ScaleTo(0.8f, 450, Easing.OutQuint);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (cosmetics != null)
                cosmetics.CustomisationChanged -= onCustomisationChanged;
            base.Dispose(isDisposing);
        }

        private partial class DefaultCursorTrail : CursorTrail
        {
            [BackgroundDependencyLoader]
            private void load(TextureStore textures)
            {
                Texture = textures.Get(@"Cursor/cursortrail");
                Scale = new Vector2(1 / Texture.ScaleAdjust);
            }
        }
    }
}
