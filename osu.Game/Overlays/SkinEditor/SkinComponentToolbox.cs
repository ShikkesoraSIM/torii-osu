// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Rulesets;
using osu.Game.Screens.Edit.Components;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using osu.Framework.Graphics.Sprites;

namespace osu.Game.Overlays.SkinEditor
{
    public partial class SkinComponentToolbox : EditorSidebarSection
    {
        public Action<Type>? RequestPlacement;

        private readonly SkinnableContainer target;

        private readonly RulesetInfo? ruleset;

        // When set, this toolbox lists ONLY Torii-custom components
        // (those implementing IToriiSkinComponent) drawn from BOTH
        // osu.Game and the active ruleset's assembly. Used by the
        // "Torii Exclusive Components" section pinned at the top of
        // the sidebar so users don't have to scroll past the lazer
        // standard set to find the bonus pieces shipped with Torii.
        private readonly bool toriiExclusiveOnly;

        private FillFlowContainer fill = null!;

        /// <summary>
        /// Create a new component toolbox for the specified taget.
        /// </summary>
        /// <param name="target">The target. This is mainly used as a dependency source to find candidate components.</param>
        /// <param name="ruleset">A ruleset to filter components by. If null, only components which are not ruleset-specific will be included.</param>
        public SkinComponentToolbox(SkinnableContainer target, RulesetInfo? ruleset)
            : base(ruleset == null ? SkinEditorStrings.Components : LocalisableString.Interpolate($"{SkinEditorStrings.Components} ({ruleset.Name})"))
        {
            this.target = target;
            this.ruleset = ruleset;
        }

        /// <summary>
        /// Factory for the dedicated Torii section pinned at the top
        /// of the components sidebar. Lists only components flagged
        /// with <see cref="IToriiSkinComponent"/>; the regular
        /// toolboxes filter the same set out so nothing appears twice.
        /// </summary>
        public static SkinComponentToolbox CreateToriiExclusive(SkinnableContainer target, RulesetInfo? ruleset)
            => new SkinComponentToolbox(target, ruleset, toriiOnly: true);

        private SkinComponentToolbox(SkinnableContainer target, RulesetInfo? ruleset, bool toriiOnly)
            : base(toriiOnly
                ? (LocalisableString)@"Torii Exclusive Components"
                : (ruleset == null ? SkinEditorStrings.Components : LocalisableString.Interpolate($"{SkinEditorStrings.Components} ({ruleset.Name})")))
        {
            this.target = target;
            this.ruleset = ruleset;
            toriiExclusiveOnly = toriiOnly;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = fill = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(EditorSidebar.PADDING)
            };

            reloadComponents();
        }

        private void reloadComponents()
        {
            fill.Clear();

            var skinnableTypes = toriiExclusiveOnly
                ? SerialisedDrawableInfo.GetAllToriiSkinComponents(ruleset)
                : SerialisedDrawableInfo.GetAllAvailableDrawables(ruleset);

            foreach (var type in skinnableTypes)
                attemptAddComponent(type);
        }

        private void attemptAddComponent(Type type)
        {
            try
            {
                Drawable instance = (Drawable)Activator.CreateInstance(type)!;

                if (!((ISerialisableDrawable)instance).IsEditable) return;

                fill.Add(new ToolboxComponentButton(instance, target)
                {
                    RequestPlacement = t => RequestPlacement?.Invoke(t),
                    Expanding = contractOtherButtons,
                });
            }
            catch (DependencyNotRegisteredException)
            {
                // This loading code relies on try-catching any dependency injection errors to know which components are valid for the current target screen.
                // If a screen can't provide the required dependencies, a skinnable component should not be displayed in the list.
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Skin component {type} could not be loaded in the editor component list due to an error");
            }
        }

        private void contractOtherButtons(ToolboxComponentButton obj)
        {
            foreach (var b in fill.OfType<ToolboxComponentButton>())
            {
                if (b == obj)
                    continue;

                b.Contract();
            }
        }

        public partial class ToolboxComponentButton : OsuButton
        {
            public Action<Type>? RequestPlacement;
            public Action<ToolboxComponentButton>? Expanding;

            private readonly Drawable component;
            private readonly CompositeDrawable? dependencySource;

            private Container innerContainer = null!;

            private ScheduledDelegate? expandContractAction;

            private const float contracted_size = 60;
            private const float expanded_size = 120;

