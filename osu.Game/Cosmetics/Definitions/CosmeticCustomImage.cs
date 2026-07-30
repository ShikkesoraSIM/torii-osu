// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: decodifica una imagen custom (PNG en base64, embebida en la definicion de un cosmetico)
    /// a un factory de sprite reusable. es lo que deja que un creador "suba" su propia forma de
    /// particula — la imagen viaja DENTRO del JSON, asi sigue siendo portable/shareable para contests.
    ///
    /// Es data pura (un PNG, sin code-exec), asi que es seguro cargarla de archivos de la comunidad,
    /// pero con caps estrictos de tamaño para acotar el peso y el trabajo de GPU. Lo comparten los
    /// trails de particula (<see cref="CosmeticParticleTrail"/>) y las auras data-driven, para tener
    /// UN solo lugar con los caps y la normalizacion de tamaño.
    /// </summary>
    public static class CosmeticCustomImage
    {
        /// <summary>lado maximo (px) de la imagen decodificada.</summary>
        public const int MaxDimension = 256;

        /// <summary>largo maximo del string base64 (~500KB decodificados).</summary>
        public const int MaxBase64Length = 700_000;

        /// <summary>lado mayor al que se normaliza por defecto la imagen resultante.</summary>
        public const float DefaultLongestSidePx = 26f;

        /// <summary>
        /// Convierte un base64 en un factory de <see cref="Sprite"/> (misma firma que las formas
        /// built-in: <c>Func&lt;int, Drawable&gt;</c>, el int es el indice de emision). Devuelve null si
        /// falta / supera los caps / es invalida — el caller se queda con su forma built-in.
        /// </summary>
        /// <param name="renderer">renderer para crear la textura.</param>
        /// <param name="base64">el PNG en base64 embebido en la definicion.</param>
        /// <param name="longestSidePx">a que tamaño (lado mayor) normalizar; los sliders de escala lo ajustan encima.</param>
        public static Func<int, Drawable>? Resolve(IRenderer renderer, string base64, float longestSidePx = DefaultLongestSidePx)
        {
            if (renderer == null || string.IsNullOrEmpty(base64) || base64.Length > MaxBase64Length)
                return null;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);

                using var stream = new MemoryStream(bytes);
                var upload = new TextureUpload(stream);

                if (upload.Width <= 0 || upload.Height <= 0 || upload.Width > MaxDimension || upload.Height > MaxDimension)
                    return null;

                var texture = renderer.CreateTexture(upload.Width, upload.Height);
                texture.SetData(upload);

                // normalizamos el lado mayor preservando el aspecto, asi una imagen grande no entra
                // gigante (despues los sliders Start/End scale la ajustan encima).
                float longest = Math.Max(texture.DisplayWidth, texture.DisplayHeight);
                float scale = longest > 0 ? longestSidePx / longest : 1f;
                var drawSize = new Vector2(texture.DisplayWidth * scale, texture.DisplayHeight * scale);

                return _ => new Sprite
                {
                    Texture = texture,
                    Size = drawSize,
                    Origin = Anchor.Centre,
                };
            }
            catch
            {
                // base64 / imagen invalida -> null, el caller mantiene su forma built-in.
                return null;
            }
        }
    }
}
