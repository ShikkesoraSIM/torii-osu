// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Mods
{
    public partial class ModPanel : ModSelectPanel, IFilterable
    {
        public Mod Mod => modState.Mod;
        public override BindableBool Active => modState.Active;

        protected override float IdleSwitchWidth => 54;
        protected override float ExpandedSwitchWidth => 70;

        private readonly ModState modState;

        // rojo de la gate torii (mismo vermillion que usan los auras Founder).
        private static readonly Color4 torii_red = new Color4(255, 80, 60, 255);

        public ModPanel(ModState modState)
        {
            this.modState = modState;

            Title = Mod.Name;
            Description = Mod.Description;

            SwitchContainer.Child = new ModSwitchSmall(Mod)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Active = { BindTarget = Active },
                Shear = -OsuGame.SHEAR,
                Scale = new Vector2(HEIGHT / ModSwitchSmall.DEFAULT_SIZE)
            };
        }

        public ModPanel(Mod mod)
            : this(new ModState(mod))
        {
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            AccentColour = colours.ForModType(Mod.Type);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            modState.ValidForSelection.BindValueChanged(_ => updateFilterState());
            modState.MatchingTextFilter.BindValueChanged(_ => updateFilterState(), true);
            modState.Preselected.BindValueChanged(b =>
            {
                if (b.NewValue)
                {
                    Content.EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = AccentColour,
                        Hollow = true,
                        Radius = 2,
                    };
                }
                else
                    Content.EdgeEffect = default;
            }, true);

            // badge "torii exclusive" con la gate roja para los mods propios de torii.
            if (Mod is IToriiExclusiveMod)
            {
                MainContentContainer.Add(new Container
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    AutoSizeAxes = Axes.Both,
                    Margin = new MarginPadding { Right = 6, Bottom = 4 },
                    Shear = -OsuGame.SHEAR,
                    Child = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(3, 0),
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Icon = FontAwesome.Solid.ToriiGate,
                                Size = new Vector2(9),
                                Colour = torii_red,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            },
                            new OsuSpriteText
                            {
                                Text = "torii exclusive",
                                Font = OsuFont.Default.With(size: 9, weight: FontWeight.SemiBold),
                                Colour = torii_red,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                        },
                    },
                });
            }
        }

        protected override void Select()
        {
            modState.PendingConfiguration = Mod.RequiresConfiguration;
            Active.Value = true;
        }

        protected override void Deselect()
        {
            modState.PendingConfiguration = false;
            Active.Value = false;
        }

        #region Filtering support

        /// <seealso cref="ModState.Visible"/>
        public bool Visible => modState.Visible;

        public override IEnumerable<LocalisableString> FilterTerms => new LocalisableString[]
        {
            Mod.Name,
            Mod.Name.Replace(" ", string.Empty),
            Mod.Acronym,
        };

        public override bool MatchingFilter
        {
            get => modState.MatchingTextFilter.Value;
            set => modState.MatchingTextFilter.Value = value;
        }

        private void updateFilterState()
        {
            this.FadeTo(Visible ? 1 : 0);
        }

        #endregion
    }
}
