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

            return flags;
        }
    }
}
