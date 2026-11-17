namespace GulagRunners.Sim
{
    /// <summary>
    /// How loot leaves a chest or a hand and how it behaves once it is on the floor.
    ///
    /// The arc is simulated rather than animated, because where an item lands changes who can
    /// reach it first — that is a gameplay fact and both devices have to agree on it.
    /// </summary>
    public struct DropConfig
    {
        /// <summary>Sideways and upward speed an item leaves a chest with.</summary>
        public Fix PopSpeedX;
        public Fix PopSpeedY;

        /// <summary>The same for an item displaced out of a slot: a short hop, not a throw.</summary>
        public Fix DropSpeedX;
        public Fix DropSpeedY;

        public Fix Gravity;
        public Fix MaxFallSpeed;

        /// <summary>How much speed a bounce keeps. Zero lands dead, which reads as heavy.</summary>
        public Fix Bounce;

        /// <summary>
        /// Half the size of the item itself: what rests on the floor and what a wall
        /// stops. Not how close you have to be to take it -- see <see cref="PickupReach"/>.
        ///
        /// They were one number, and that is why the pickup box could not be widened
        /// without the item also sitting higher off the ground and bumping into walls
        /// sooner. A thing's size and the distance you can reach it from are two
        /// different facts.
        /// </summary>
        public Fix PickupRadius;

        /// <summary>
        /// Half the size of the box you walk through to take it. Measured from the item,
        /// against the fighter's whole body, so the reach past his skin is this plus
        /// half his width.
        /// </summary>
        public Fix PickupReach;

        /// <summary>Frames before an item that just came out of a chest can be taken.</summary>
        public int PopLockFrames;

        /// <summary>
        /// Frames before an item you just dropped can be taken again. Longer, on purpose: a swap
        /// has to be a commitment for a moment, or it is a free look at both items.
        /// </summary>
        public int DropLockFrames;

        public static DropConfig Default()
        {
            DropConfig c;
            c.PopSpeedX = Fix.FromMilli(1400);
            c.PopSpeedY = Fix.FromMilli(3600);
            c.DropSpeedX = Fix.FromMilli(1100);
            c.DropSpeedY = Fix.FromMilli(2200);
            c.Gravity = Fix.FromMilli(22000);
            c.MaxFallSpeed = Fix.FromMilli(14000);
            c.Bounce = Fix.FromMilli(150);
            c.PickupRadius = Fix.FromMilli(220);

            // Half a body width wider than the item itself. Against a 0.30 m body that
            // is 0.48 m of reach from the middle of the fighter, where it used to be
            // 0.37 -- enough that walking past something takes it, rather than walking
            // exactly over it.
            c.PickupReach = Fix.FromMilli(330);

            c.PopLockFrames = 14;      // 0.23 s: it has to land before it is yours
            c.DropLockFrames = 30;     // 0.50 s
            return c;
        }
    }
}
