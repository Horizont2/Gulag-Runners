using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The mobile scheme from docs/02: one stick plus three buttons, nothing more.
    /// Leave a reference empty and that control is simply absent.
    /// </summary>
    public sealed class TouchInputSource : MonoBehaviour, IPlayerInputSource
    {
        [Header("The on-screen controls (docs/02)")]
        [Tooltip("Optional, like every slot here: leave it empty and that control is simply " +
                 "absent. All five are empty in this build, which has no runtime interface by " +
                 "design — the keyboard source drives the fighters instead, and these are " +
                 "waiting for the phone build rather than missing.")]
        public VirtualJoystick stick;

        [Tooltip("Optional. See Stick.")]
        public VirtualButton attackButton;

        [Tooltip("Optional. The attack thumb: a flick that both swings and AIMS.\n\n" +
                 "This is the control the aim/travel split was built for. A button can only " +
                 "say \"attack\", and then the blade can only go where the feet are going; a " +
                 "flick says which way, so a fighter can back off and keep the other one " +
                 "off at the same time. Both can be wired at once — the button is then an " +
                 "unaimed swing, which is what a tap on the area does anyway.")]
        public SwipeArea swipeArea;

        [Tooltip("Optional. See Stick.")]
        public VirtualButton jumpButton;

        [Tooltip("Optional. See Stick.")]
        public VirtualButton dodgeButton;

        [Tooltip("Optional. See Stick.")]
        public VirtualButton actionButton;

        [Tooltip("Pushing the stick up past this also counts as climb.")]
        [Range(0.2f, 0.95f)] public float verticalThreshold = 0.5f;

        public InputFlags Read()
        {
            InputFlags flags = InputFlags.None;

            if (stick != null)
            {
                Vector2 v = stick.Value;
                if (v.x < -0.2f) flags |= InputFlags.Left;
                if (v.x > 0.2f) flags |= InputFlags.Right;
                if (v.y > verticalThreshold) flags |= InputFlags.Up;
                if (v.y < -verticalThreshold) flags |= InputFlags.Down;
            }

            if (jumpButton != null && jumpButton.Pressed) flags |= InputFlags.Jump;
            if (dodgeButton != null && dodgeButton.Pressed) flags |= InputFlags.Dodge;
            if (actionButton != null && actionButton.Pressed) flags |= InputFlags.Action;
            if (attackButton != null && attackButton.Pressed) flags |= InputFlags.Attack;

            // Taken rather than read: a swipe is an event and this is polled once a tick, so
            // the area hands it over once and forgets it. The swing itself is latched by the
            // combat motor, which is why one tick of Attack is enough to start a whole one.
            if (swipeArea != null && swipeArea.Take(out int aim))
            {
                flags |= InputFlags.Attack;
                if (aim > 0) flags |= InputFlags.AimRight;
                else if (aim < 0) flags |= InputFlags.AimLeft;
            }

            return flags;
        }
    }
}
