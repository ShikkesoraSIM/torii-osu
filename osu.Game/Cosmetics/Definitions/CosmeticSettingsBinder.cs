// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using osu.Framework.Graphics;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: aplica el dict de <see cref="CosmeticDefinition.Settings"/> (JSON) sobre las propiedades
    /// publicas de una clase runtime (un trail, mas adelante un aura element). mismo espiritu que el
    /// ApplySerialisedInfo del SkinEditor: match por nombre + conversion de tipos. defensivo a proposito
    /// (un valor invalido se saltea en vez de tirar) porque la data puede venir de la comunidad.
    /// </summary>
    public static class CosmeticSettingsBinder
    {
        public static void Apply(object target, JObject settings, params string[] skip)
        {
            if (target == null || settings == null)
                return;

            var type = target.GetType();

            foreach (var entry in settings.Properties())
            {
                if (skip.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                var prop = type.GetProperty(entry.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite)
                    continue;

                try
                {
                    prop.SetValue(target, Convert(entry.Value, prop.PropertyType));
                }
                catch
                {
                    // torii: valor que no matchea el tipo (data corrupta / de otra version) -> lo
                    // ignoramos y seguimos, en vez de romper toda la carga del cosmetico.
                }
            }
        }

        private static object Convert(JToken token, Type target)
        {
            if (target == typeof(Color4))
                return ParseColour(token);

            if (target == typeof(Color4[]))
                return ((JArray)token).Select(ParseColour).ToArray();

            if (target == typeof(Vector2))
            {
                var arr = (JArray)token;
                return new Vector2((float)arr[0], (float)arr[1]);
            }

            if (target == typeof(BlendingParameters))
                return ParseBlending(token);

            if (target.IsEnum)
                return Enum.Parse(target, token.Value<string>(), ignoreCase: true);

            if (target == typeof(float)) return token.Value<float>();
            if (target == typeof(double)) return token.Value<double>();
            if (target == typeof(int)) return token.Value<int>();
            if (target == typeof(bool)) return token.Value<bool>();
            if (target == typeof(string)) return token.Value<string>();

            return token.ToObject(target);
        }

        /// <summary>blending (heredado de Drawable) como nombre de preset. "additive" para el glow
        /// tipico de los trails; "inherit" = blend normal (color true).</summary>
        public static BlendingParameters ParseBlending(JToken token)
        {
            switch (token.Value<string>()?.Trim().ToLowerInvariant())
            {
                case "additive": return BlendingParameters.Additive;
                case "mixture": return BlendingParameters.Mixture;
                case "none": return BlendingParameters.None;
                default: return BlendingParameters.Inherit;
            }
        }

        /// <summary>colores como "#RGB" / "#RRGGBB" / "#RRGGBBAA" (recomendado) o como [r,g,b(,a)] 0-255.</summary>
        public static Color4 ParseColour(JToken token)
        {
            if (token.Type == JTokenType.String)
                return HexToColour(token.Value<string>());

            var arr = (JArray)token;
            float r = (float)arr[0], g = (float)arr[1], b = (float)arr[2];
            float a = arr.Count > 3 ? (float)arr[3] : 255f;
            return new Color4(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        /// <summary>Color4 -> "#RRGGBBAA", para que la Creator exporte definiciones legibles.</summary>
        public static string ColourToHex(Color4 c)
        {
            byte r = (byte)Math.Round(c.R * 255f);
            byte g = (byte)Math.Round(c.G * 255f);
            byte b = (byte)Math.Round(c.B * 255f);
            byte a = (byte)Math.Round(c.A * 255f);
            return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
        }

        private static Color4 HexToColour(string hex)
        {
            hex = hex.TrimStart('#');

            // #RGB shorthand -> expand.
            if (hex.Length == 3)
                hex = string.Concat(hex.Select(ch => new string(ch, 2)));

            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte a = hex.Length >= 8 ? byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : (byte)255;
            return new Color4(r / 255f, g / 255f, b / 255f, a / 255f);
        }
    }
}