            public ToolboxComponentButton(Drawable component, CompositeDrawable? dependencySource)
            {
                this.component = component;
                this.dependencySource = dependencySource;

                Enabled.Value = true;

                RelativeSizeAxes = Axes.X;
                Height = contracted_size;
            }

            private const double animation_duration = 500;

            protected override bool OnHover(HoverEvent e)
            {
                expandContractAction?.Cancel();
                expandContractAction = Scheduler.AddDelayed(() =>
                {
                    this.ResizeHeightTo(expanded_size, animation_duration, Easing.OutQuint);
                    Expanding?.Invoke(this);
                }, 100);

                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                base.OnHoverLost(e);

                expandContractAction?.Cancel();
                // If no other component is selected for too long, force a contract.
                // Otherwise we will generally contract when Contract() is called from outside.
                expandContractAction = Scheduler.AddDelayed(Contract, 1000);
            }

            public void Contract()
            {
                // Cheap debouncing to avoid stacking animations.
                // The only place this is nulled is at the end of this method.
                if (expandContractAction == null)
                    return;

                this.ResizeHeightTo(contracted_size, animation_duration, Easing.OutQuint);

                expandContractAction?.Cancel();
                expandContractAction = null;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                BackgroundColour = colourProvider.Background3;

                AddRange(new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding(10) { Bottom = 20 },
                        Masking = true,
                        Child = innerContainer = new DependencyBorrowingContainer(dependencySource)
                        {
                            RelativeSizeAxes = Axes.Both,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Child = component
                        },
                    },
                    createNameLabel(),
                });

                // adjust provided component to fit / display in a known state.
                component.Anchor = Anchor.Centre;
                component.Origin = Anchor.Centre;
            }

            /// <summary>
            /// Bottom-edge label for the component card. For components
            /// flagged with <see cref="IToriiSkinComponent"/> we add a
            /// small torii-gate glyph and tint the name in the brand
            /// vermillion so users can spot Torii-custom additions
            /// among the upstream lazer set without having to read
            /// every class name.
            /// </summary>
            private Drawable createNameLabel()
            {
                bool isTorii = component is IToriiSkinComponent;

                // Same vermillion the ToriiClientBadge uses on user
                // panels — keeps the visual language consistent so a
                // user already knows "vermillion + torii glyph =
                // Torii-specific" from one place to another.
                var torii_red = new Color4(204, 41, 41, 255);

                var nameText = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = component.GetType().Name,
                    Colour = isTorii ? torii_red : Color4.White,
                    Font = isTorii
                        ? OsuFont.Default.With(weight: FontWeight.SemiBold)
                        : OsuFont.Default,
                };

                if (!isTorii)
                {
                    return nameText.With(t =>
                    {
                        t.Anchor = Anchor.BottomCentre;
                        t.Origin = Anchor.BottomCentre;
                        t.Margin = new MarginPadding(5);
                    });
                }

                return new FillFlowContainer
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(5, 0),
                    Margin = new MarginPadding(5),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.ToriiGate,
                            Size = new Vector2(11),
                            Colour = torii_red,
                        },
                        nameText,
                    },
                };
            }

            protected override void UpdateAfterChildren()
            {
                base.UpdateAfterChildren();

                if (component.DrawSize != Vector2.Zero)
                {
                    float bestScale = Math.Min(
                        innerContainer.DrawWidth / component.DrawWidth,
                        innerContainer.DrawHeight / component.DrawHeight);

                    innerContainer.Scale = new Vector2(bestScale);
                }
            }

            protected override bool OnClick(ClickEvent e)
            {
                RequestPlacement?.Invoke(component.GetType());
                return true;
            }
        }

        private partial class DependencyBorrowingContainer : Container
        {
            protected override bool ShouldBeConsideredForInput(Drawable child) => false;

            public override bool PropagateNonPositionalInputSubTree => false;

            private readonly CompositeDrawable? donor;

            public DependencyBorrowingContainer(CompositeDrawable? donor)
            {
                this.donor = donor;
            }

            protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
            {
                var baseDependencies = base.CreateChildDependencies(parent);
                if (donor == null)
                    return baseDependencies;

                var dependencies = new DependencyContainer(donor.Dependencies);
                // inject `SkinEditor` again *on top* of the borrowed dependencies.
                // this is designed to let components know when they are being displayed in the context of the skin editor
                // via attempting to resolve `SkinEditor`.
                dependencies.CacheAs(baseDependencies.Get<SkinEditor>());
                return dependencies;
            }
        }
    }
}
