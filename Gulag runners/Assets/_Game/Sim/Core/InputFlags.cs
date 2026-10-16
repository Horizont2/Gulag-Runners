using System;

namespace GulagRunners.Sim
{
    /// <summary>
    /// One tick of player intent, packed into two bytes.
    ///
    /// It was one byte, and docs/06-tech-and-netcode.md budgeted exactly that: 60 Hz plus
    /// redundancy is about 2 KB/s on mobile data. WHERE A FIGHTER AIMS cost the ninth bit.
    /// A side-view fighter has two thumbs doing two independent things — the left one says
    /// which way the body travels, the right one which way the blade goes — and once those
    /// can disagree there is no eighth bit left to say so. Two bytes is 4 KB/s, which is the
    /// honest price and still a rounding error on any connection the game can be played on.
    ///
    /// The aim bits are OPTIONAL, which is what makes this cost nothing anywhere else: no
    /// aim bit means the aim follows the travel, exactly as it did when there were eight.
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
        /// The one defensive button. docs/02 gives the medieval fighter a block and no roll
        /// button, and one bit has to carry both: pressed with a direction held it is a dodge,
        /// pressed standing still it raises the guard, and in the air it is neither.
        /// </summary>
        Dodge  = 1 << 5,
        Action = 1 << 6,   // pick up, open a chest, grab
        Attack = 1 << 7,

        /// <summary>
        /// Aim the blow this way, whichever way the body is travelling.
        ///
        /// This is the swipe, not the stick. Holding left and striking right is a fighter
        /// backing away while he keeps the other one off, and it is most of what separates
        /// a fight from two people taking turns walking into each other. Neither bit set
        /// means what it has always meant: the blow goes where the body is going.
        /// </summary>
        AimLeft  = 1 << 8,
        AimRight = 1 << 9
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

        /// <summary>
        /// -1, 0 or +1: which way the blow is aimed, or 0 for "wherever the body is going".
        /// Both at once cancels out, the same way the movement bits do.
        /// </summary>
        public static int AimX(this InputFlags f)
        {
            int x = 0;
            if (f.Has(InputFlags.AimLeft)) x -= 1;
            if (f.Has(InputFlags.AimRight)) x += 1;
            return x;
        }

        public static int MoveY(this InputFlags f)
        {
            int y = 0;
            if (f.Has(InputFlags.Down)) y -= 1;
            if (f.Has(InputFlags.Up)) y += 1;
            return y;
        }
    }
}
