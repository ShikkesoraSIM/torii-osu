// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: el modelo de datos de un aura data-driven. es lo que vive dentro de
    /// <see cref="CosmeticDefinition.Settings"/> cuando <c>Type == Aura</c>, deserializado con
    /// Newtonsoft (todos los campos son primitivos / strings / listas, para que el JSON sea directo
    /// y portable). El intérprete (AuraParticleBuilder) lo lee y arma las partículas; los colores
    /// van como hex y se convierten al construir, reusando <see cref="CosmeticSettingsBinder.ParseColour"/>.
    ///
    /// Un aura NO es "un trail con más campos": un trail spawnea UN tipo de partícula, un aura
    /// spawnea una MEZCLA PONDERADA de tipos (<see cref="Particles"/>), más el glow del nombre y
    /// sellos opcionales. Todos los defaults acá son sanos para "efecto sutil ambiental".
    /// </summary>
    public class DataDrivenAura
    {
        // ---- emisión / tuning ----

        /// <summary>ms promedio entre spawns.</summary>
        public double SpawnIntervalMs { get; set; } = 280;

        /// <summary>delay random extra (0..N) sumado a cada spawn.</summary>
        public double SpawnJitterMs { get; set; } = 180;

        /// <summary>tope de partículas vivas a la vez (acota el trabajo de GPU).</summary>
        public int MaxAlive { get; set; } = 10;

        /// <summary>el glow que abraza las letras del nombre. null = sin glow.</summary>
        public GlowSpec Glow { get; set; }

        /// <summary>la mezcla de tipos de partícula. cada spawn hace un roll ponderado por Weight.</summary>
        public List<ParticleSpec> Particles { get; set; } = new List<ParticleSpec>();

        // ---- ornamentos opcionales (los usan las auras Founder) ----

        /// <summary>capa persistente bajo las partículas (raro). null = ninguna.</summary>
        public OrnamentSpec Background { get; set; }

        /// <summary>sello inline a la IZQUIERDA del nombre. null = ninguno.</summary>
        public OrnamentSpec LeadingOrnament { get; set; }

        /// <summary>sello inline a la DERECHA del nombre. null = ninguno.</summary>
        public OrnamentSpec TrailingOrnament { get; set; }
    }

    /// <summary>el text-shape glow: color + pulso/intensidad (control total, decisión de producto).</summary>
    public class GlowSpec
    {
        /// <summary>color del glow como hex. null/vacío = sin glow.</summary>
        public string Colour { get; set; }

        public float MinAlpha { get; set; } = 0.5f;
        public float MaxAlpha { get; set; } = 0.9f;

        /// <summary>radio del blur (sigma). más alto = halo más difuso.</summary>
        public float BlurSigma { get; set; } = 4f;

        /// <summary>ms de un ciclo del pulso.</summary>
        public float PulseMs { get; set; } = 1500;

        /// <summary>si late o queda fijo (respeta reduced-motion igual).</summary>
        public bool Pulsate { get; set; } = true;
    }

    /// <summary>una entrada de la mezcla: un tipo de partícula con su peso y todos sus ejes.</summary>
    public class ParticleSpec
    {
        /// <summary>peso relativo en el roll ponderado que elige qué tipo spawnea.</summary>
        public float Weight { get; set; } = 1;

        // ---- forma ----

        /// <summary>forma built-in por nombre (whitelist de <see cref="AuraParticleShapes"/>). Si es
        /// "customImage" usa <see cref="CustomImage"/>. Ignorado si <see cref="Layers"/> o
        /// <see cref="Icon"/> o <see cref="Text"/> están presentes.</summary>
        public string Shape { get; set; } = "circle";

        /// <summary>PNG propio en base64 (cuando Shape == "customImage"). Data pura, portable.</summary>
        public string CustomImage { get; set; }

        /// <summary>glyph FontAwesome por nombre (whitelist). Alternativa a Shape para íconos.</summary>
        public string Icon { get; set; }

        /// <summary>pool de strings a renderizar como texto (ej ["0","1"] de las dev bits). random del pool.</summary>
        public string[] Text { get; set; }

        /// <summary>peso de fuente del texto ("Regular"/"Bold"/...).</summary>
        public string FontWeight { get; set; } = "Bold";

        /// <summary>rango [min,max] de tamaño en px (× ParticleScale del nombre).</summary>
        public float[] SizePx { get; set; } = { 6, 10 };

        /// <summary>ratio [w,h] para formas no cuadradas (pétalos, barras). null = cuadrado.</summary>
        public float[] Aspect { get; set; }

        /// <summary>blending de esta partícula ("additive"/"inherit"/...).</summary>
        public string Blend { get; set; } = "additive";

        /// <summary>rango [min,max] de rotación inicial en grados.</summary>
        public float[] InitialRotation { get; set; }

        /// <summary>copia agrandada y tenue detrás (fake-glow). null = sin halo.</summary>
        public HaloSpec Halo { get; set; }

        /// <summary>partícula compuesta multi-capa (ej sparkle = 2 boxes + core). Si está, pisa Shape.</summary>
        public List<LayerSpec> Layers { get; set; }

        // ---- colores ----

        /// <summary>paleta de colores (hex). el pick elige uno según <see cref="ColourPick"/>.</summary>
        public string[] Palette { get; set; } = { "#FFFFFFFF" };

        /// <summary>cómo se elige el color: "random" | "weighted" | "twotone" | "bylayer" | "fixed".</summary>
        public string ColourPick { get; set; } = "random";

        /// <summary>pesos para ColourPick == "weighted" (paralelo a Palette).</summary>
        public float[] ColourWeights { get; set; }

        // ---- spawn (fracciones de parentSize; pueden exceder [0,1] para rodear el nombre) ----

        public float[] SpawnX { get; set; } = { 0, 1 };
        public float[] SpawnY { get; set; } = { 0.5f, 0.95f };

        /// <summary>selector ponderado de zona de spawn. si está, pisa SpawnX/SpawnY.</summary>
        public List<ZoneSpec> Zones { get; set; }

        // ---- movimiento ----

        /// <summary>
        /// cómo se mueve la partícula. Lineales: "drift" (deriva), "rise" (flota arriba), "fall"
        /// (cae acelerando), "burst" (sale disparada y frena). Circulares/oscilatorios: "orbit"
        /// (círculos), "spiral" (espiral que se abre), "zigzag" (serpentea), "pendulum" (se balancea).
        /// En el lugar: "popInPlace" (aparece con pop), "ripple" (crece como onda), "beam" (aparece,
        /// se queda, se va). Los circulares usan <see cref="OrbitRadius"/> + <see cref="OrbitTurns"/>.
        /// </summary>
        public string Motion { get; set; } = "drift";

        /// <summary>rango [min,max] de drift en X (fracción del ancho).</summary>
        public float[] DriftX { get; set; } = { -0.1f, 0.1f };

        /// <summary>rango [min,max] de drift en Y (fracción del alto; negativo = sube).</summary>
        public float[] DriftY { get; set; } = { -0.9f, -0.4f };

        /// <summary>radio del movimiento circular/oscilatorio (orbit/spiral/zigzag/pendulum), fracción del alto. [min,max].</summary>
        public float[] OrbitRadius { get; set; } = { 0.28f, 0.28f };

        /// <summary>vueltas (orbit/spiral) o ciclos (zigzag/pendulum) completos a lo largo de la vida.</summary>
        public float OrbitTurns { get; set; } = 1.5f;

        public float[] LifetimeMs { get; set; } = { 1500, 2000 };

        // ---- animación ----

        public AnimSpec Anim { get; set; } = new AnimSpec();
    }

    /// <summary>una sub-capa de una partícula compuesta.</summary>
    public class LayerSpec
    {
        public string Shape { get; set; }
        public string Icon { get; set; }

        /// <summary>tamaño relativo al tamaño base de la partícula.</summary>
        public float SizeRatio { get; set; } = 1;

        public float[] Aspect { get; set; }

        /// <summary>índice al Palette del <see cref="ParticleSpec"/>; -1 = blanco fijo.</summary>
        public int ColourRef { get; set; } = -1;

        public float Alpha { get; set; } = 1;
        public string Blend { get; set; } = "additive";

        /// <summary>offset [x,y] del centro (fracción del tamaño). para glints especulares.</summary>
        public float[] Offset { get; set; }
    }

    /// <summary>copia agrandada y tenue detrás de la partícula, para fake-glow sin shader.</summary>
    public class HaloSpec
    {
        public float Scale { get; set; } = 1.5f;
        public float Alpha { get; set; } = 0.18f;
    }

    /// <summary>una zona de spawn ponderada (para el gate 50/25/25 de las Founder).</summary>
    public class ZoneSpec
    {
        public float Weight { get; set; } = 1;
        public float[] SpawnX { get; set; } = { 0, 1 };
        public float[] SpawnY { get; set; } = { 0, 1 };
    }

    /// <summary>la gramática de animación de una partícula (fade/scale/rotate/keyframes/loops).</summary>
    public class AnimSpec
    {
        public float FadeInMs { get; set; } = 240;

        /// <summary>rango [min,max] del alpha pico.</summary>
        public float[] PeakAlpha { get; set; } = { 1, 1 };

        public string FadeInEasing { get; set; } = "OutQuad";
        public float FadeOutMs { get; set; } = 340;

        /// <summary>pop de escala en la entrada (OutBack). null = sin pop.</summary>
        public ScaleInSpec ScaleIn { get; set; }

        /// <summary>rango [min,max] de rotación durante toda la vida. null = sin rotación.</summary>
        public float[] RotateOverLife { get; set; }

        public string RotateOverLifeEasing { get; set; } = "InOutSine";

        /// <summary>rotación ABSOLUTA one-shot (ej 45° para volver un box un rombo). null = no aplica.</summary>
        public float? RotateToAbsolute { get; set; }

        /// <summary>resize en el lugar (koi ripple / approach ring). null = no aplica.</summary>
        public ResizeSpec Resize { get; set; }

        /// <summary>timeline de alpha (shimmer/twinkle). si está, reemplaza el fade-in/hold/fade-out simple.</summary>
        public List<KeyframeSpec> FadeKeyframes { get; set; }

        /// <summary>timeline de escala sincronizado con FadeKeyframes (twinkle de Stardust).</summary>
        public List<KeyframeSpec> ScaleKeyframes { get; set; }

        /// <summary>loops concurrentes (bob/pulse/breathing). se arman en LoadComplete (nunca inline).</summary>
        public List<LoopSpec> Loops { get; set; }
    }

    public class ScaleInSpec
    {
        public float From { get; set; }
        public float To { get; set; } = 1;
        public float Ms { get; set; } = 260;
        public string Easing { get; set; } = "OutBack";
    }

    public class ResizeSpec
    {
        /// <summary>factor final respecto al tamaño inicial.</summary>
        public float Factor { get; set; } = 1;
        public float Ms { get; set; } = 400;
        public string Easing { get; set; } = "OutCubic";
    }

    /// <summary>un keyframe de una timeline: multiplicador del valor base + duración del tramo.</summary>
    public class KeyframeSpec
    {
        /// <summary>multiplicador sobre el valor base (alpha pico o escala 1).</summary>
        public float Mul { get; set; } = 1;
        public float Ms { get; set; } = 200;
        public string Easing { get; set; } = "InOutSine";
    }

    /// <summary>una oscilación en loop, concurrente con el movimiento principal.</summary>
    public class LoopSpec
    {
        /// <summary>"scale" | "moveOffset".</summary>
        public string Channel { get; set; } = "scale";

        /// <summary>"inner" (el drawable interno) | "whole" (el container entero).</summary>
        public string Target { get; set; } = "inner";

        /// <summary>amplitud: fracción de escala (scale) o px (moveOffset).</summary>
        public float Amount { get; set; } = 0.1f;

        /// <summary>ms de medio ciclo.</summary>
        public float Ms { get; set; } = 700;

        public string Easing { get; set; } = "InOutSine";
    }

    /// <summary>un ornamento (sello): stack de capas concéntricas + un breath opcional.</summary>
    public class OrnamentSpec
    {
        public List<OrnamentLayerSpec> Layers { get; set; } = new List<OrnamentLayerSpec>();

        /// <summary>tamaño base en px del ornamento.</summary>
        public float BaseSizePx { get; set; } = 20;

        /// <summary>respiración (pulso lento de alpha). null = fijo.</summary>
        public BreathSpec Breath { get; set; }
    }

    public class OrnamentLayerSpec
    {
        /// <summary>"fillCircle" | "ringGlyph" | "iconGlyph".</summary>
        public string Kind { get; set; } = "fillCircle";

        public string Icon { get; set; }
        public float SizeRatio { get; set; } = 1;
        public string Colour { get; set; } = "#FFFFFFFF";
        public float Alpha { get; set; } = 1;

        /// <summary>"additive" | "inherit" (para el backplate onyx opaco de Lacquered).</summary>
        public string Blend { get; set; } = "additive";
    }

    public class BreathSpec
    {
        public float MinAlpha { get; set; } = 0.6f;
        public float MaxAlpha { get; set; } = 1;
        public float HalfPeriodMs { get; set; } = 1200;
        public string Easing { get; set; } = "InOutSine";
    }
}
