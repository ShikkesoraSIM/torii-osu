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
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserEffects;
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
            public Persona(string username, string? groupKey, string? groupName, string? groupShortName, string? groupColourHex)
            {
                Username = username;
                GroupKey = groupKey;
                GroupName = groupName;
                GroupShortName = groupShortName;
                GroupColourHex = groupColourHex;
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
            }
        }

        // ---------- User / score factory ---------------------------------

        private static APIUser makeFakeUser(Persona p, int id) => new APIUser
        {
            Id = id,
            Username = p.Username,
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
            var flow = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
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
    }
}
