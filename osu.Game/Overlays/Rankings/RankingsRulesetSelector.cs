// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Overlays.Profile;
using osu.Game.Overlays.Profile.Header.Components;
using osu.Game.Rulesets;

namespace osu.Game.Overlays.Rankings
{
    /// <summary>
    /// torii: el selector de ruleset de la pagina de rankings, con los modos exclusivos del server
    /// (osu!relax, osu!autopilot, taiko relax) como tabs propios intercalados despues de su ruleset base
    /// — igual que en los perfiles. Al elegir un variant, <see cref="Online.API.Requests.GetRankingsRequest"/>
    /// pega a rankings/{ShortName} (osurx/osuap/taikorx) y g0v0 devuelve el ranking de ese modo. Reusa el
    /// registry de variants y el tab item con label del perfil. NUNCA asigna al ruleset global del juego:
    /// el RankingsOverlay tiene su propio bindable local.
    /// </summary>
    public partial class RankingsRulesetSelector : OverlayRulesetSelector
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            // re-armamos la lista que ya poblo el selector base (solo los 4 legacy) con los variants
            // intercalados: osu, osu!relax, osu!autopilot, taiko, taiko relax, fruits, mania.
            var tabs = ProfileRulesetVariants.BuildProfileTabs(Rulesets);

            foreach (var item in Items.ToList())
                RemoveItem(item);

            foreach (var tab in tabs)
                AddItem(tab);
        }

        protected override TabItem<RulesetInfo> CreateTabItem(RulesetInfo value) => new ProfileRulesetTabItem(value);
    }
}
