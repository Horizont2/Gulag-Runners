using System;

namespace GulagRunners.Sim
{
    /// <summary>
    /// One tick of player intent, packed into a single byte.
    /// docs/06-tech-and-netcode.md budgets exactly this: 1 byte per tick per player, so
    /// 60 Hz of input plus redundancy stays around 2 KB/s even on mobile data.
    /// </summary>
    [Flags]
    public enum InputFlags : byte
    {
        None   = 0,
        Left   = 1 << 0,
        Right  = 1 << 1,
        Up     = 1 << 2,   // climb / mount ladder
        Down   = 1 << 3,   // crouch / descend / drop through a one-way platform
        Jump   = 1 << 4,
        Dodge  = 1 << 5,   // the dodge into depth; block in medieval mode
        Action = 1 << 6,   // pick up, open a chest, grab
        Attack = 1 << 7
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
    }
}
