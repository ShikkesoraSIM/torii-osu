// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests;

namespace osu.Game.Online.API
{
    /// <summary>
    /// Raised in place of a generic login failure when the server reports the account
    /// is restricted (a 403 on the user fetch). Carries the restriction details so the
    /// UI can explain it - the login form shows <see cref="System.Exception.Message"/>,
    /// and ToriiRestrictionOverlay pops a full briefing from <see cref="Restriction"/> -
    /// instead of leaving the user with a blank "couldn't log in".
    /// </summary>
    public class RestrictedAccountException : APIException
    {
        public readonly APIToriiUserRestriction Restriction;

        public RestrictedAccountException(APIToriiUserRestriction restriction)
            : base(string.IsNullOrWhiteSpace(restriction.Reason)
                ? "Your account is restricted. Contact the admins on our Discord."
                : $"Your account is restricted: {restriction.Reason}", null)
        {
            Restriction = restriction;
        }
    }
}
