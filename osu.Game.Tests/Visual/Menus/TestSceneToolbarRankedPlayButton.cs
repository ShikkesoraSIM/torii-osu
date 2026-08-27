// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Overlays.Toolbar;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.Menus
{
    /// <summary>
    /// La pildora de ranked play en sus tres estados, sin necesidad de un server atras.
    /// </summary>
    /// <remarks>
    /// Existe porque los tres estados dependen de que haya gente real en la cola de prod:
    /// esperar a que se junten dos personas para ver si el numero se dibuja bien no es una
    /// forma de trabajar. Con esto se empuja el estado a mano.
    /// </remarks>
    [TestFixture]
    public partial class TestSceneToolbarRankedPlayButton : OsuTestScene
    {
        private ToolbarRankedPlayButton button = null!;

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            Children = new Drawable[]
            {
                // Fondo oscuro imitando el toolbar, para juzgar el contraste real.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(20, 20, 24, 255),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Child = button = new ToolbarRankedPlayButton
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                },
            };
        });

        [Test]
        public void TestStates()
        {
            AddStep("vacia (compacta)", () => button.SetStateForTesting(0, 0));
            AddStep("1 en cola", () => button.SetStateForTesting(1, 0));
            AddStep("5 en cola", () => button.SetStateForTesting(5, 0));
            AddStep("12 en cola (dos digitos)", () => button.SetStateForTesting(12, 0));
            AddStep("1 partida en curso", () => button.SetStateForTesting(0, 1));
            AddStep("cola + partida", () => button.SetStateForTesting(3, 1));
            AddStep("de vuelta a vacia", () => button.SetStateForTesting(0, 0));
        }

        [Test]
        public void TestJoinAnnouncement()
        {
            AddStep("entra uno", () => button.SetStateForTesting(1, 0, new[] { "Shikkesora" }));
            AddWaitStep("mirar el cartel", 12);
            AddStep("entran dos", () => button.SetStateForTesting(3, 0, new[] { "Shikkesora", "Ayreth" }));
            AddWaitStep("mirar", 12);
            AddStep("entran muchos", () => button.SetStateForTesting(9, 0, new[] { "Shikkesora", "Ayreth", "Hek", "noki" }));
            AddWaitStep("mirar", 12);
        }

        [Test]
        public void TestNameThatDoesNotFit()
        {
            // El cartel autoajusta, pero un nombre largo con la pildora pegada al borde
            // derecho del toolbar es justo donde algo asi se sale de pantalla.
            AddStep("nombre largo", () => button.SetStateForTesting(1, 0, new[] { "ASuperExtremelyLongUsername" }));
            AddWaitStep("mirar que no se corte", 12);
        }

        [Test]
        public void TestClashScalesWithCount()
        {
            AddStep("choque chico (uno)", () => button.SetStateForTesting(1, 0, new[] { "a" }));
            AddWaitStep("mirar", 6);
            AddStep("choque grande (cuatro)", () => button.SetStateForTesting(5, 0, new[] { "a", "b", "c", "d" }));
            AddWaitStep("mirar", 6);
        }

        [Test]
        public void TestSizing()
        {
            // La pildora tiene que MEDIR distinto segun el estado, no solo verse distinto.
            float empty = 0;

            AddStep("vacia", () => button.SetStateForTesting(0, 0));
            AddWaitStep("dejar animar", 4);
            AddStep("medir", () => empty = button.DrawWidth);

            AddStep("con cola", () => button.SetStateForTesting(7, 0));
            AddWaitStep("dejar animar", 4);
            AddAssert("crecio", () => button.DrawWidth > empty);

            AddStep("vacia de nuevo", () => button.SetStateForTesting(0, 0));
            AddWaitStep("dejar animar", 6);
            AddAssert("volvio a achicarse", () => button.DrawWidth <= empty + 1);
        }
    }
}
