// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Game.Graphics.Backgrounds;

namespace osu.Game.Rulesets.Osu.Skinning.Default
{
    public partial class TrianglesPiece : Triangles
    {
        protected override bool CreateNewTriangles => false;

        // Triangles.SpawnRatio is now a public property (settable) so the
        // matchmaking RankedPlayBackground can dial it per-instance. Kept
        // an override here that just hard-pins the value at 0.5 so the
        // skinned slider/circle triangle pieces keep their subtle look
        // regardless of consumer-side mutation.
        public override float SpawnRatio
        {
            get => 0.5f;
            set { /* fixed for the slider/circle skin piece */ }
        }

        public TrianglesPiece(int? seed = null)
            : base(seed)
        {
            TriangleScale = 1.2f;
            HideAlphaDiscrepancies = false;
            ClampAxes = Axes.None;
        }

        protected override void Update()
        {
            if (IsPresent)
                base.Update();
        }
    }
}
