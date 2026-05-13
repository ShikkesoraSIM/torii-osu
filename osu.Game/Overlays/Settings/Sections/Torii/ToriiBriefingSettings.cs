// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Overlays.ToriiBriefing;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiBriefingSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Briefing";

        [Resolved(canBeNull: true)]
        private ToriiBriefingOverlay? briefingOverlay { get; set; }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddRange(new Drawable[]
            {
                new SettingsButtonV2
                {
                    Text = "Generate new briefing",
                    TooltipText = "Fetch a fresh briefing from the Torii API and display it now.",
                    Keywords = new[] { "torii", "briefing", "refresh", "generate", "daily" },
                    Action = () => briefingOverlay?.ForceBriefingRefresh(),
                },
                new SettingsButtonV2
                {
                    Text = "Show again last briefing",
                    TooltipText = "Re-open the most recent Torii briefing saved on this client.",
                    Keywords = new[] { "torii", "briefing", "last", "reopen", "history", "again" },
                    Action = () => briefingOverlay?.ShowLastBriefing(),
                },
                new SettingsButtonV2
                {
                    Text = "Replay last recalc",
                    TooltipText = "Re-show the gains and losses produced by the most recent server-side mass pp recalculation.",
                    Keywords = new[] { "torii", "briefing", "recalc", "replay", "mass", "pp-dev", "gains", "losses" },
                    Action = () => briefingOverlay?.ShowLastRecalcBriefing(),
                },
            });
        }
    }
}
