namespace GulagRunners.Sim
{
    /// <summary>
    /// Every tuning number for movement, in one place, in fixed point.
    /// The defaults are derived from the design docs rather than guessed:
    ///
    ///   RunSpeed      a room is about 4x body height = 7.2 m, and docs/03 budgets ~2 s to
    ///                 cross one, so 3.6 m/s.
    ///   Climb speeds  a floor is 2.5 m (docs/05) and docs/03 budgets ~1.5 s to change floor,
    ///                 including mounting the ladder.
    ///   Jump          apex is v*v/(2g) = 9.5^2 / 56 = 1.61 m, deliberately LOWER than the
    ///                 2.5 m floor height. That is a design decision, not a leftover: if a
    ///                 jump cleared a floor, ladders and hatches would be pointless and the
    ///                 fog-of-war topology of docs/05 would collapse.
    ///   Dodge         0.35 s of invulnerability, straight from docs/02.
    ///   Stamina       3 charges, 1.2 s each, straight from docs/02.
    /// </summary>
    public struct MoveConfig
    {
        public Fix RunSpeed;
        public Fix CrouchSpeed;
        public Fix GroundAccel;
        public Fix GroundDecel;
        public Fix AirAccel;
        public Fix AirDecel;

        public Fix Gravity;
        public Fix MaxFallSpeed;
        public Fix JumpSpeed;
        public Fix JumpCutMul;
        public int CoyoteFrames;
        public int JumpBufferFrames;

        public Fix ClimbUpSpeed;
        public Fix ClimbDownSpeed;
        /// <summary>How fast you edge sideways off a ladder.</summary>
        public Fix LadderDismountSpeed;

        public Fix DodgeSpeed;
        public int DodgeFrames;
        public int DodgeRecoverFrames;

        public int StaminaMax;
        public int StaminaRecoverFrames;

        public Fix BodyWidth;
        public Fix BodyHeight;
        public Fix CrouchHeight;

        /// <summary>Ledge assist: steps this high are climbed instead of blocking you.</summary>
        public Fix StepUpHeight;
        /// <summary>Ledge assist: a jump clipping a corner by less than this is nudged through.</summary>
        public Fix CornerCorrect;

        public int FallThroughFrames;

        /// <summary>Above this speed, footsteps are Loud rather than Quiet.</summary>
        public Fix LoudSpeed;
        public int StepNoiseFrames;
        /// <summary>Landing faster than this is a Loud landing.</summary>
        public Fix HardLandSpeed;

        public static MoveConfig Default()
        {
            MoveConfig c;
            c.RunSpeed    = Fix.FromMilli(3600);
            c.CrouchSpeed = Fix.FromMilli(1600);
            c.GroundAccel = Fix.FromMilli(40000);
            c.GroundDecel = Fix.FromMilli(50000);
            c.AirAccel    = Fix.FromMilli(22000);
            c.AirDecel    = Fix.FromMilli(10000);

            c.Gravity          = Fix.FromMilli(28000);
            c.MaxFallSpeed     = Fix.FromMilli(18000);
            c.JumpSpeed        = Fix.FromMilli(9500);
            c.JumpCutMul       = Fix.FromMilli(450);
            c.CoyoteFrames     = 6;   // 0.10 s
            c.JumpBufferFrames = 7;   // 0.12 s

            c.ClimbUpSpeed   = Fix.FromMilli(2000);
            c.ClimbDownSpeed = Fix.FromMilli(2600);
            c.LadderDismountSpeed = Fix.FromMilli(1500);

            c.DodgeSpeed         = Fix.FromMilli(7000);
            c.DodgeFrames        = 21;  // 0.35 s
            c.DodgeRecoverFrames = 9;

            c.StaminaMax            = 3;
            c.StaminaRecoverFrames  = 72; // 1.2 s

            c.BodyWidth   = Fix.FromMilli(600);
            c.BodyHeight  = Fix.FromMilli(1800);
            c.CrouchHeight = Fix.FromMilli(1100);

            c.StepUpHeight  = Fix.FromMilli(300);
            c.CornerCorrect = Fix.FromMilli(250);

            c.FallThroughFrames = 18;

            c.LoudSpeed       = Fix.FromMilli(2500);
            c.StepNoiseFrames = 18;  // a footstep every 0.3 s
            c.HardLandSpeed   = Fix.FromMilli(8000);
            return c;
        }
    }
}
