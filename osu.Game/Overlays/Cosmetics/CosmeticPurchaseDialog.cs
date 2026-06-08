// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Graphics.Sprites;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Confirmation prompt shown before an expensive cosmetic purchase,
    /// so a fat-finger doesn't drain a big points balance.</summary>
    public partial class CosmeticPurchaseDialog : PopupDialog
    {
        public CosmeticPurchaseDialog(string itemName, int price, Action onConfirm)
        {
            HeaderText = $"Buy {itemName}?";
            BodyText = $"This spends {price:N0} points. Purchases can't be undone.";

            Icon = FontAwesome.Solid.Coins;

            Buttons = new PopupDialogButton[]
            {
                new PopupDialogOkButton
                {
                    Text = $"Buy  ·  {price:N0} pts",
                    Action = onConfirm,
                },
                new PopupDialogCancelButton
                {
                    Text = "Cancel",
                },
            };
        }

        /// <summary>Always confirm a purchase before spending (when a dialog
        /// overlay is available — otherwise just buy).</summary>
        public static void Prompt(IDialogOverlay dialog, string itemName, int price, Action onConfirm)
        {
            if (dialog != null)
                dialog.Push(new CosmeticPurchaseDialog(itemName, price, onConfirm));
            else
                onConfirm();
        }
    }
}
