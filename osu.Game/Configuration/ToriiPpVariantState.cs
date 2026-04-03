// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using System;
using System.Threading;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Shared runtime state for selecting pp variants in client-side calculations.
    /// </summary>
    public static class ToriiPpVariantState
    {
        private static readonly BindableBool usePpDevVariant = new BindableBool();
        private static readonly AsyncLocal<bool?> scopedOverride = new AsyncLocal<bool?>();
        private static IBindable<bool>? configToggle;

        public static bool UsePpDevVariant => scopedOverride.Value ?? usePpDevVariant.Value;

        public static IBindable<bool> UsePpDevVariantBindable => usePpDevVariant;

        public static void Initialise(OsuConfigManager config)
        {
            configToggle?.UnbindAll();
            configToggle = config.GetBindable<bool>(OsuSetting.AlphaPpDevModeEnabled).GetBoundCopy();
            configToggle.BindValueChanged(v => SetEffectiveValue(v.NewValue), true);
        }

        public static void SetEffectiveValue(bool value) => usePpDevVariant.Value = value;

        /// <summary>
        /// Temporarily overrides pp variant selection for the current async context.
        /// </summary>
        public static IDisposable BeginScopedOverride(bool usePpDev)
        {
            return new ScopedVariantOverride(usePpDev);
        }

        private sealed class ScopedVariantOverride : IDisposable
        {
            private readonly bool? previousValue;
            private bool disposed;

            public ScopedVariantOverride(bool usePpDev)
            {
                previousValue = scopedOverride.Value;
                scopedOverride.Value = usePpDev;
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                scopedOverride.Value = previousValue;
                disposed = true;
            }
        }
    }
}
