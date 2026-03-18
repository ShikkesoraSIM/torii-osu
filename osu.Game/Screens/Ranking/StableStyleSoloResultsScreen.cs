// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Scoring;

namespace osu.Game.Screens.Ranking
{
    /// <summary>
    /// Dedicated entry point for stable-style results rendering.
    /// This currently inherits the existing solo results behaviour and allows
    /// iterative stable-style visual parity work behind a runtime toggle.
    /// </summary>
    public partial class StableStyleSoloResultsScreen : SoloResultsScreen
    {
        public StableStyleSoloResultsScreen(ScoreInfo score)
            : base(score)
        {
        }
    }
}
