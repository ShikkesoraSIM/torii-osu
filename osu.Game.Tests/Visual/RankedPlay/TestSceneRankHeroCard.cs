// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Overlays;
using osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.RankedPlay
{
    /// <summary>
    /// torii: preview del hero card de rank de la cola, con todos los tiers y el estado provisional,
    /// sin entrar a una cola real. Cada paso recrea el card asi se ve la animacion de entrada.
    /// </summary>
    public partial class TestSceneRankHeroCard : OsuTestScene
    {
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        public TestSceneRankHeroCard()
        {
            variant("Unranked (no games)", null, false, null);
            variant("Bronze 3", 780, false, 0.08);
            variant("Silver 2", 960, false, 0.26);
            variant("Gold 2", 1120, false, 0.52);
            variant("Gold 1 (near promo)", 1235, false, 0.63);
            variant("Platinum 1", 1300, false, 0.74);
            variant("Diamond 3", 1460, false, 0.88);
            variant("Master", 1720, false, 0.97);
            variant("Provisional (seeded 1960)", 1960, true, null);
        }

        private void variant(string name, int? rating, bool provisional, double? percentile) => AddStep(name, () =>
        {
            RankHeroCard card;

            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(400, 340),
                Masking = true,
                CornerRadius = 14,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.09f, 0.08f, 0.13f, 1f),
                    },
                    card = new RankHeroCard(),
                }
            };

            card.SetData(rating, provisional, percentile);
        });
    }
}
