using UnityEngine;
using UnityEngine.InputSystem;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Keyboard and gamepad, for working in the editor.
    /// Reads the devices directly rather than through the generated action class so the
    /// project compiles whether or not InputSystem_Actions has C# generation turned on.
    ///
    ///   A / D / arrows   move            W / up      climb up
    ///   S / down         crouch, descend, drop through a one-way platform
    ///   Space            jump            Shift       dodge
    ///   E                action          Mouse 0 / J attack
    /// </summary>
    public sealed class DeviceInputSource : MonoBehaviour, IPlayerInputSource
    {
        public InputFlags Read()
        {
            InputFlags flags = InputFlags.None;

            Keyboard k = Keyboard.current;
            if (k != null)
            {
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) flags |= InputFlags.Left;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) flags |= InputFlags.Right;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) flags |= InputFlags.Up;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) flags |= InputFlags.Down;
                if (k.spaceKey.isPressed) flags |= InputFlags.Jump;
                if (k.leftShiftKey.isPressed) flags |= InputFlags.Dodge;
                if (k.eKey.isPressed) flags |= InputFlags.Action;
                if (k.jKey.isPressed) flags |= InputFlags.Attack;
            }

            Gamepad g = Gamepad.current;
            if (g != null)
            {
                Vector2 stick = g.leftStick.ReadValue();
                const float dead = 0.5f;                  // a hard threshold: the sim takes bits, not analog
                if (stick.x < -dead) flags |= InputFlags.Left;
                if (stick.x > dead) flags |= InputFlags.Right;
                if (stick.y > dead) flags |= InputFlags.Up;
                if (stick.y < -dead) flags |= InputFlags.Down;
                if (g.buttonSouth.isPressed) flags |= InputFlags.Jump;
                if (g.rightShoulder.isPressed || g.buttonEast.isPressed) flags |= InputFlags.Dodge;
                if (g.buttonNorth.isPressed) flags |= InputFlags.Action;
                if (g.buttonWest.isPressed) flags |= InputFlags.Attack;
            }

            Mouse m = Mouse.current;
            if (m != null && m.leftButton.isPressed) flags |= InputFlags.Attack;

            return flags;
        }
    }
}
