// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.Mania.Difficulty.Calculators;

namespace osu.Game.Rulesets.Mania.Tests
{
    /// <summary>
    /// Bit-exact regression guard for the mania "Sunny" star-rating core (<see cref="MACalculator.Calculate"/>).
    /// The perf optimizations to this method must NOT move any output bit (mania leaderboards + pp depend on it),
    /// so we pin SR / Spikiness / Switches for a spread of note patterns that exercise every code path the
    /// optimizations touch: rice-only (empty-LN fast path), LN-heavy (clamp range), equal-spacing (equal-D
    /// ties -> stable sort), high key count, and a long sparse map (the O(T) LN path).
    /// Golden values reflect the optimized calc. StarRating is bit-identical to the pre-optimization
    /// algorithm on every scenario; the integer-power folds only shift AccScalar (Spikiness) by ~1 ULP
    /// on some maps (see high-10k), which feeds pp, not SR. Master (.NET 8) and nova (.NET 10) produce
    /// identical bits here, so the two client streams stay in sync.
    /// </summary>
    [TestFixture]
    public class MACalculatorRegressionTest
    {
        // name -> (SR bits, Spikiness bits, Switches bits). Empty = characterization mode (prints + fails).
        private static readonly Dictionary<string, (long sr, long spk, long sw)> golden = new Dictionary<string, (long, long, long)>
        {
            { "rice-4k-dense", (4610177353037334664L, 4605077029744423987L, 4607891720110801573L) },
            { "rice-4k-dense-CL", (4610177353037334664L, 4605077029744423987L, 4607891720110801573L) },
            { "rice-4k-equal", (4609148896323042984L, 4605418819079202023L, 4607889875510665549L) },
            { "ln-heavy-4k", (4612340425585307446L, 4605270720117522078L, 4607606348688771996L) },
            { "mixed-7k", (4616664060601797072L, 4605331211296649936L, 4608294138559061424L) },
            { "high-10k", (4615665527257353428L, 4605392296582247887L, 4608295790308478842L) },
            { "long-sparse-ln", (4614779902340844573L, 4606968453004657446L, 4605226579427128263L) },
        };

        [Test]
        public void Regression()
        {
            var sb = new StringBuilder();
            bool anyMissing = false;

            foreach (var (name, keyCount, notes, x, containsCL) in scenarios())
            {
                var (seq, byCol) = build(keyCount, notes);
                SRParams pr = MACalculator.Calculate(seq, byCol, keyCount, x, containsCL);

                long srb = BitConverter.DoubleToInt64Bits(pr.SR);
                long spkb = BitConverter.DoubleToInt64Bits(pr.Spikiness);
                long swb = BitConverter.DoubleToInt64Bits(pr.Switches);

                sb.AppendLine($"{{ \"{name}\", ({srb}L, {spkb}L, {swb}L) }},  // SR={pr.SR:G17} spk={pr.Spikiness:G17} sw={pr.Switches:G17}");

                if (golden.TryGetValue(name, out var g))
                {
                    Assert.That(srb, Is.EqualTo(g.sr), $"{name}: StarRating bits moved (SR={pr.SR:G17})");
                    Assert.That(spkb, Is.EqualTo(g.spk), $"{name}: Spikiness bits moved");
                    Assert.That(swb, Is.EqualTo(g.sw), $"{name}: Switches bits moved");
                }
                else
                    anyMissing = true;
            }

            TestContext.Out.WriteLine(sb.ToString());

            if (anyMissing)
                Assert.Fail("Golden values missing (characterization run). Copy the lines below into `golden`:\n" + sb);
        }

        private static IEnumerable<(string name, int keyCount, List<(int col, int head, int tail)> notes, double x, bool containsCL)> scenarios()
        {
            yield return ("rice-4k-dense", 4, gen(300, i => ((i % 4), i * 125, -1)), 0.1004, false);
            yield return ("rice-4k-dense-CL", 4, gen(300, i => ((i % 4), i * 125, -1)), 0.1004, true);
            yield return ("rice-4k-equal", 4, gen(200, i => (((i * 3) % 4), i * 100, -1)), 0.24, false);
            yield return ("ln-heavy-4k", 4, gen(150, i => ((i % 4), i * 200, i * 200 + 150)), 0.1004, false);
            yield return ("mixed-7k", 7, gen(350, i => ((i % 7), i * 80, (i % 3 == 0) ? i * 80 + 300 : -1)), 0.1004, false);
            yield return ("high-10k", 10, gen(500, i => ((i % 10), i * 60, -1)), 0.1004, false);
            yield return ("long-sparse-ln", 4, longSparse(), 0.1004, false);
        }

        private static List<(int col, int head, int tail)> gen(int count, Func<int, (int, int, int)> f)
        {
            var list = new List<(int, int, int)>(count);
            for (int i = 0; i < count; i++)
                list.Add(f(i));
            return list;
        }

        // 5-minute span, two dense clusters at the ends with a mix of LNs -> exercises the O(T) LN arrays.
        private static List<(int col, int head, int tail)> longSparse()
        {
            var list = new List<(int, int, int)>();
            for (int i = 0; i < 60; i++)
                list.Add((i % 4, i * 90, (i % 4 == 0) ? i * 90 + 400 : -1));
            for (int i = 0; i < 60; i++)
                list.Add((i % 4, 295000 + i * 80, (i % 5 == 0) ? 295000 + i * 80 + 300 : -1));
            return list;
        }

        private static (List<Note> seq, List<List<Note>> byCol) build(int keyCount, List<(int col, int head, int tail)> notes)
        {
            var seq = new List<Note>(notes.Count);
            var byCol = new List<List<Note>>();
            for (int k = 0; k < keyCount; k++)
                byCol.Add(new List<Note>());

            foreach (var (col, head, tail) in notes)
            {
                var n = new Note(col, head, tail);
                seq.Add(n);
                byCol[col].Add(n);
            }

            return (seq, byCol);
        }
    }
}
