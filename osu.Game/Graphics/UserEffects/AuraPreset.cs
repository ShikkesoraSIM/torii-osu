// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// A particle-aura definition that can be rendered around a username.
    ///
    /// Presets are SELF-DESCRIBING:
    ///  - <see cref="AuraId"/>     — stable string id, matches the server-side
    ///    <c>torii_auras.TORII_AURAS</c> key and the value stored in
    ///    <c>APIUser.equipped_aura</c>.
    ///  - <see cref="OwningGroupIdentifiers"/> — which API group identifiers
    ///    (e.g. "torii-admin") grant a user access to this aura. Used by the
    ///    client-side fallback resolver when a user has no explicit equipped
    ///    pick yet, and as a sanity check that the local preset matches what
    ///    the server says it should.
    ///  - <see cref="DefaultPriority"/> — used to break ties when a user owns
    ///    multiple eligible groups and has not picked explicitly. Lower wins.
    ///    Mirrors the server's <c>default_priority</c> on the same aura id.
    ///
    /// Adding a new aura: create one <see cref="AuraPreset"/> subclass and
    /// register it in <see cref="AuraRegistry.AllPresets"/>. No other client
    /// file needs to change. Display name + description are intentionally NOT
    /// stored here — those come from the server catalog endpoint so they
    /// stay localised and authoritative.
    /// </summary>
    public abstract class AuraPreset
    {
        /// <summary>
        /// Stable identifier for this aura (e.g. "admin-embers"). Used as the
        /// dictionary key in <see cref="AuraRegistry"/>, the value of
        /// <c>APIUser.EquippedAura</c>, and the catalog id from the server.
        /// </summary>
        public abstract string AuraId { get; }

        /// <summary>
        /// API group identifiers (the same strings carried by
        /// <c>APIUserGroup.Identifier</c>, e.g. "torii-admin", "torii-goof")
        /// that grant a user access to this aura. A user is eligible iff at
        /// least one of their groups appears here.
        /// </summary>
        public abstract IReadOnlyList<string> OwningGroupIdentifiers { get; }

        /// <summary>
        /// Tie-breaker for the client-side default-aura fallback. Used only
        /// when <c>APIUser.EquippedAura</c> is null AND the user owns more
        /// than one eligible group — the preset with the lowest priority
        /// wins. Keep aligned with the server's <c>default_priority</c>.
        /// </summary>
        public virtual int DefaultPriority => 100;

        // ---- Particle behaviour tuning. Defaults are sane for a "subtle
        // ambient effect"; presets override to taste. ----

        /// <summary>Average milliseconds between particle spawns.</summary>
        public virtual double SpawnIntervalMs => 280;

        /// <summary>Random extra delay (0..N) added to each spawn.</summary>
        public virtual double SpawnJitterMs => 180;

        /// <summary>Hard cap on simultaneously-alive particles. Bounds worst-case GPU work.</summary>
        public virtual int MaxAlive => 10;

        /// <summary>
        /// Optional persistent layer (added once, never expires) drawn underneath
        /// the particle stream. Useful for pulsing halos, tint overlays, or any
        /// always-on effect that defines the preset's "base mood". Returns null
        /// when the preset only uses transient particles.
        /// </summary>
        /// <remarks>
        /// Prefer <see cref="GlowColour"/> for username decorations — a flat
        /// background drawable produces a visible rectangular halo behind the
        /// bounding box, which reads as a square. <see cref="GlowColour"/>
        /// drives a <see cref="TextShapeGlow"/> that hugs the actual letter
        /// shapes via blur, which is the expected look in 99% of cases.
        /// This hook is left in place for future "non-text" overlay needs
        /// (e.g. an animated frame around an avatar — different shape).
        /// </remarks>
        public virtual Drawable? CreateBackground() => null;

        /// <summary>
        /// Colour of the persistent text-shape glow rendered behind the
        /// username. <see cref="UserAuraContainer"/> mirrors the wrapped
        /// <see cref="osu.Framework.Graphics.Sprites.SpriteText"/> into a
        /// <see cref="TextShapeGlow"/>, so the glow follows the actual letter
        /// outlines rather than a rectangular bounding box.
        /// </summary>
        /// <remarks>
        /// Returns null to opt out (no glow). When non-null, the wrapped
        /// target must be a <see cref="osu.Framework.Graphics.Sprites.SpriteText"/>
        /// for the container to mirror it; non-text targets silently skip the
        /// glow because there's nothing meaningful to mirror.
        /// </remarks>
        public virtual Color4? GlowColour => null;

        /// <summary>
        /// Spawns one particle into <paramref name="parent"/>, sized/positioned
        /// relative to <paramref name="parentSize"/> (the bounding box of the
        /// username being decorated). The spawned drawable is responsible for
        /// expiring itself.
        /// </summary>
        public abstract void EmitParticle(Container parent, Vector2 parentSize, Random random);

        /// <summary>
        /// Per-context particle scale. Maps the parent username's height to a
        /// multiplier presets apply to fixed-pixel sizes (sparks, glyphs,
        /// hearts, etc) so the aura looks proportionate across both tiny
        /// chat rows (≈13px) and large profile headers (≈40px+).
        /// </summary>
        /// <remarks>
        /// Reference height of 30px corresponds to the typical leaderboard /
        /// user-panel username size. Below that we scale down (chat rows
        /// otherwise get visually huge sparks dominating the row); above we
        /// scale up modestly (profile header sparks need to read at distance
        /// without looking puny). Clamp keeps both extremes sane — never
        /// invisible, never overwhelming.
        /// </remarks>
        protected static float ParticleScale(Vector2 parentSize) =>
            Math.Clamp(parentSize.Y / 30f, 0.45f, 1.5f);
    }
}
