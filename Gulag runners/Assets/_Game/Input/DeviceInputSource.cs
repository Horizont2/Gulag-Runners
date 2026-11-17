using UnityEngine;
using UnityEngine.InputSystem;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Keyboard and gamepad, for working in the editor. Two keyboard schemes so both players
    /// can be driven from one machine while testing; on a phone each player has their own
    /// device and only uses the on-screen controls.
    ///
    /// Reads devices directly rather than through the generated action class, so the project
    /// compiles whether or not InputSystem_Actions has C# generation switched on.
    /// </summary>
    public sealed class DeviceInputSource : MonoBehaviour, IPlayerInputSource
    {
        public enum Scheme
        {
            /// <summary>WASD, Space, Left Shift (block / dodge), E, J.</summary>
            Primary = 0,
            /// <summary>Arrows, Right Ctrl, Right Shift (block / dodge), Numpad 0, Numpad 1.</summary>
            Secondary = 1
        }

        [Tooltip("Primary drives player 1, Secondary player 2, so a local two-player test " +
                 "needs only one keyboard.")]
        public Scheme scheme = Scheme.Primary;

        [Tooltip("Which gamepad to read. 0 is the first pad, 1 the second. -1 ignores gamepads.")]
        public int gamepadIndex;

        [Tooltip("Mouse left button counts as attack. Turn this off for player 2.")]
        public bool useMouseForAttack = true;

        public TickInput Read()
        {
            InputFlags flags = InputFlags.None;
            int aimX = 0, aimY = 0;

            Keyboard k = Keyboard.current;
            if (k != null)
            {
                if (scheme == Scheme.Primary)
                {
                    if (k.aKey.isPressed) flags |= InputFlags.Left;
                    if (k.dKey.isPressed) flags |= InputFlags.Right;
                    if (k.wKey.isPressed) flags |= InputFlags.Up;
                    if (k.sKey.isPressed) flags |= InputFlags.Down;
                    if (k.spaceKey.isPressed) flags |= InputFlags.Jump;
                    if (k.leftShiftKey.isPressed) flags |= InputFlags.Dodge;
                    if (k.eKey.isPressed) flags |= InputFlags.Action;

                    // The attack stick, on a desk. Eight directions off IJKL, because a
                    // key has no magnitude and the arm wants a direction: holding two
                    // gives the diagonal, which is where most of a swing happens.
                    if (k.jKey.isPressed) aimX -= 1000;
                    if (k.lKey.isPressed) aimX += 1000;
                    if (k.iKey.isPressed) aimY += 1000;
                    if (k.kKey.isPressed) aimY -= 1000;
                }
                else
                {
                    if (k.leftArrowKey.isPressed) flags |= InputFlags.Left;
                    if (k.rightArrowKey.isPressed) flags |= InputFlags.Right;
                    if (k.upArrowKey.isPressed) flags |= InputFlags.Up;
                    if (k.downArrowKey.isPressed) flags |= InputFlags.Down;
                    if (k.rightCtrlKey.isPressed) flags |= InputFlags.Jump;
                    if (k.rightShiftKey.isPressed) flags |= InputFlags.Dodge;
                    if (k.numpad0Key.isPressed) flags |= InputFlags.Action;
                    if (k.numpad4Key.isPressed) aimX -= 1000;
                    if (k.numpad6Key.isPressed) aimX += 1000;
                    if (k.numpad8Key.isPressed) aimY += 1000;
                    if (k.numpad2Key.isPressed) aimY -= 1000;
                }
            }

            Gamepad pad = null;
            if (gamepadIndex >= 0 && Gamepad.all.Count > gamepadIndex)
                pad = Gamepad.all[gamepadIndex];

            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                const float dead = 0.5f;          // a hard threshold: the sim takes bits, not analog
                if (stick.x < -dead) flags |= InputFlags.Left;
                if (stick.x > dead) flags |= InputFlags.Right;
                if (stick.y > dead) flags |= InputFlags.Up;
                if (stick.y < -dead) flags |= InputFlags.Down;
                if (pad.buttonSouth.isPressed) flags |= InputFlags.Jump;
                if (pad.rightShoulder.isPressed || pad.buttonEast.isPressed) flags |= InputFlags.Dodge;
                if (pad.buttonNorth.isPressed) flags |= InputFlags.Action;

                // The RIGHT stick IS the weapon now -- the pad's version of the second
                // thumb, and analog all the way through rather than thresholded, because
                // the arm follows it continuously.
                Vector2 aim = pad.rightStick.ReadValue();
                if (aim.sqrMagnitude > 0.0001f)
                {
                    aimX = Mathf.RoundToInt(Mathf.Clamp(aim.x, -1f, 1f) * 1000f);
                    aimY = Mathf.RoundToInt(Mathf.Clamp(aim.y, -1f, 1f) * 1000f);
                }
            }

            return new TickInput(flags, aimX, aimY);
        }
    }
}
