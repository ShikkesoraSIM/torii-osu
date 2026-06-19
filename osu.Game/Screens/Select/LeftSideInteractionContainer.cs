// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Input.Events;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// Handles mouse interactions required when moving away from the carousel.
    /// </summary>
    internal partial class LeftSideInteractionContainer : Container
    {
        private readonly Action? resetCarouselPosition;

        // Torii: forwards a drag that starts in this (empty, in legacy mode) left area into the carousel
        // scroll, and a predicate gating that to legacy mode only. Without this, the container eats the
        // mouse-down (OnMouseDown => true) so the carousel never gets the drag, making the empty left
        // area a drag dead-zone.
        private readonly Action<float>? forwardScroll;
        private readonly Func<bool>? canForwardDrag;

        private bool mouseContained;
        private bool isDragging;

        private InputManager inputManager = null!;

        public LeftSideInteractionContainer(Action resetCarouselPosition, Action<float>? forwardScroll = null, Func<bool>? canForwardDrag = null)
        {
            this.resetCarouselPosition = resetCarouselPosition;
            this.forwardScroll = forwardScroll;
            this.canForwardDrag = canForwardDrag;
        }

        // we want to block plain scrolls on the left side so that they don't scroll the carousel,
        // but also we *don't* want to handle scrolls when they're combined with keyboard modifiers
        // as those will usually correspond to other interactions like adjusting volume.
        protected override bool OnScroll(ScrollEvent e) => !e.ControlPressed && !e.AltPressed && !e.ShiftPressed && !e.SuperPressed;

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        // Only claim the drag when forwarding is allowed (legacy mode); otherwise leave behaviour as-is.
        protected override bool OnDragStart(DragStartEvent e) => canForwardDrag?.Invoke() == true;

        protected override void OnDrag(DragEvent e)
        {
            isDragging = true;
            forwardScroll?.Invoke(e.Delta.Y);
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            isDragging = false;
            base.OnDragEnd(e);
        }

        protected override void LoadComplete()
        {
            inputManager = GetContainingInputManager()!;
            base.LoadComplete();
        }

        protected override void Update()
        {
            base.Update();

            // Don't snap the carousel back to the selection while drag-scrolling from here (would fight it).
            if (isDragging)
                return;

            // torii: en modo legacy esta zona izquierda es para drag-scrollear el carousel y browsear.
            // si dejamos el "volver a la seleccion" al hoverear, te tira el scroll de vuelta a la cancion
            // actual cada vez que pasas el mouse por ahi (lo que el usuario veia como "la leaderboard me
            // resetea el carousel"). asi que en legacy desactivamos el reset por hover.
            if (canForwardDrag?.Invoke() == true)
            {
                mouseContained = Contains(inputManager.CurrentState.Mouse.Position);
                return;
            }

            // We want to trigger an action whenever the cursor is in the left area of song select.
            // Other elements in song select handle input, so rather than using `OnHover` let's check the true mouse position.
            if (Contains(inputManager.CurrentState.Mouse.Position))
            {
                if (!mouseContained)
                {
                    mouseContained = true;
                    resetCarouselPosition?.Invoke();
                }
            }
            else
            {
                mouseContained = false;
            }
        }
    }
}
