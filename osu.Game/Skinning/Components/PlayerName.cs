// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Screens.Play;

namespace osu.Game.Skinning.Components
{
    [UsedImplicitly]
    public partial class PlayerName : FontAdjustableSkinComponent
    {
        private readonly OsuSpriteText text;

        [Resolved]
        private GameplayState? gameplayState { get; set; }

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private IBindable<APIUser>? apiUser;

        public PlayerName()
        {
            AutoSizeAxes = Axes.Both;

            text = new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            APIUser? user;

            if (gameplayState != null)
            {
                user = gameplayState.Score.ScoreInfo.User;
                text.Text = user.Username;
            }
            else
            {
                user = api.LocalUser.Value;
                apiUser = api.LocalUser.GetBoundCopy();
                apiUser.BindValueChanged(u => text.Text = u.NewValue.Username, true);
            }

            // Base colour parity with the leaderboards: the role / equipped colour,
            // falling back to white. SetTextColour (skin setting) overrides this base
            // when configured; the aura repaints it live for equipped name colours.
            text.Colour = ToriiColourHelper.GetTopColour(user) ?? Colour4.White;

            // Torii: wrap the name SpriteText (not this component) with the aura, so
            // the user's aura + name/role colour ride behind their gameplay-HUD name.
            // SetFont/SetTextColour still operate on `text`; the aura repaints the
            // colour live each frame for users with an equipped name colour.
            InternalChild = UserAuraContainer.Wrap(user, text);
        }

        protected override void SetFont(FontUsage font) => text.Font = font.With(size: 40);

        protected override void SetTextColour(Colour4 textColour) => text.Colour = textColour;
    }
}
