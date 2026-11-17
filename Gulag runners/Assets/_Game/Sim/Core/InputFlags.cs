using System;

namespace GulagRunners.Sim
{
    /// <summary>
    /// The BUTTONS of one tick. Where the weapon points is not in here -- that is two
    /// signed thousandths on <see cref="TickInput"/>, which carries this alongside it.
    ///
    /// There is no Attack bit any more, and that is the whole of docs/31 in one deletion.
    /// A blow is not a press; it is the tip of a weapon moving into somebody. There are no
    /// AimLeft/AimRight bits either: they were a one-trit stand-in for a stick, and now
    /// there is a stick.
    ///
    /// Six bits, which fits a byte again. docs/06 budgeted one byte for intent at 60 Hz;
    /// the aim costs two more, and 3 bytes a tick is about 3 KB/s with redundancy -- still
    /// a rounding error on any connection the game can be played on.
    /// </summary>
    [Flags]
    public enum InputFlags : ushort
    {
        None   = 0,
        Left   = 1 << 0,
        Right  = 1 << 1,
        Up     = 1 << 2,   // climb / mount ladder
        Down   = 1 << 3,   // crouch / descend / drop through a one-way platform
        Jump   = 1 << 4,
        /// <summary>
        /// The one defensive button, and now it only has one job. It used to carry both
        /// the dodge and the guard; the guard is the weapon's job now -- a blade is
        /// stopped by another blade -- so this is purely the dodge, and the dodge is
        /// switched on, which it had not been since the movement pass.
        /// </summary>
        Dodge  = 1 << 5,
        Action = 1 << 6,   // pick up, open a chest, grab

        /// <summary>
        /// Which thing on a lock was touched this tick, 1..7, with 0 for "nothing".
        ///
        /// Three bits rather than a field of its own, and that is not a trick: the flags
        /// already travel as an INT on the wire (LanLink), so bits 8 to 10 cost nothing at
        /// all -- a puzzle touch adds zero bytes to a packet at 60 Hz. docs/04 never needs
        /// more than six options on one lock, so three bits is one spare.
        ///
        /// Touches go through the input stream like every other intent because of rollback:
        /// anything the simulation reads that was not in the stored input is a desync
        /// waiting for a bad connection, and "which rune did he tap" is exactly that.
        /// </summary>
        Choice0 = 1 << 8,
        Choice1 = 1 << 9,
        Choice2 = 1 << 10,
        ChoiceMask = Choice0 | Choice1 | Choice2,
    }

    public static class InputFlagsExtensions
    {
        public static bool Has(this InputFlags f, InputFlags bit) => (f & bit) != 0;

        /// <summary>-1, 0 or +1. Pressing both directions at once cancels out, deterministically.</summary>
        public static int MoveX(this InputFlags f)
        {
            int x = 0;
            if (f.Has(InputFlags.Left)) x -= 1;
            if (f.Has(InputFlags.Right)) x += 1;
            return x;
        }

        public static int MoveY(this InputFlags f)
        {
            int y = 0;
            if (f.Has(InputFlags.Down)) y -= 1;
            if (f.Has(InputFlags.Up)) y += 1;
            return y;
        }

        /// <summary>Which thing on the lock was touched, 1..7. Zero for none.</summary>
        public static int Choice(this InputFlags f) => ((int)(f & InputFlags.ChoiceMask)) >> 8;

        /// <summary>
        /// The same flags carrying a touch. Out of range is no touch at all rather than a
        /// wrapped one: a touch on option nine is a bug upstream, and a lock that answered it
        /// as option one would hide that bug behind a wrong answer.
        /// </summary>
        public static InputFlags WithChoice(this InputFlags f, int option)
        {
            InputFlags bare = f & ~InputFlags.ChoiceMask;
            if (option < 1 || option > 7) return bare;
            return bare | (InputFlags)(option << 8);
        }
    }
}
