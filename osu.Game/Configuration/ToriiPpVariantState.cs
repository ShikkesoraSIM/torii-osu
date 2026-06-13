// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using System;
using System.Threading;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Shared runtime state for pp variant selection.
    /// pp-dev is now the primary (and only) PP system — <see cref="UsePpDevVariant"/> always returns <c>true</c>.
    /// <see cref="BeginScopedOverride"/> is kept for tooling / test contexts that need a temporary override.
    /// </summary>
    public static class ToriiPpVariantState
    {
        // A permanently-true bindable so existing UI code that binds to it keeps working.
        private static readonly BindableBool ppDevAlwaysActive = new BindableBool(true) { Disabled = true };
        private static readonly AsyncLocal<bool?> scopedOverride = new AsyncLocal<bool?>();

        /// <summary>pp-dev is always active; scoped overrides can temporarily suppress it (e.g. for testing).</summary>
        public static bool UsePpDevVariant => scopedOverride.Value ?? true;

        /// <summary>Always-<c>true</c> bindable for UI controls that need to bind to pp variant changes.</summary>
        public static IBindable<bool> UsePpDevVariantBindable => ppDevAlwaysActive;

        /// <summary>No-op — pp-dev is now always enabled regardless of config.</summary>
        public static void Initialise(OsuConfigManager config) { }

        /// <summary>No-op — kept for call-site compatibility.</summary>
        public static void SetEffectiveValue(bool value) { }

        /// <summary>
        /// Temporarily overrides pp variant selection for the current async context.
        /// Useful for tooling or test code that explicitly needs stable-pp behaviour.
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
