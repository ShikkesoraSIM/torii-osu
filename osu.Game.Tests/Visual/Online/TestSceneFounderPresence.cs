// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Metadata;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual.Metadata;
using osu.Game.Users;
using osuTK;

namespace osu.Game.Tests.Visual.Online
{
    /// <summary>
    /// La tarjeta del fundador al lado de las de siempre, como se ven en la lista de
    /// gente conectada.
    /// </summary>
    /// <remarks>
    /// Aislada no se puede juzgar: lo que importa es si el anillo que cicla y el texto
    /// propio se leen como algo distinto SIN gritar al lado de las tarjetas normales. Por
    /// eso van todas juntas y en la misma grilla que usa el dashboard.
    ///
    /// El caso que de verdad hay que mirar es el ultimo: cuando el fundador esta jugando
    /// de verdad, su tarjeta tiene que volver a comportarse como cualquier otra. Si el
    /// arcoiris se queda puesto ahi, el estado divino dejo de significar "no esta" y pasa
    /// a ser una decoracion permanente.
    /// </remarks>
    [TestFixture]
    public partial class TestSceneFounderPresence : OsuTestScene
    {
        private const int founder_user_id = 3;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        private TestMetadataClient metadataClient = null!;

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            Child = new DependencyProvidingContainer
            {
                RelativeSizeAxes = Axes.Both,
                CachedDependencies =
                [
                    (typeof(MetadataClient), metadataClient = new TestMetadataClient()),
                ],
                Children = new Drawable[]
                {
                    metadataClient,
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Full,
                        Spacing = new Vector2(10),
                        Width = 900,
                        Children = new Drawable[]
                        {
                            panelFor(founder_user_id, @"Shikkesora"),
                            panelFor(101, @"Flobes"),
                            panelFor(102, @"basil"),
                            panelFor(103, @"lauri"),
                        },
                    },
                },
            };

            metadataClient.BeginWatchingUserPresence();
        });

        private static Drawable panelFor(int id, string username) => new UserGridPanel(new APIUser
        {
            Id = id,
            Username = username,
            CountryCode = CountryCode.AR,
        })
        {
            Width = 290,
        };

        [SetUpSteps]
        public void SetUpSteps()
        {
            // Los de al lado quietos: son la referencia contra la que se mira al fundador.
            AddStep("los demas, conectados", () =>
            {
                presence(101, UserStatus.Online, new UserActivity.ChoosingBeatmap());
                presence(102, UserStatus.Online, new UserActivity.InSoloGame(new TestBeatmap(new RulesetInfo { OnlineID = 0 }).BeatmapInfo, new RulesetInfo { OnlineID = 0 }));
                presence(103, UserStatus.Online, null);
            });
        }

        [Test]
        public void TestFounderIsPresentWhileAway()
        {
            // Lo que manda el server cuando el fundador NO esta conectado: presente y sin
            // actividad. Tiene que leerse "Watching my Grasshoppers" con el anillo ciclando.
            AddStep("fundador ausente (presencia sintetica)", () => presence(founder_user_id, UserStatus.Online, null));
        }

        [Test]
        public void TestFounderPlayingLooksNormal()
        {
            AddStep("fundador ausente", () => presence(founder_user_id, UserStatus.Online, null));

            // Con actividad de verdad el estado divino se apaga solo: sin esto el arcoiris
            // seria permanente y ya no diria nada.
            AddStep("fundador jugando de verdad", () => presence(founder_user_id, UserStatus.Online,
                new UserActivity.InSoloGame(new TestBeatmap(new RulesetInfo { OnlineID = 0 }).BeatmapInfo, new RulesetInfo { OnlineID = 0 })));

            AddStep("fundador eligiendo mapa", () => presence(founder_user_id, UserStatus.Online, new UserActivity.ChoosingBeatmap()));

            AddStep("vuelve a ausente", () => presence(founder_user_id, UserStatus.Online, null));
        }

        [Test]
        public void TestOnlyTheFounderGetsIt()
        {
            // Mismo estado exacto en otro usuario: presente y sin actividad. Ese tiene que
            // decir "Online" y quedarse verde. Si el arcoiris aparece aca, la condicion se
            // escribio sobre "sin actividad" y no sobre "es el fundador".
            AddStep("otro usuario, presente y sin actividad", () => presence(103, UserStatus.Online, null));
        }

        private void presence(int userId, UserStatus status, UserActivity? activity)
            => metadataClient.UserPresenceUpdated(userId, new UserPresence { Status = status, Activity = activity });
    }
}
