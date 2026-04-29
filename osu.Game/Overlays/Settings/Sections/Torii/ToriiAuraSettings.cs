// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    /// <summary>
    /// "User Aura" settings subsection: lets the local user toggle aura
    /// rendering globally and pick which aura they want to broadcast (out
    /// of the auras their groups grant them access to).
    ///
    /// All aura ownership / availability comes from the server catalog
    /// endpoint. The dropdown is populated lazily after the catalog
    /// arrives; before then we show a single "Default (auto)" option so
    /// the UI never looks broken on cold start. Picking a value PATCHes
    /// the server which returns the refreshed catalog → we re-populate
    /// from the response in one round trip.
    /// </summary>
    public partial class ToriiAuraSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "User Aura";

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private FormDropdown<AuraOption> dropdown = null!;
        private AuraSettingsPreview preview = null!;

        // Bindable for the dropdown — items are swapped in once the
        // catalog request resolves. Bound to the picker so changing the
        // selection drives the PATCH + preview update flow centrally.
        private readonly Bindable<AuraOption> selectedOption = new Bindable<AuraOption>(AuraOption.DefaultPlaceholder);

        // Suppress the dropdown -> server roundtrip during the initial
        // populate, otherwise loading the catalog would immediately PATCH
        // back the same value the server just told us.
        private bool suppressUpdate;

        // Sentinel strings come from the server in the catalog response so
        // we don't hardcode them here; default to the documented values
        // until the first catalog arrives.
        private string sentinelDefault = "default";
        private string sentinelNone = "none";

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show user auras",
                    HintText = "Render the per-user particle effect behind elite-group usernames "
                               + "everywhere they appear (chat, profile, leaderboards). Disable on "
                               + "weaker hardware if you'd rather skip the GPU work.",
                    Current = config.GetBindable<bool>(OsuSetting.UserAuraEnabled),
                })
                {
                    Keywords = new[] { "aura", "particles", "torii", "title", "effect" },
                },
                new SettingsItemV2(dropdown = new FormDropdown<AuraOption>
                {
                    Caption = "My equipped aura",
                    HintText = "Pick which aura plays behind your username. Other users see "
                               + "your choice everywhere your name shows up.",
                    Items = new[] { AuraOption.DefaultPlaceholder },
                    Current = selectedOption,
                })
                {
                    Keywords = new[] { "aura", "title", "torii", "cosmetic", "equip" },
                },
                preview = new AuraSettingsPreview
                {
                    // Tiny top margin so the preview isn't flush against
                    // the dropdown — visually separates the picker from
                    // its preview without a heavy divider.
                    Margin = new MarginPadding { Top = 6 },
                },
            };

            selectedOption.BindValueChanged(onSelectionChanged);
            fetchCatalog();
        }

        private void fetchCatalog()
        {
            if (!api.IsLoggedIn)
                return;

            var req = new GetAuraCatalogRequest();
            req.Success += catalog => Schedule(() => populateFromCatalog(catalog));
            // Failures are non-fatal — the placeholder dropdown stays in
            // place. We don't want to nag with a popup on every settings
            // open; if logging is configured the framework already records
            // the failure.
            api.Queue(req);
        }

        private void populateFromCatalog(APIAuraCatalog catalog)
        {
            sentinelDefault = catalog.SentinelDefault;
            sentinelNone = catalog.SentinelNone;

            var options = new List<AuraOption>
            {
                // Always-present: "Default" picks whichever aura the user's
                // groups grant them by priority. The (auto) hint matches the
                // server's resolution behaviour.
                new AuraOption("Default (auto)", sentinelDefault),
                // "None" means "no aura broadcast" — explicit opt-out.
                new AuraOption("None", sentinelNone),
            };

            options.AddRange(catalog.Available.Select(a => new AuraOption(a.DisplayName, a.Id)));

            // Pick which option to highlight: prefer the raw stored setting
            // so a user who picked "None" sees that selected (not the
            // resolved aura). Falls back to "Default" otherwise.
            var initial = options.FirstOrDefault(o => o.AuraId == catalog.CurrentSetting)
                          ?? options[0];

            suppressUpdate = true;
            try
            {
                dropdown.Items = options;
                selectedOption.Value = initial;
            }
            finally
            {
                suppressUpdate = false;
            }

            updatePreview(catalog.EffectiveAuraId);
        }

        private void onSelectionChanged(ValueChangedEvent<AuraOption> e)
        {
            if (suppressUpdate || e.NewValue == null)
                return;

            // Update the preview right away so it feels instant; the server
            // round-trip happens in parallel and corrects us if rejected.
            updatePreview(resolvePreviewIdLocally(e.NewValue.AuraId));

            var req = new UpdateEquippedAuraRequest(translateForRequest(e.NewValue.AuraId));
            req.Success += updated => Schedule(() => populateFromCatalog(updated));
            req.Failure += _ => Schedule(() =>
            {
                // Server rejected — pull the fresh catalog so we display
                // whatever the truth is, not the stale optimistic value.
                fetchCatalog();
            });
            api.Queue(req);
        }

        // The server treats null and "default" identically on read but we
        // keep the explicit string so a non-aware proxy in the middle
        // doesn't strip a `null` field and turn it into "missing" silently.
        private string? translateForRequest(string? value) => value;

        // For the local preview, translate sentinels into a concrete aura
        // id so the preview renders something. "default" -> top-priority
        // entitled aura, "none" -> null (no preview).
        private string? resolvePreviewIdLocally(string? selection)
        {
            if (selection == sentinelNone)
                return null;

            if (selection == null || selection == sentinelDefault)
            {
                var entitled = AuraRegistry.GetEntitledAuras(api.LocalUser.Value).FirstOrDefault();
                return entitled?.AuraId;
            }

            return selection;
        }

        private void updatePreview(string? auraId)
        {
            preview.Show(auraId == null ? null : AuraRegistry.GetById(auraId));
        }

        /// <summary>
        /// Dropdown row. <see cref="AuraId"/> is the value sent to the
        /// server (sentinel string or concrete aura id); <see cref="Label"/>
        /// is what shows in the dropdown.
        /// </summary>
        public class AuraOption
        {
            public string Label { get; }
            public string? AuraId { get; }

            public AuraOption(string label, string? auraId)
            {
                Label = label;
                AuraId = auraId;
            }

            public override string ToString() => Label;

            public override bool Equals(object? obj) =>
                obj is AuraOption other && other.AuraId == AuraId;

            public override int GetHashCode() => AuraId?.GetHashCode() ?? 0;

            // Used as the placeholder item before the catalog arrives so the
            // dropdown is non-empty (FormDropdown<T> rejects empty Items).
            public static readonly AuraOption DefaultPlaceholder = new AuraOption("Default (auto)", "default");
        }
    }
}
