// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Cosmetics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Metadata;
using osu.Game.Screens.Play.HUD;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// Wraps a username drawable and renders the user's aura (a particle effect
    /// derived from their groups / equipped cosmetic) BEHIND it.
    ///
    /// Auto-sizes to its child so the particle field tracks the username's
    /// bounding box exactly — useful because usernames change width with text.
    ///
    /// If the user has no aura, this renders just the wrapped child with zero
    /// emission overhead (the emitter is swapped out when the user changes,
    /// not just hidden).
    ///
    /// Respects the <see cref="OsuSetting.UserAuraEnabled"/> setting so users
    /// on weaker hardware can disable the global effect.
    /// </summary>
    public partial class UserAuraContainer : Container
    {
        /// <summary>
        /// Returns the FULL local user (with groups + equipped cosmetics), or
        /// null. Wired once at startup so this decorator can resolve the local
        /// user's aura + name colour even on surfaces that only carry a stripped
        /// user object (a leaderboard score's user has no groups, for example).
        /// </summary>
        public static Func<APIUser?>? LocalUserProvider;

        /// <summary>True when the local user has a name colour equipped — so the
        /// decorator wraps their name even if they have no aura.</summary>
        public static Func<bool>? LocalUserHasNameColour;

        /// <summary>Future "ignore cosmetics" toggle: when it returns true, names
        /// render bare (no colour, glow, or particles). Null = not suppressed.</summary>
        public static Func<bool>? CosmeticsSuppressed;

        /// <summary>Future "reduced motion" toggle: when it returns true, the glow
        /// holds steady (no pulse) and the particle field is hidden, while the
        /// colour itself still applies. Null = full motion.</summary>
        public static Func<bool>? ReducedMotion;

        private APIUser? user;
        private readonly Drawable target;
        private readonly Axes requestedRelativeSizeAxes;

        private ParticleAuraEmitter? emitter;

        // Optional pulsing text-shape glow rendered UNDER the emitter and the
        // target. Only created when the resolved preset opts in via
        // AuraPreset.GlowColour AND the target is a SpriteText we can mirror.
        private TextShapeGlow? textGlow;

        // Optional horizontal flow wrapping the target when the resolved
        // preset declares leading / trailing ornaments (see
        // AuraPreset.CreateLeadingOrnament / CreateTrailingOrnament). When
        // non-null, the glow + emitter positioning in Update() aligns to
        // the target's actual position INSIDE this flow (which the flow
        // shifts right if a leading ornament pushed it) rather than to
        // the wrapper's (0, 0). When null, the target is a direct child
        // of the wrapper and the glow / emitter sit at the wrapper origin.
        private FillFlowContainer? targetFlow;

        // Constrained-width fallback path: when the wrapper has
        // RelativeSizeAxes=X and ornaments exist, we can't use the
        // auto-sized FillFlow path (TruncatingSpriteText needs an
        // explicit parent width). Instead we wrap the target in a
        // padded Container whose Padding.Left / Padding.Right reserves
        // space for the seals (which are added as direct wrapper
        // children, anchored to the wrapper's CentreLeft / CentreRight
        // flanks). The padding visually "pushes" the text right of the
        // leading seal — same behaviour the design wanted from the
        // inline flow — without breaking TruncatingSpriteText sizing.
        private Container? ornamentPaddedTarget;

        // References to the leading + trailing ornament drawables in
        // the constrained-mode path, kept so Update() can reposition
        // them per-frame to hug the actual visible text edges instead
        // of sitting at the column boundaries (which leaves a visible
        // gap between the trailing seal and the text when the text is
        // shorter than the column).
        private Drawable? leadingOrnament;
        private Drawable? trailingOrnament;

        private Bindable<bool> auraEnabled = null!;
        private bool loaded;

        // Resolved if available so we can react to server-side
        // UserUpdated broadcasts (someone else changed their cosmetic
        // payload) and refresh in place. CanBeNull because the wrapper
        // is also used in test scenes / contexts without a metadata hub.
        [Resolved(CanBeNull = true)]
        private MetadataClient? metadataClient { get; set; }

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        // Torii: the local user's equipped name colour, painted onto the wrapped
        // username so it shows in EVERY surface that already wraps for auras
        // (chat, song-select + gameplay leaderboards, user panels, profile).
        // CanBeNull for test scenes / contexts without the cosmetics manager.
        [Resolved(CanBeNull = true)]
        private ToriiCosmeticsManager? cosmetics { get; set; }

        private Bindable<string>? equippedNameColourId;
        private CosmeticNameColour? nameColour;

        // Tracks the most recent fetch we kicked off in response to a
        // remote UserUpdated broadcast, so we can ignore older races
        // when the server fires multiple updates in quick succession.
        private int latestPendingRefreshId;

        /// <summary>
        /// Wrap <paramref name="target"/> with an aura matching <paramref name="user"/>'s groups.
        /// Use the static <see cref="Wrap"/> helper from call-sites for the cleanest one-liner.
        /// </summary>
        /// <param name="user">The user whose aura (if any) should render behind the target.</param>
        /// <param name="target">The drawable (usually a username SpriteText) to decorate.</param>
        /// <param name="relativeSizeAxes">
        /// Axes the wrapper should size relative to its own parent. Defaults
        /// to <see cref="Axes.None"/> (wrapper auto-sizes both axes to the
        /// target's natural size — correct for free-flowing usernames). Pass
        /// <see cref="Axes.X"/> when wrapping a <c>TruncatingSpriteText</c>
        /// that needs a fixed parent width to know where to truncate; the
        /// wrapper then matches the target's RelativeSizeAxes so the
        /// truncation column stays correct.
        /// </param>
        public UserAuraContainer(APIUser? user, Drawable target, Axes relativeSizeAxes = Axes.None)
        {
            this.user = user;
            this.target = target;
            requestedRelativeSizeAxes = relativeSizeAxes;

            // Size policy:
            //   - Wrapper takes the target's relative-size axes (so a
            //     RelativeSizeAxes=X target keeps its width relative to the
            //     real parent transitively).
            //   - Auto-size on whichever axes are NOT relative-sized so the
            //     emitter still has a meaningful (non-zero, non-100%) bounds
            //     to spawn particles within on those axes.
            if (relativeSizeAxes != Axes.None)
            {
                RelativeSizeAxes = relativeSizeAxes;
                AutoSizeAxes = Axes.Both & ~relativeSizeAxes;
            }
            else
            {
                AutoSizeAxes = Axes.Both;
            }
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager? config)
        {
            auraEnabled = config?.GetBindable<bool>(OsuSetting.UserAuraEnabled) ?? new Bindable<bool>(true);

            // Resolve the local user's equipped name colour BEFORE the first
            // build so the glow layer can be tinted to it (matching the profile).
            resolveNameColour();

            // rebuildEmitter handles the full layer stack including the
            // target placement (direct child OR wrapped in an inline
            // FillFlowContainer with leading + trailing ornaments). We
            // mark `loaded` AFTER so the rebuild path knows it's safe
            // to manipulate children directly without racing the
            // BackgroundDependencyLoader pass.
            rebuildEmitter();
            loaded = true;

            // Subscribe to the global aura-change channel so every container
            // currently rendering this user re-resolves the moment the picker
            // confirms a new selection — no need for the user to close/reopen
            // the profile / dashboard / chat panel to see the swap.
            // Comparison is by user ID (not reference) because the same APIUser
            // sometimes gets re-fetched as a fresh instance between contexts;
            // we only care that "this is the same person".
            UserAuraEvents.UserAuraChanged += onUserAuraChanged;

            // Server-side broadcast: another connected user (or this one
            // from a different session) just updated their cosmetic
            // payload. Refresh in place by refetching their public profile.
            if (metadataClient != null)
                metadataClient.UserUpdated += onRemoteUserUpdated;

            // Track the local user's equipped name colour so the text colour AND
            // the glow tint update live in every surface when they change it.
            if (cosmetics != null)
            {
                equippedNameColourId = cosmetics.EquippedNameColourId.GetBoundCopy();
                equippedNameColourId.BindValueChanged(_ =>
                {
                    resolveNameColour();

                    // Only the local user's own decorations depend on the equipped
                    // colour; rebuild just those (not every other wrapper on screen).
                    if (loaded && user != null && api?.LocalUser.Value != null && user.Id == api.LocalUser.Value.Id)
                        rebuildEmitter();
                });
            }
        }

        private void onUserAuraChanged(int changedUserId, string? newEffectiveAuraId)
        {
            if (user == null || user.Id != changedUserId)
                return;

            // Mutate the locally-held APIUser snapshot so a subsequent
            // SetUser call with the same instance still reflects the
            // latest aura, and rebuild the visual to match.
            user.EquippedAura = newEffectiveAuraId;
            rebuildEmitter();
        }

        private void onRemoteUserUpdated(int changedUserId)
        {
            if (user == null || user.Id != changedUserId)
                return;

            // If the broadcast targets the locally-signed-in user, the
            // picker path already mutated their EquippedAura + fired
            // UserAuraEvents — the LocalUser refresh in APIAccess.RefreshLocalUser
            // takes care of badges and the rest. No need to fire a parallel
            // GetUserRequest from every visible container for the same user.
            if (api?.LocalUser.Value != null && api.LocalUser.Value.Id == changedUserId)
                return;

            // Other user: pull a fresh public profile so the new aura +
            // any new groups land on this container without waiting for
            // someone to navigate to the profile manually.
            if (api == null)
                return;

            int requestId = ++latestPendingRefreshId;

            var refreshRequest = new GetUserRequest(changedUserId);
            refreshRequest.Success += refreshed => Schedule(() =>
            {
                if (refreshed == null) return;
                // Stale-response guard: a newer broadcast fired while
                // this request was in flight, so the result is now
                // outdated — drop it and let the newer request win.
                if (requestId != latestPendingRefreshId) return;
                if (user == null || user.Id != changedUserId) return;

                // In-place mutation of the fields we care about. Avoids
                // breaking reference equality for any other consumer
                // still pointing at this APIUser instance, while picking
                // up the latest aura + group membership.
                user.EquippedAura = refreshed.EquippedAura;
                user.Groups = refreshed.Groups;
                rebuildEmitter();
            });
            api.Queue(refreshRequest);
        }

        /// <summary>
        /// Swap the user this container renders an aura for. Safe to call before
        /// or after BDL has run — the emitter is rebuilt accordingly. Useful
        /// when a single header drawable is reused as the active profile changes
        /// (e.g. profile overlay switching between users).
        /// </summary>
        public void SetUser(APIUser? newUser)
        {
            if (ReferenceEquals(newUser, user))
                return;

            user = newUser;
            resolveNameColour();
            if (loaded)
                rebuildEmitter();
        }

        /// <summary>
        /// Walks the ancestor chain looking for a
        /// <see cref="DrawableGameplayLeaderboardScore"/>. When true,
        /// <see cref="rebuildEmitter"/> drops the flanking ornaments so
        /// the in-game leaderboard rows don't carry oversized seals
        /// against tiny truncated usernames. Cheap (typically 5-15 hops)
        /// and only runs at rebuild time, not per frame.
        /// </summary>
        private bool isInsideGameplayLeaderboard()
        {
            for (Drawable? cur = Parent; cur != null; cur = cur.Parent)
            {
                if (cur is DrawableGameplayLeaderboardScore) return true;
            }
            return false;
        }

        private void rebuildEmitter()
        {
            // Tear down all dynamic layers so SetUser swaps + aura changes
            // don't leak old visuals.
            if (emitter != null)
            {
                Remove(emitter, disposeImmediately: true);
                emitter = null;
            }

            if (textGlow != null)
            {
                Remove(textGlow, disposeImmediately: true);
                textGlow = null;
            }

            // Detach target from its current parent (the wrapper
            // directly, the inline FillFlow, or the padded constrained-
            // mode container). Keep the target drawable ALIVE so we
            // can re-attach it under the new preset's layout — calling
            // code may hold references to the target and dispose-on-
            // remove would yank it from under them.
            if (targetFlow != null)
            {
                targetFlow.Remove(target, disposeImmediately: false);
                Remove(targetFlow, disposeImmediately: true);
                targetFlow = null;
            }
            else if (ornamentPaddedTarget != null)
            {
                // Leading + trailing ornaments now live INSIDE the
                // padded container (so they auto-centre vertically on
                // the target). Removing the container disposes them
                // along with itself — we just need to detach the
                // target first to keep it alive for re-attachment.
                ornamentPaddedTarget.Remove(target, disposeImmediately: false);
                Remove(ornamentPaddedTarget, disposeImmediately: true);
                ornamentPaddedTarget = null;
                leadingOrnament = null;
                trailingOrnament = null;
            }
            else if (target.Parent == this)
            {
                Remove(target, disposeImmediately: false);
            }

            // Future "ignore cosmetics" toggle: render the bare username (vanilla
            // look) with no colour, glow or particles.
            if (CosmeticsSuppressed?.Invoke() ?? false)
            {
                Add(target);
                return;
            }

            var preset = AuraRegistry.ResolveForUser(effectiveAuraUser());

            // A role (earned) colour gets a dramatic, additive, pulsing bloom in
            // its own colour so it reads as clearly special, never a flat solid. A
            // buyable solid/gradient stays flat (its only glow, if any, comes from
            // the user's aura). Other users fall back to the aura's glow.
            bool roleGlow = nameColour?.Style == NameColourStyle.Halo;
            Color4? glowTint = roleGlow ? nameColour!.Primary : preset?.GlowColour;

            if (preset == null)
            {
                // No aura. Still glow when the local user has a role colour, then
                // attach the bare target.
                addTextGlow(glowTint, roleGlow);
                Add(target);
                applyEnabledState();
                return;
            }

            // Glow is added FIRST so it sits at the bottom of the z-stack.
            // The TextShapeGlow auto-sizes to its mirror text plus
            // GlowPadding on every side. Position is shifted in Update()
            // to align with the target's actual position (which is non-
            // zero when an inline flow pushes the target right of any
            // leading ornament).
            addTextGlow(glowTint, roleGlow);

            // Decide layout up front so the emitter knows whether to
            // skip its CreateBackground call (which a variant would
            // otherwise paint as a duplicate of its inline ornaments).
            //
            // Inline flow ONLY when the wrapper auto-sizes on X (chat,
            // user panels, profile header, dashboards). In constrained-
            // width wrappers (song-select / gameplay leaderboards via
            // TruncatingSpriteText), stripping the target's
            // RelativeSizeAxes to fit it inside an auto-sized flow
            // makes the text collapse to width-0 because
            // TruncatingSpriteText needs its parent's width to know
            // where to render. We accept the visual compromise (seals
            // flank the text via CreateBackground fallback, with the
            // MaxWidth-clamped emitter keeping them at the visible
            // text edge) over the alternative of invisible usernames.
            // Gameplay leaderboard rows are too cramped (small font + tight
            // row strip) for the flanking seals to read as anything but
            // visual noise — the seal sits at the text centre but its
            // fixed pixel size is oversized vs the surrounding 14-ish px
            // text, and the trailing seal collides with the truncation
            // ellipsis on long names. Suppress ornaments in that surface
            // only; the glow + particle field still ride with the
            // username so the aura is still visible, just without seals.
            bool suppressOrnaments = isInsideGameplayLeaderboard();

            Drawable? leading = suppressOrnaments ? null : preset.CreateLeadingOrnament();
            Drawable? trailing = suppressOrnaments ? null : preset.CreateTrailingOrnament();
            bool useInlineFlow = (leading != null || trailing != null)
                                 && (requestedRelativeSizeAxes & Axes.X) == 0;

            // Emitter (middle layer). Size + position are computed per-frame
            // in Update() — for the wrapped-in-flow case the emitter rides
            // with the target's actual position inside the flow. When the
            // wrapper is handling ornaments (either via inline flow OR the
            // constrained-mode padding path), the emitter is told to
            // SKIP its background hook so the variant's CreateBackground
            // doesn't paint a duplicate set of seals on top of the ones
            // already laid out by the wrapper.
            bool ornamentsHandledByWrapper = leading != null || trailing != null;
            // includeBackground=true falls back to preset.CreateBackground
            // which paints the same flanking seals from another path.
            // Skip it when the wrapper is already handling ornaments inline
            // OR when ornaments are explicitly suppressed (gameplay
            // leaderboard) — otherwise the background path would re-add
            // the seals we just hid.
            Add(emitter = new ParticleAuraEmitter(preset, includeBackground: !ornamentsHandledByWrapper && !suppressOrnaments)
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                BypassAutoSizeAxes = Axes.Both,
            });

            // Finally, attach the target (front layer). When the preset
            // declares inline ornaments AND the wrapper is auto-sizing
            // on X (no RelativeSizeAxes constraint), wrap the target
            // in a horizontal FillFlow so the ornaments push the text
            // right / left and the wrapper's bounding box includes them.
            //
            // Skipping the flow when the wrapper has RelativeSizeAxes=X
            // (TruncatingSpriteText cases) avoids a circular sizing
            // dependency between the flow (auto-sized to children) and
            // the target inside (relative-sized to the flow). Those
            // surfaces render the seals through the CreateBackground
            // fallback path on the emitter instead — same visual,
            // just doesn't participate in the wrapper bounding box.

            if (useInlineFlow)
            {
                var flowChildren = new List<Drawable>();
                if (leading != null) flowChildren.Add(leading);
                flowChildren.Add(target);
                if (trailing != null) flowChildren.Add(trailing);

                targetFlow = new FillFlowContainer
                {
                    Direction = FillDirection.Horizontal,
                    AutoSizeAxes = Axes.Both,
                    // Small inter-element gap so the seal doesn't sit
                    // pixel-perfect against the first / last letter.
                    Spacing = new Vector2(2, 0),
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Children = flowChildren.ToArray(),
                };
                Add(targetFlow);
            }
            else if ((leading != null || trailing != null) && (requestedRelativeSizeAxes & Axes.X) != 0)
            {
                // Constrained-width wrapper with ornaments: can't use
                // FillFlow (TruncatingSpriteText collapses to 0 width
                // without an explicit parent width), so we manually
                // pad the target and place the ornaments as direct
                // children at the wrapper's flanks. The padding shifts
                // the visible text right by the seal width so the
                // leading seal "pushes" the text the way the inline
                // flow does in auto-sized contexts — which is what
                // the design wanted.
                //
                // Generous 22px padding handles the largest seal
                // variant (the heraldic crest at 19px); smaller seals
                // sit with a tiny visual gap that reads as breathing
                // room rather than misalignment. Padding is applied
                // even when the ornament side is null (asymmetry would
                // shift the text off-centre relative to seals on the
                // other side); we only consume padding on sides where
                // an ornament exists.
                const float ornament_padding = 22f;

                // Build the padded target first so we can parent the
                // seals INSIDE it. Putting seals inside the container
                // (instead of as direct wrapper children) is what
                // makes them follow the text's vertical centre on
                // surfaces where the wrapper is taller than the
                // target (e.g. the in-game gameplay leaderboard, where
                // the row strip leaves vertical space around the
                // username and seals anchored to the wrapper's
                // CentreLeft floated to the row's vertical middle —
                // which is BELOW the text — instead of riding with
                // the username baseline).
                ornamentPaddedTarget = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding
                    {
                        Left = leading != null ? ornament_padding : 0,
                        Right = trailing != null ? ornament_padding : 0,
                    },
                    Child = target,
                };
                Add(ornamentPaddedTarget);

                if (leading != null)
                {
                    // Anchor relative to the PADDED container's
                    // content area. CentreLeft + Origin.CentreRight +
                    // X=-2 lands the seal's right edge 2px left of
                    // the content area's left edge — exactly in the
                    // reserved padding strip, immediately before the
                    // first letter. Y at content centre = text centre
                    // because the container auto-sizes its height to
                    // the target.
                    leading.Anchor = Anchor.CentreLeft;
                    leading.Origin = Anchor.CentreRight;
                    leading.Position = new Vector2(-2, 0);
                    ornamentPaddedTarget.Add(leading);
                    leadingOrnament = leading;
                }

                if (trailing != null)
                {
                    // Same parent (so vertical centring matches the
                    // text) but anchored CentreLeft + Origin.CentreLeft;
                    // the per-frame X update in Update() snaps it to
                    // the visible text's right edge + 2px gap so
                    // shorter names don't leave the trailing seal
                    // floating at the column boundary.
                    trailing.Anchor = Anchor.CentreLeft;
                    trailing.Origin = Anchor.CentreLeft;
                    trailing.Position = Vector2.Zero;
                    ornamentPaddedTarget.Add(trailing);
                    trailingOrnament = trailing;
                }
            }
            else
            {
                Add(target);
            }

            applyEnabledState();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            auraEnabled.BindValueChanged(_ => applyEnabledState(), true);
        }

        protected override void Update()
        {
            base.Update();

            // Torii: paint the local user's equipped name colour every frame so it
            // wins over each surface's own colouring + hover transforms. Idempotent
            // for static colours (the Colour setter no-ops when unchanged); animates
            // for the rainbow / pulse styles. The role glow still comes from the
            // aura layer behind the text, so role colours read like the profile.
            if (nameColour != null && target is SpriteText nameText && !(CosmeticsSuppressed?.Invoke() ?? false))
                nameColour.Apply(nameText, Time.Current);

            // Sync the emitter's bounds to the actual rendered text bounds
            // each frame. The glow's Mirror SpriteText auto-sizes to the
            // username text shape regardless of the wrapper's
            // RelativeSizeAxes (since the mirror has no RelativeSizeAxes),
            // so its DrawSize is the natural glyph extent — exactly what
            // we want as the emitter's spawn area. Without this binding
            // the emitter would span the (potentially much wider) wrapper
            // bounds and particles would visibly spawn past the visible
            // text in TruncatingSpriteText cases.
            //
            // Falls back to the wrapper's DrawSize when there is no glow
            // (preset has GlowColour=null, or target isn't a SpriteText).
            // In those cases there's no truncation issue to worry about
            // because if there were a TruncatingSpriteText we'd have a
            // glow to read from too.
            if (emitter == null) return;

            Vector2 spawnSize = textGlow?.Mirror.DrawSize ?? DrawSize;
            if (spawnSize.X <= 0 || spawnSize.Y <= 0) return;

            // Clamp spawn area to the WRAPPER bounds when the wrapper is
            // narrower than the Mirror's natural glyph extent. This is the
            // TruncatingSpriteText case: the visible text is cut to fit
            // the wrapper column, but the Mirror (used for glow + emitter
            // sizing) still reports the full untruncated width, which
            // would push seal/background ornaments past the visible text
            // edge and into adjacent UI columns. Taking the min keeps
            // both seals and particles bounded by what's actually drawn.
            //
            // In the constrained-with-ornaments (padded) path, the text
            // doesn't fill the full wrapper width — it lives in a
            // padded container starting at Padding.Left and ending at
            // wrapper.right - Padding.Right. Subtract those so the
            // clamp matches the visible text area, not the column
            // (otherwise the trailing seal in the bg path would sit
            // at the column edge again, defeating the manual layout).
            float availableTextWidth = DrawWidth;
            if (ornamentPaddedTarget != null)
            {
                availableTextWidth -= ornamentPaddedTarget.Padding.Left
                                    + ornamentPaddedTarget.Padding.Right;
            }
            if (availableTextWidth > 0 && availableTextWidth < spawnSize.X) spawnSize.X = availableTextWidth;
            if (DrawHeight > 0 && DrawHeight < spawnSize.Y) spawnSize.Y = DrawHeight;

            // Sync the glow's MaxWidth too: when the wrapper is
            // narrower, this clips the Mirror inside the glow buffer
            // so the blur halo doesn't extend past the visible text.
            // Setting 0 when wrapper >= Mirror reverts the glow to its
            // natural auto-sized behaviour (no clipping needed).
            if (textGlow != null)
            {
                textGlow.MaxWidth = availableTextWidth > 0
                                    && availableTextWidth < textGlow.Mirror.DrawSize.X
                    ? availableTextWidth
                    : 0f;
            }

            // Track the target's actual position. When the target sits
            // inside an inline FillFlow OR a padded constrained-mode
            // container (both because the preset declared ornaments),
            // its position is shifted right of the wrapper origin by
            // either the flow's leading-ornament width or the padded
            // container's Padding.Left. Re-anchoring the glow + emitter
            // to that shifted position keeps the glow halo on the text
            // and keeps particles spawning over the username letters,
            // not over the leading seal.
            Vector2 textOffset = Vector2.Zero;
            if (targetFlow != null)
            {
                textOffset = targetFlow.Position + target.Position;
            }
            else if (ornamentPaddedTarget != null)
            {
                // Padded container starts at wrapper TopLeft, and the
                // text inside is shifted right by Padding.Left.
                textOffset = new Vector2(ornamentPaddedTarget.Padding.Left, 0);
            }

            if (textGlow != null)
            {
                // Original baseline is -GlowPadding (so the inward
                // Padding inside TextShapeGlow cancels back to (0, 0)).
                // Add the target's flow-assigned offset on top so the
                // glow tracks the text inside the flow too.
                textGlow.Position = textOffset - new Vector2(TextShapeGlow.GlowPadding);
            }

            if (Math.Abs(emitter.Position.X - textOffset.X) > 0.5f
                || Math.Abs(emitter.Position.Y - textOffset.Y) > 0.5f)
            {
                emitter.Position = textOffset;
            }

            // Per-frame trailing-seal positioning in constrained-mode.
            // The trailing seal lives INSIDE ornamentPaddedTarget so
            // its X is relative to the container's content area (which
            // starts at Padding.Left in wrapper coords and ends at
            // wrapper.Right - Padding.Right). We want the seal's left
            // edge 2px past the visible text's right edge, so the
            // text + trailing-seal pair always reads as a tight unit
            // regardless of how short the name is relative to the
            // column width.
            if (trailingOrnament != null && ornamentPaddedTarget != null)
            {
                // spawnSize.X is already the visible text width
                // (clamped above to min(Mirror, availableTextWidth)).
                // No need to add Padding.Left here because the trailing
                // seal's coordinate space starts at the content area.
                float desiredTrailingX = spawnSize.X + 2f;

                if (Math.Abs(trailingOrnament.X - desiredTrailingX) > 0.5f)
                    trailingOrnament.X = desiredTrailingX;
            }

            // Tolerate sub-pixel jitter so we don't thrash Size every
            // frame (which would invalidate emitter children layout).
            if (System.Math.Abs(emitter.DrawWidth - spawnSize.X) > 0.5f
                || System.Math.Abs(emitter.DrawHeight - spawnSize.Y) > 0.5f)
            {
                emitter.Size = spawnSize;
            }
        }

        private void applyEnabledState()
        {
            // Particles hide when the aura setting is off, or under the (future)
            // reduced-motion / ignore-cosmetics toggles.
            bool show = auraEnabled.Value
                        && !(ReducedMotion?.Invoke() ?? false)
                        && !(CosmeticsSuppressed?.Invoke() ?? false);

            if (emitter != null)
                emitter.Alpha = show ? 1 : 0;
        }

        // Resolve the equipped name colour for the wrapped user. For now only the
        // local user has a client-side equipped colour (kept in config); other
        // users will pick theirs up once the server broadcasts it on APIUser.
        private void resolveNameColour()
        {
            nameColour = null;

            var local = api?.LocalUser.Value;
            if (user == null || cosmetics == null || local == null)
                return;

            // Resolve from the FULL local user, not the per-row `user`: role
            // colours (id "name-group-...") need the user's groups to resolve, and
            // the stripped score/leaderboard user object usually has none.
            if (user.Id == local.Id)
                nameColour = CosmeticNameColourCatalog.GetById(cosmetics.EquippedNameColourId.Value, local);
        }

        // The local user's per-row object on most surfaces (chat, leaderboards,
        // score panels) is a stripped APIUser with no groups, so its aura can't
        // resolve. When the wrapped user IS the local user, resolve the aura from
        // the FULL local user instead, so it shows everywhere their name appears
        // (not only on the profile, which already holds the full object).
        private APIUser? effectiveAuraUser()
        {
            var local = api?.LocalUser.Value;
            if (local != null && user != null && user.Id == local.Id)
                return local;

            return user;
        }

        // Adds the letter-hugging glow behind the target, tinted to <paramref
        // name="tint"/>. For the local user's name colour it blooms additively so
        // it reads like the profile name; the aura's own glow keeps its softer
        // normal blend.
        private void addTextGlow(Color4? tint, bool dramatic)
        {
            if (tint is not Color4 glowColour || target is not SpriteText spriteText)
                return;

            bool reduced = ReducedMotion?.Invoke() ?? false;

            // Role glow: push the colour toward white so the additive bloom reads
            // as a bright "colour blurred with white" halo, clearly distinct from a
            // flat solid. Other glows keep their own colour.
            Color4 glow = dramatic
                ? new Color4(
                    glowColour.R + (1f - glowColour.R) * 0.5f,
                    glowColour.G + (1f - glowColour.G) * 0.5f,
                    glowColour.B + (1f - glowColour.B) * 0.5f,
                    1f)
                : glowColour;

            // TextShapeGlow now blooms additively internally (the GlowingDrawable
            // pipeline the toolbar/profile use); we just feed it the tint + the
            // pulse shape. A deeper blur + pulse swing makes role colours exaggerated.
            Add(textGlow = new TextShapeGlow(spriteText.Text, spriteText.Font, glow)
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-TextShapeGlow.GlowPadding),
                BypassAutoSizeAxes = Axes.Both,
                BlurSigma = dramatic ? new Vector2(5f) : new Vector2(4f),
                MaxAlpha = dramatic ? 1f : 0.9f,
                MinAlpha = dramatic ? 0.45f : 0.5f,
                DurationMs = dramatic ? 700 : 1500,
                Pulsate = !reduced,
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            // Static event reference would otherwise pin this container in
            // memory for the rest of the process lifetime. Critical because
            // chat lines / leaderboard rows churn through hundreds of
            // wrapper instances during a session.
            UserAuraEvents.UserAuraChanged -= onUserAuraChanged;

            if (metadataClient != null)
                metadataClient.UserUpdated -= onRemoteUserUpdated;

            equippedNameColourId?.UnbindAll();

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// Convenience helper: returns the original drawable when the user has
        /// no aura (so we don't pay any wrapping cost), otherwise returns a new
        /// <see cref="UserAuraContainer"/>. Anchor/origin of the original
        /// drawable are MOVED to the wrapper (and reset to TopLeft on the
        /// target) so:
        ///   - external layout sees the wrapper at the same effective position
        ///     as the bare drawable would have been.
        ///   - the target lays out naturally as a top-left child inside the
        ///     auto-sized wrapper, which is the only configuration that plays
        ///     nicely with <c>AutoSizeAxes = Axes.Both</c>. Leaving the target
        ///     at e.g. <c>Anchor.CentreLeft</c> caused the wrapper's auto-size
        ///     calculation to misbehave (and in some panels, throw outright).
        /// </summary>
        // Decide whether a username needs decorating. Other users: only if they
        // have a resolvable aura. The local user: if they have an aura (resolved
        // from their FULL data, which a surface's stripped per-row user object may
        // lack) OR an equipped name colour to paint.
        private static bool shouldDecorate(APIUser? user)
        {
            if (AuraRegistry.ResolveForUser(user) != null)
                return true;

            var local = LocalUserProvider?.Invoke();
            if (local != null && user != null && user.Id == local.Id)
                return AuraRegistry.ResolveForUser(local) != null || (LocalUserHasNameColour?.Invoke() ?? false);

            return false;
        }

        public static Drawable Wrap(APIUser? user, Drawable target)
        {
            if (!shouldDecorate(user))
                return target;

            var anchor = target.Anchor;
            var origin = target.Origin;
            // Pull the target's RelativeSizeAxes onto the wrapper so we
            // preserve any X-relative / Y-relative sizing the layout above
            // expected. Without this, wrapping a TruncatingSpriteText (which
            // is RelativeSizeAxes=X to know how wide to truncate) would
            // collapse to zero width because the wrapper auto-sizes to a
            // child that's trying to be 100% of the wrapper.
            var relativeSizeAxes = target.RelativeSizeAxes;

            // Pull Shear too. The slanted song-select leaderboard row sits
            // inside a sheared container and its username SpriteText carries
            // a counter-shear (-OsuGame.SHEAR) to render upright. If we
            // don't preserve that on the wrapper, the wrapper itself stays
            // sheared by the parent and the glow / emitter children inside
            // it render sheared — while the target text (still carrying its
            // counter-shear) renders upright. Result: visible misalignment
            // in the slanted leaderboard. Move the shear up to the wrapper
            // and reset it on the target so the wrapper renders upright AND
            // every child inside it (target, glow, emitter) shares the same
            // unsheared coordinate system.
            var shear = target.Shear;

            // Reset anchor/origin/shear on the inner target so AutoSize on
            // the wrapper sees a top-left-anchored, zero-shear child it can
            // size to predictably. RelativeSizeAxes stays on the target so
            // it can continue to fill the wrapper transitively.
            target.Anchor = Anchor.TopLeft;
            target.Origin = Anchor.TopLeft;
            target.Shear = Vector2.Zero;

            return new UserAuraContainer(user, target, relativeSizeAxes)
            {
                Anchor = anchor,
                Origin = origin,
                Shear = shear,
            };
        }
    }
}
