using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The phone scheme: two sticks and two buttons.
    ///
    /// The left stick walks. THE RIGHT STICK IS THE WEAPON -- it does not trigger a swing,
    /// it points the arm, and whatever the blade touches while it is moving gets hurt
    /// (docs/31). That is why there is no attack button any more: an attack button is a
    /// request for the game to play an animation at you, and the whole problem docs/30
    /// measured was the half second between the request and anything happening.
    ///
    /// What is left of the buttons is jump and action. The guard is gone because a weapon
    /// is stopped by another weapon, and the dodge moved onto the right stick's shoulder
    /// as a tap -- see <see cref="dodgeButton"/>.
    ///
    /// Leave a reference empty and that control is simply absent.
    /// </summary>
    public sealed class TouchInputSource : MonoBehaviour, IPlayerInputSource
    {
        [Header("The on-screen controls (docs/31)")]
        [Tooltip("Optional. Left thumb: walks, climbs, crouches. Leave it empty and " +
                 "that control is simply absent, which is what the prefabs do -- the " +
                 "HUD builder fills these in on the scene's local fighter.")]
        public VirtualJoystick stick;

        [Tooltip("Optional. Right thumb, and the whole of the fight.\n\n" +
                 "Its DIRECTION is where the arm points and its PUSH is how far out the " +
                 "arm reaches. Nothing here starts an attack: the arm follows this every " +
                 "tick, and damage comes from how fast the weapon's tip is moving when it " +
                 "touches somebody. Flick it and you swing; hold it steady between you " +
                 "and them and you are guarding, because their blade meets yours.")]
        public VirtualJoystick aimStick;

        [Tooltip("Optional. See Stick.")]
        public VirtualButton jumpButton;

        [Tooltip("Optional. Dodge: a roll with invulnerable frames, for two stamina.")]
        public VirtualButton dodgeButton;

        [Tooltip("Optional. Pick up, open a chest.")]
        public VirtualButton actionButton;

        [Tooltip("Pushing the walk stick up past this also counts as climb.")]
        [Range(0.2f, 0.95f)] public float verticalThreshold = 0.5f;

        public TickInput Read()
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

            int aimX = 0, aimY = 0;
            if (aimStick != null)
            {
                // Quantised here, at the edge, and never again. A float from a touch
                // position is the one value in the chain that could differ between two
                // devices, so it becomes an integer before the simulation sees it.
                Vector2 a = aimStick.Value;
                aimX = Mathf.RoundToInt(Mathf.Clamp(a.x, -1f, 1f) * 1000f);
                aimY = Mathf.RoundToInt(Mathf.Clamp(a.y, -1f, 1f) * 1000f);
            }

            return new TickInput(flags, aimX, aimY);
        }
    }
}
