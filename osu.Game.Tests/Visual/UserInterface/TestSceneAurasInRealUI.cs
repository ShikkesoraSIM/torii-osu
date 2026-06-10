// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserEffects.Presets;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Overlays;
using osu.Game.Overlays.Chat;
using osu.Game.Overlays.Profile;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.HUD;
using osu.Game.Screens.Select.Leaderboards;
using osu.Game.Screens.SelectV2;
using osu.Game.Users;
using osuTK;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Real-context aura review scene. Wires up production drawables (the
    /// V2 song-select leaderboard row, the in-game gameplay leaderboard,
    /// real <see cref="ChatLine"/>s, all four user panels, and the full
    /// <see cref="ProfileHeader"/>) with fake users carrying each
    /// <see cref="AuraPreset"/>'s owning group identifier — so the aura
    /// code path actually fires per-row.
    ///
    /// Why bother: existing per-component test scenes
    /// (<c>TestSceneBeatmapLeaderboardScore</c>, <c>TestSceneChatOverlay</c>,
    /// etc) only feed generic users with no staff groups, so the aura code
    /// path never runs in those scenes and there is no place to compare
    /// "all 6 presets at once on a real surface". This scene closes that
    /// gap and adds a width slider so we can resize rows to see full names.
    /// </summary>
    [TestFixture]
    public partial class TestSceneAurasInRealUI : OsuTestScene
    {
        [Cached]
        private OverlayColourProvider colourProvider { get; set; } = new OverlayColourProvider(OverlayColourScheme.Aquamarine);

        // ---------- Personas ---------------------------------------------
        // One per aura preset, plus a baseline plain user. Every context
        // renders exactly this list so a side-by-side comparison across
        // presets is consistent.
        //
        // Group `colour_hex` is what `ToriiColourHelper.GetTopColour` reads
        // when DrawableChatUsername wants to colour the chat name. v1 of
        // this scene used grey "#888888" placeholders which made the chat
        // names render uniformly grey — fixed here by giving each preset a
        // distinct on-brand hex pulled from its glow palette.
        private readonly struct Persona
        {
            public readonly string Username;
            public readonly string? GroupKey;
            public readonly string? GroupName;       // tooltip text on the group badge
            public readonly string? GroupShortName;  // text rendered IN the badge (e.g. "ADM")
            public readonly string? GroupColourHex;
            // Optional playmode payload on the synthesised APIUserGroup.
            // Only set for personas representing the per-mode Consul auras
            // (advisor-osu / advisor-taiko / …) where multiple keys share
            // the same client identifier "torii-advisor" and the matching
            // ConsulAuraPreset uses RequiredPlaymodes to discriminate
            // which of the four renders. Defaults to null so adding this
            // field doesn't disturb the existing baseline personas.
            public readonly string[]? GroupPlaymodes;
            // Optional UserId override for personas that exercise
            // server-side auto-detected entitlement (currently: Founder
            // for user.id <= 100). When null, the test scene's per-row
            // synthetic id is used. We set this to a small int (e.g. 7)
            // for the Founder persona so the local resolver receives a
            // user payload that would be eligible if the server ran the
            // same check — the actual eligibility on the client side
            // still flows through the group (torii-founder), this is
            // just an honest-looking value.
            public readonly int? IdOverride;
            // Optional explicit AuraId. When set, the makeFakeUser
            // helper sets APIUser.EquippedAura to this string so the
            // registry's Path 1 resolution (explicit equipped pick)
            // returns the matching preset instead of the group-fallback
            // default. Used by the Founder-variant personas to force
            // each variant's preset to render even though they all
            // share the same "torii-founder" group and would otherwise
            // tie in the fallback resolver.
            public readonly string? EquippedAura;

            public Persona(string username, string? groupKey, string? groupName, string? groupShortName, string? groupColourHex,
                           string[]? groupPlaymodes = null, int? idOverride = null, string? equippedAura = null)
            {
                Username = username;
                GroupKey = groupKey;
                GroupName = groupName;
                GroupShortName = groupShortName;
                GroupColourHex = groupColourHex;
                GroupPlaymodes = groupPlaymodes;
                IdOverride = idOverride;
                EquippedAura = equippedAura;
            }
        }

        private static readonly Persona[] personas =
        {
            // PlainPlayer has no group → no aura, no badge, baseline for comparison.
            new Persona("PlainPlayer",    null,              null,                 null,    null),
            // Group display Names mirror what appears in real osu! profile tooltips
            // — short readable labels rather than the raw "torii-*" identifier
            // (which is an internal vocabulary). ShortName = the 2-3 char pill text.
            new Persona("Shikkesora",     "torii-admin",     "Administrator",      "ADM",   "FF8C70"),
            new Persona("Imperation",     "torii-dev",       "Developer",          "DEV",   "78DCFF"),
            new Persona("Boreas",         "torii-mod",       "Moderator",          "MOD",   "FFD266"),
            new Persona("Mash39",         "torii-qat",       "Beatmap Nominator",  "QAT",   "5AE0C0"),
            new Persona("NahuelSupports", "torii-supporter", "Torii Supporter",    "SUP",   "FF7FC8"),
            new Persona("GoofGuy",        "torii-goof",      "Goofball",           "GOOF",  "9CE5A0"),
            // ---- Phase 1 additions -------------------------------------------
            // Bug Finder — already in production but no persona was here. Add
            // for full coverage of the existing aura roster.
            new Persona("BugReporter",    "torii-bug-finder", "Bug Finder",        "BUG",   "8CE0C5"),
            // Founder — torii-themed, vermillion + gold. Server side this
            // would be granted via user.id <= 100; here we synthesise the
            // group directly + give the persona a low IdOverride so the
            // full surface visually matches a real founder user.
            new Persona("OGOne",          "torii-founder",   "Founder",           "FND",   "FF6B5A",
                        idOverride: 7),
            // Feature Architect (May 2026 Cohort) — blueprint cyan + gold.
            // Granted manually in production; persona here just shows what
            // it looks like in the UI.
            new Persona("FeatureCrafter", "torii-feature-architect-2026-06",
                                          "Feature Architect (May 2026 Cohort)",   "FA26",  "4DD0E1"),
            // Per-mode Consuls — all share identifier "torii-advisor"; the
            // GroupPlaymodes field is what makes each ConsulAuraPreset's
            // RequiredPlaymodes filter pick the right one of the four.
            new Persona("OsuConsulPersona",   "torii-advisor", "osu! Advisor",     "ADV",   "FF66AA",
                        groupPlaymodes: new[] { "osu" }),
            new Persona("TaikoConsulPersona", "torii-advisor", "Taiko Advisor",    "ADV",   "FF6B35",
                        groupPlaymodes: new[] { "taiko" }),
            new Persona("CatchConsulPersona", "torii-advisor", "Catch Advisor",    "ADV",   "26C6A6",
                        groupPlaymodes: new[] { "fruits" }),
            new Persona("ManiaConsulPersona", "torii-advisor", "Mania Advisor",    "ADV",   "E91E8C",
                        groupPlaymodes: new[] { "mania" }),
            // ---- Founder design variants ------------------------------------
            // Each variant persona reuses the torii-founder group so
            // production entitlement logic accepts the aura, then
            // pins EquippedAura to that variant's specific AuraId so
            // the registry's Path-1 resolver returns the right preset.
            // Cycle through these in the AllSurfaces context to see
            // each variant in every UI side-by-side with the others.
            new Persona("V1_ImperialGold",  "torii-founder", "Founder", "FND", "FFCE66",
                        idOverride: 1,  equippedAura: FounderImperialGoldPreset.ID),
            new Persona("V2_SakuraGarden",  "torii-founder", "Founder", "FND", "FF9EC3",
                        idOverride: 2,  equippedAura: FounderSakuraGardenPreset.ID),
            new Persona("V3_LacqueredBox",  "torii-founder", "Founder", "FND", "DCAA50",
                        idOverride: 3,  equippedAura: FounderLacqueredBoxPreset.ID),
            new Persona("V4_SunrisePillar", "torii-founder", "Founder", "FND", "FFBC64",
                        idOverride: 4,  equippedAura: FounderSunrisePillarPreset.ID),
            new Persona("V5_CrestOfHonor",  "torii-founder", "Founder", "FND", "FFD26E",
                        idOverride: 5,  equippedAura: FounderCrestOfHonorPreset.ID),
        };

        // Extra non-staff people used ONLY in the chat view to give the
        // chat a more realistic "mix of regular users and staff" look.
        private static readonly Persona[] extraChatPlayers =
        {
            new Persona("Aluvi",     null, null, null, null),
            new Persona("Mochi42",   null, null, null, null),
            new Persona("CherryBomb", null, null, null, null),
            new Persona("snail",     null, null, null, null),
        };

        // ---------- State + step / slider wiring -------------------------

        private enum Ctx
        {
            SlantedLeaderboard,
            PlainLeaderboard,
            GameplayLeaderboard,
            Chat,
            UserPanels,
            ProfileHeader,
            // "All surfaces for a single persona" — stacks every UI that
            // can host a username (chat, leaderboard, gameplay strip,
            // user panels) into one scrollable view, so you can verify
            // at a glance that ONE aura reads correctly across EVERY
            // place it might appear. Cycle which persona is being
            // displayed with the per-context "next persona" step.
            AllSurfaces,
        }

        private Ctx currentCtx = Ctx.SlantedLeaderboard;

        // Width fraction of the test viewport for surfaces that scale —
        // leaderboards + panels + chat. Profile header / gameplay LB stay
        // at their natural width because they don't visually "stretch"
        // sensibly with this slider.
        private float widthFactor = 0.6f;

        public TestSceneAurasInRealUI()
        {
            AddStep("Slanted song-select leaderboard (V2 sheared)", () => set(Ctx.SlantedLeaderboard));
            AddStep("Plain leaderboard (V2 not sheared)",            () => set(Ctx.PlainLeaderboard));
            AddStep("In-game gameplay leaderboard",                  () => set(Ctx.GameplayLeaderboard));
            AddStep("Chat (mixed staff + regular players)",          () => set(Ctx.Chat));
            AddStep("User panels (Brick / Grid / List / Rank)",      () => set(Ctx.UserPanels));
            AddStep("Profile header (per-preset cycle)",             () => set(Ctx.ProfileHeader));
            AddStep("» All surfaces (single persona, per-preset cycle)", () => set(Ctx.AllSurfaces));

            // Width slider rebuilds the active context so the change is
            // visible immediately — handy for verifying the aura survives
            // truncation at narrow widths and bleeds correctly at wider
            // ones.
            AddSliderStep("content width %", 0.25f, 1.0f, 0.6f, v =>
            {
                widthFactor = v;
                rebuild();
            });

            rebuild();
        }

        private void set(Ctx c)
        {
            currentCtx = c;
            rebuild();
        }

        private void rebuild()
        {
            Clear();
            switch (currentCtx)
            {
                case Ctx.SlantedLeaderboard: buildLeaderboard(sheared: true); break;
                case Ctx.PlainLeaderboard:   buildLeaderboard(sheared: false); break;
                case Ctx.GameplayLeaderboard: buildGameplayLeaderboard(); break;
                case Ctx.Chat:               buildChat(); break;
                case Ctx.UserPanels:         buildUserPanels(); break;
                case Ctx.ProfileHeader:      buildProfileHeader(); break;
                case Ctx.AllSurfaces:        buildAllSurfaces(); break;
            }
        }

        // ---------- User / score factory ---------------------------------

        private static APIUser makeFakeUser(Persona p, int id) => new APIUser
        {
            // Honour the persona's IdOverride when provided so personas
            // representing id-gated entitlements (Founder = id <= 100)
            // present a believable id to anything that reads it. Default
            // path uses the test-scene's per-row synthetic id for stable
            // diffability across runs.
            Id = p.IdOverride ?? id,
            Username = p.Username,
            // Explicit equipped aura, when set, makes the registry's
            // Path 1 resolver return THIS preset for the user. Used
            // by the Founder-variant personas to disambiguate between
            // the five variant presets that all match the same group.
            EquippedAura = p.EquippedAura,
            CountryCode = CountryCode.AR,
            // Chat name colour (and many other places) reads `Colour`
            // directly when there is no group; setting it here so plain
            // users still get a distinguishable name colour rather than
            // pure white.
            Colour = p.GroupColourHex ?? defaultPlainColour(p.Username),
            Groups = p.GroupKey == null
                ? null
                : new[]
                {
                    new APIUserGroup
                    {
                        Identifier = p.GroupKey,
                        // Display Name surfaces in the GroupBadge tooltip when
                        // hovering the pill in profile / user panels. Use the
                        // human-readable label ("Administrator", not the raw
                        // identifier) so tooltips in this test scene look the
                        // same as real users.
                        Name = p.GroupName!,
                        // ShortName is the 2-3 char text rendered INSIDE the
                        // pill ("ADM", "MOD", "QAT" ...). Without it the
                        // badge renders an empty pill.
                        ShortName = p.GroupShortName!,
                        // `GetTopColour` reads this — it's what colours the
                        // chat name. Distinct hex per preset so the chat view
                        // also visually surfaces "which staff group".
                        Colour = "#" + p.GroupColourHex,
                        // Per-mode advisor entries carry a Playmodes array
                        // that's what the ConsulAuraPreset.RequiredPlaymodes
                        // filter matches against. HasPlaymodes mirrors the
                        // server-side field so the badge UI uses the right
                        // layout for groups with mode tags.
                        Playmodes = p.GroupPlaymodes,
                        HasPlaymodes = p.GroupPlaymodes != null && p.GroupPlaymodes.Length > 0,
                    },
                },
            // Statistics so UserRankPanel / similar render numbers instead
            // of dashes.
            Statistics = new UserStatistics { GlobalRank = 4204 + id, CountryRank = 12 + (id % 30) },
            CoverUrl = null, // skip remote cover loading in tests
        };

        // Stable-but-varied colours for plain users so the chat doesn't
        // look monochrome when nobody has a staff group.
        private static string defaultPlainColour(string seed)
        {
            // Tiny string hash to map to one of a handful of pleasant tones.
            int h = 0;
            foreach (char c in seed) h = h * 31 + c;
            string[] palette =
            {
                "B2B2C8", "DDB892", "9DCBE2", "C4D9A4", "E0A5C9", "F2C57C",
            };
            return palette[Math.Abs(h) % palette.Length];
        }

        private static ScoreInfo makeFakeScore(Persona p, int id, int position) => new ScoreInfo
        {
            Position = position,
            Rank = position == 1 ? ScoreRank.X : position <= 3 ? ScoreRank.S : ScoreRank.A,
            Accuracy = 0.99f - position * 0.005f,
            MaxCombo = 1500 - position * 80,
            TotalScore = (long)(2_000_000 - position * 110_000 + RNG.Next(-30_000, 30_000)),
            MaximumStatistics = { { HitResult.Great, 3000 } },
            Ruleset = new OsuRuleset().RulesetInfo,
            User = makeFakeUser(p, id),
            Date = DateTimeOffset.Now.AddDays(-position),
        };

        // ---------- Builders ---------------------------------------------

        private void buildLeaderboard(bool sheared)
        {
            var fillFlow = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                Width = widthFactor,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(0, 2),
                // Sheared parent only on the song-select variant, matching
                // production layout.
                Shear = sheared ? OsuGame.SHEAR : Vector2.Zero,
                Children = personas
                           .Select((p, idx) =>
                           {
                               var score = makeFakeScore(p, 1000 + idx, idx + 1);
                               return (Drawable)new BeatmapLeaderboardScore(score, sheared)
                               {
                                   Rank = score.Position,
                                   Shear = sheared ? Vector2.Zero : Vector2.Zero,
                               };
                           })
                           .ToArray(),
            };

            Add(new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new OsuContextMenuContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = fillFlow,
                },
            });
        }

        private void buildGameplayLeaderboard()
        {
            // Gameplay leaderboard rows have a fixed natural width (250) in
            // production — it's a HUD strip, not a scaling panel. The width
            // slider deliberately doesn't affect this view.
            Add(new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Y,
                Width = 250,
                Spacing = new Vector2(0, 6),
                Direction = FillDirection.Vertical,
                Children = personas
                           .Select((p, idx) =>
                           {
                               var user = makeFakeUser(p, 2000 + idx);
                               var displayScore = new BindableLong(2_000_000 - idx * 100_000);
                               var score = new GameplayLeaderboardScore(user, tracked: idx == 0, displayScore)
                               {
                                   Position = { Value = idx + 1 },
                               };
                               return (Drawable)new DrawableGameplayLeaderboardScore(score)
                               {
                                   Expanded = { Value = true },
                                   RelativeSizeAxes = Axes.X,
                               };
                           })
                           .ToArray(),
            });
        }

        private void buildChat()
        {
            // Interleave staff + plain users so the chat reads like a
            // realistic mixed channel rather than 7 staff members in a row.
            var chatOrder = new List<Persona>();
            chatOrder.Add(personas[0]);                    // PlainPlayer
            chatOrder.Add(extraChatPlayers[0]);            // Aluvi
            chatOrder.Add(personas[1]);                    // Shikkesora (admin)
            chatOrder.Add(extraChatPlayers[1]);            // Mochi42
            chatOrder.Add(personas[2]);                    // Imperation (dev)
            chatOrder.Add(personas[3]);                    // Boreas (mod)
            chatOrder.Add(extraChatPlayers[2]);            // CherryBomb
            chatOrder.Add(personas[4]);                    // Mash39 (qat)
            chatOrder.Add(personas[5]);                    // NahuelSupports
            chatOrder.Add(extraChatPlayers[3]);            // snail
            chatOrder.Add(personas[6]);                    // GoofGuy

            string[] snippets =
            {
                "yo",
                "anyone up for a couple of multi rooms",
                "gg, that map is brutal",
                "lmaoooo what was that miss",
                "i found a bug in beatmap submission btw",
                "ok ranking your map now, give me 5",
                "this song slaps",
                "lol",
                "thanks for the supporter tier <3",
                "did anyone else just lag spike?",
                "pls don't ask me to mod ranked maps :)",
            };

            Add(new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                Width = widthFactor,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(0, 4),
                Padding = new MarginPadding(20),
                Children = chatOrder
                           .Select((p, idx) =>
                           {
                               var sender = makeFakeUser(p, 3000 + idx);
                               var message = new Message(idx + 1)
                               {
                                   Content = snippets[idx % snippets.Length],
                                   Sender = sender,
                                   Timestamp = DateTimeOffset.Now.AddSeconds(-idx),
                               };
                               return (Drawable)new ChatLine(message);
                           })
                           .ToArray(),
            });
        }

        private void buildUserPanels()
        {
            // Sizing: RelativeSizeAxes.X (NOT AutoSize.X) plus
            // AutoSizeAxes.Y. The list-row child below is
            // RelativeSizeAxes.X because UserListPanel stretches to its
            // parent's width — that creates a circular dependency if the
            // outer flow is AutoSize.X (parent waits on child, child
            // waits on parent), which the framework silently collapses
            // to zero width, hiding the list row entirely and breaking
            // the height measurement so the scroll never extends past
            // the viewport. Pinning the outer flow's X to its parent
            // column breaks the cycle while still letting the brick /
            // grid / rank rows auto-size their own X within it.
            //
            // TopCentre anchor + origin keeps content top-aligned so the
            // parent's AutoSize.Y measurement matches the flow's actual
            // bottom edge instead of mis-measuring from a centred origin.
            var flow = new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(0, 12),
                Direction = FillDirection.Vertical,
            };

            // Brick — compact horizontal pill
            flow.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(6),
                Direction = FillDirection.Horizontal,
                Children = personas.Select((p, idx) =>
                    (Drawable)new UserBrickPanel(makeFakeUser(p, 4000 + idx))).ToArray(),
            });

            // Grid — square card
            flow.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(8),
                Direction = FillDirection.Horizontal,
                Children = personas.Select((p, idx) =>
                    (Drawable)new UserGridPanel(makeFakeUser(p, 4100 + idx)) { Width = 220 }).ToArray(),
            });

            // List — wide horizontal row, scales with the width slider
            flow.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Spacing = new Vector2(0, 4),
                Direction = FillDirection.Vertical,
                Children = personas.Select((p, idx) =>
                    (Drawable)new UserListPanel(makeFakeUser(p, 4200 + idx))).ToArray(),
            });

            // Rank panel
            flow.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(8),
                Direction = FillDirection.Horizontal,
                Children = personas.Select((p, idx) =>
                    (Drawable)new UserRankPanel(makeFakeUser(p, 4300 + idx)) { Width = 280 }).ToArray(),
            });

            Add(new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new Container
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Y,
                    RelativeSizeAxes = Axes.X,
                    Width = widthFactor,
                    Padding = new MarginPadding(20),
                    Child = flow,
                },
            });
        }

        // For the profile header context we cycle through one persona at a
        // time (it's a giant per-user header, doesn't make sense to render
        // 7 stacked). Use a separate AddStep to flip personas inside this
        // context. Default: Shikkesora (admin red, the most-tested case).
        private int profilePersonaIndex = 1;

        private void buildProfileHeader()
        {
            var p = personas[profilePersonaIndex];
            var header = new ProfileHeader();

            Add(new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = header,
            });

            header.User.Value = new UserProfileData(makeFakeUser(p, 5000 + profilePersonaIndex), new OsuRuleset().RulesetInfo);

            // Add a step (only in this context) so the user can cycle which
            // persona the header shows. We only add it once per build to
            // avoid a forever-growing step list.
            AddStep("» next persona on header", () =>
            {
                profilePersonaIndex = (profilePersonaIndex + 1) % personas.Length;
                rebuild();
            });
        }

        // Separate persona index for the AllSurfaces context so cycling
        // here doesn't interfere with the ProfileHeader cycle. Defaults
        // to the first Founder VARIANT persona (V1 Imperial Gold)
        // because that's the current active design-review target —
        // cycling "next" walks through V1 → V2 → V3 → V4 → V5 →
        // wraps back to PlainPlayer. Adjust the starting index when
        // a different family of personas becomes the review focus.
        private int allSurfacesPersonaIndex = 14; // V1_ImperialGold

        // Renders ONE persona across EVERY UI surface that can host a
        // username, stacked vertically inside a scroll container. The
        // point of this view is "verify a single aura reads correctly
        // in every place it'll ever appear" without having to switch
        // between contexts (which resets particle state and breaks
        // continuity). The other per-context views still exist for
        // zoom-in debugging of one surface in isolation.
        private void buildAllSurfaces()
        {
            var hero = personas[allSurfacesPersonaIndex];

            // A short list of filler plain personas so leaderboard /
            // chat sections don't read as "one person alone" — the hero
            // persona always sits at position 1 (top of leaderboards,
            // first chat line) so the reviewer's eye lands on the aura
            // first.
            var filler = new[] { extraChatPlayers[0], extraChatPlayers[1], extraChatPlayers[2] };

            var flow = new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Width = widthFactor,
                Padding = new MarginPadding { Top = 14, Bottom = 24, Horizontal = 12 },
                Spacing = new Vector2(0, 18),
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    // Hero label so the reviewer knows whose aura they're
                    // currently inspecting. Uses the persona's group
                    // colour so the label visually previews the aura's
                    // palette before the surfaces even render.
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = "Showing: " + hero.Username,
                                Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                                Colour = Colour4.White,
                            },
                            new OsuSpriteText
                            {
                                Text = hero.GroupKey == null
                                    ? "(no group, baseline plain user)"
                                    : "group: " + hero.GroupKey
                                      + (hero.GroupPlaymodes != null && hero.GroupPlaymodes.Length > 0
                                          ? "  •  playmode: " + string.Join(",", hero.GroupPlaymodes)
                                          : string.Empty),
                                Font = OsuFont.GetFont(size: 11),
                                Colour = Colour4.White.Opacity(0.55f),
                            },
                        },
                    },

                    sectionHeader("Chat — three lines from the persona, mixed with regulars"),
                    buildAllSurfacesChat(hero, filler),

                    sectionHeader("Song-select leaderboard — hero at position 1, sheared variant"),
                    buildAllSurfacesLeaderboard(hero, filler, sheared: true),

                    sectionHeader("Song-select leaderboard — hero at position 1, plain variant"),
                    buildAllSurfacesLeaderboard(hero, filler, sheared: false),

                    sectionHeader("In-game gameplay leaderboard — hero at top"),
                    buildAllSurfacesGameplayLeaderboard(hero, filler),

                    sectionHeader("User panels — Brick / Grid / List / Rank (dashboard placements)"),
                    buildAllSurfacesUserPanels(hero),

                    sectionHeader("Profile header — the full-page hero card"),
                    buildAllSurfacesProfileHeader(hero),
                },
            };

            Add(new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = flow,
            });

            // Cycle step — bound here so it's only added in this
            // context (avoids cluttering the step list when the
            // reviewer is in other views).
            AddStep("» next persona across surfaces", () =>
            {
                allSurfacesPersonaIndex = (allSurfacesPersonaIndex + 1) % personas.Length;
                rebuild();
            });
        }

        // Small section divider with a label. Used between each
        // surface-block in the AllSurfaces view so the reviewer can
        // tell at a glance which UI a particular row belongs to.
        private static Drawable sectionHeader(string label) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Y,
            RelativeSizeAxes = Axes.X,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 4),
            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Text = label,
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                    Colour = Colour4.White.Opacity(0.55f),
                },
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Colour = Colour4.White.Opacity(0.10f),
                },
            },
        };

        private static Drawable buildAllSurfacesChat(Persona hero, Persona[] filler)
        {
            // Three lines: hero, filler, hero — gives two looks at the
            // aura in chat density and verifies it doesn't visually
            // bleed into adjacent rows.
            string[] heroLines = { "yo, just landed a clean play", "anyone wanna multi after this map" };
            string[] fillerLines = { "gg, that one was rough", "lmao what was that miss" };

            (Persona persona, string text)[] order =
            {
                (hero, heroLines[0]),
                (filler[0], fillerLines[0]),
                (hero, heroLines[1]),
                (filler[1], fillerLines[1]),
            };

            return new FillFlowContainer
            {
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 3),
                Children = order.Select((entry, idx) =>
                {
                    var sender = makeFakeUser(entry.persona, 6000 + idx);
                    var message = new Message(idx + 1)
                    {
                        Content = entry.text,
                        Sender = sender,
                        Timestamp = DateTimeOffset.Now.AddSeconds(-idx),
                    };
                    return (Drawable)new ChatLine(message);
                }).ToArray(),
            };
        }

        private static Drawable buildAllSurfacesLeaderboard(Persona hero, Persona[] filler, bool sheared)
        {
            // Four rows: hero on top, then three filler rows. Ensures
            // the hero's aura has neighbours so the reviewer can spot
            // any visual bleed between adjacent rows AND see how the
            // aura compares to plain users in the same view.
            (Persona persona, int rank)[] rows =
            {
                (hero, 1),
                (filler[0], 2),
                (filler[1], 3),
                (filler[2], 4),
            };

            return new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(0, 2),
                Shear = sheared ? OsuGame.SHEAR : Vector2.Zero,
                Children = rows.Select(r =>
                {
                    var score = makeFakeScore(r.persona, 6100 + r.rank, r.rank);
                    return (Drawable)new BeatmapLeaderboardScore(score, sheared)
                    {
                        Rank = score.Position,
                        Shear = Vector2.Zero,
                    };
                }).ToArray(),
            };
        }

        private static Drawable buildAllSurfacesGameplayLeaderboard(Persona hero, Persona[] filler)
        {
            // Four-row strip with the hero "tracked" (marked as the
            // local player) so the gameplay leaderboard's
            // highlight-the-local-row visual treatment applies too.
            (Persona persona, bool tracked, int rank)[] rows =
            {
                (hero, true, 1),
                (filler[0], false, 2),
                (filler[1], false, 3),
                (filler[2], false, 4),
            };

            return new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Y,
                Width = 250,
                Spacing = new Vector2(0, 6),
                Direction = FillDirection.Vertical,
                Children = rows.Select(r =>
                {
                    var user = makeFakeUser(r.persona, 6200 + r.rank);
                    var displayScore = new BindableLong(2_000_000 - r.rank * 100_000);
                    var glScore = new GameplayLeaderboardScore(user, r.tracked, displayScore)
                    {
                        Position = { Value = r.rank },
                    };
                    return (Drawable)new DrawableGameplayLeaderboardScore(glScore)
                    {
                        Expanded = { Value = true },
                        RelativeSizeAxes = Axes.X,
                    };
                }).ToArray(),
            };
        }

        private static Drawable buildAllSurfacesUserPanels(Persona hero)
        {
            // One of each panel variant so the reviewer can confirm the
            // aura renders correctly across all dashboard placements
            // (Brick = compact pill, Grid = square card, List = wide
            // row, Rank = trophy-style panel). Same persona on all four
            // so the eye can cross-reference how the aura adapts to
            // each layout's name size + position.
            var user = makeFakeUser(hero, 6300);

            return new FillFlowContainer
            {
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(8),
                        Direction = FillDirection.Horizontal,
                        Children = new Drawable[]
                        {
                            new UserBrickPanel(user),
                            new UserGridPanel(user) { Width = 220 },
                        },
                    },
                    new UserListPanel(user),
                    new UserRankPanel(user) { Width = 280 },
                },
            };
        }

        private static Drawable buildAllSurfacesProfileHeader(Persona hero)
        {
            // Profile header takes care of its own internal sizing; just
            // wrap it in a fixed-height container so the surrounding
            // FillFlow doesn't try to render the header at zero height.
            var header = new ProfileHeader();
            header.User.Value = new UserProfileData(makeFakeUser(hero, 6400), new OsuRuleset().RulesetInfo);

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                // Tall enough to show the cover image + the header band
                // without scrolling inside the header itself.
                Height = 320,
                Child = header,
            };
        }
    }
}
