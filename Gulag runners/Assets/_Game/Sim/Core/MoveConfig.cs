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

        /// <summary>
        /// Let go of the button early and the rise is cut short by this. Off by default: a jump
        /// whose height depends on how long a thumb stayed on glass is a jump a player cannot
        /// aim, and every ledge in the arena is then a different distance depending on the tap.
        /// </summary>
        public bool VariableJumpHeight;
        public Fix JumpCutMul;
        public int CoyoteFrames;
        public int JumpBufferFrames;

        public Fix ClimbUpSpeed;
        public Fix ClimbDownSpeed;

        /// <summary>
        /// How fast the body is drawn back to the ladder's centre line after drifting off it. The
        /// grab itself no longer uses this — it eases on over several frames — so all this has left
        /// to undo is a sideways nudge, and it does it at climbing speed so it cannot be seen.
        /// </summary>
        public Fix LadderSnapSpeed;

        /// <summary>Clearance kept below the top of a ladder box, so climbing never leaves it.</summary>
        public Fix LadderTopMargin;

        /// <summary>
        /// The depth the fight happens at: the level's gameplay plane, which PlayerController
        /// fills in from the fighter's own Plane Z.
        ///
        /// Lanes exist so a fighter can step back onto a ramp or a walkway that was modelled
        /// behind the plane — not so he can take up residence there. Whenever the surface
        /// under his feet reaches this, it is where he stands.
        /// </summary>
        public Fix HomeDepth;

        /// <summary>How far to either side the climb-out looks for floor to step onto.</summary>
        public Fix MantleReach;

        /// <summary>
        /// Average speed of a scripted ladder move — the grab at the bottom of a ladder and the
        /// climb-out at the top. Both last as long as their own distance at this speed, so neither
        /// is ever a snap: the eased peak is one and a half times this, which at the default is
        /// exactly a run. A fixed duration cannot do that, because the distance is not fixed.
        /// </summary>
        public Fix ScriptSpeed;
        /// <summary>Floor and ceiling on a scripted move's length, in frames. The ceiling is what
        /// stops a long climb-out from taking a whole second; it is high enough that the peak stays
        /// below a dodge even at full reach.</summary>
        public int ScriptMinFrames;
        public int ScriptMaxFrames;

        /// <summary>
        /// Frames after letting go of a ladder during which it cannot be grabbed again. Without it,
        /// jumping off a ladder while still holding the stick up re-grabs it on the very next tick,
        /// which is the ladder trap wearing a different hat.
        /// </summary>
        public int LadderRegrabFrames;

        public Fix DodgeSpeed;
        public int DodgeFrames;
        public int DodgeRecoverFrames;
        /// <summary>Charges one dodge costs. More than one is what makes dodges rare.</summary>
        public int DodgeStaminaCost;

        /// <summary>
        /// Stamina is a pool of small charges rather than a count of dodges, because docs/02
        /// spends it on two things at once — the dodge and holding a guard — and those two cannot
        /// share a resource sensibly unless one of them can cost more than the other.
        /// </summary>
        public int StaminaMax;
        public int StaminaRecoverFrames;

        public Fix BodyWidth;

        /// <summary>
        /// How much of the level's DEPTH a body occupies. The fight is flat, so this is never
        /// moved through — it only decides which slice of a 3D location a body is standing on,
        /// and therefore which boxes are in its way. docs/02: the arena is 3D, the fight is not.
        /// </summary>
        public Fix BodyDepth;

        /// <summary>How fast a body slides between slices, in m/s. 0 snaps.</summary>
        public Fix DepthSnapSpeed;

        /// <summary>
        /// How far ahead to look for the next thing worth standing on, in metres. A body walks
        /// onto the slice of whatever it is about to climb: that is what lets one floor hold a
        /// ramp at one depth and a crate staircase at another, and a fighter use both.
        /// </summary>
        public Fix LaneLookahead;

        /// <summary>
        /// The furthest a body will shift in depth to meet something, in metres. Without it a
        /// fighter is pulled onto the slice of anything they could step onto, including the
        /// wall at the back of the building, and walks out of the fight into the scenery.
        /// </summary>
        public Fix LaneReach;
        public Fix BodyHeight;
        public Fix CrouchHeight;

        /// <summary>Ledge assist: steps this high are climbed instead of blocking you.</summary>
        public Fix StepUpHeight;

        /// <summary>
        /// The tallest ledge a standing body will haul itself onto without jumping. Anything
        /// over StepUpHeight costs most of the speed it was carrying, so a stepped slope is
        /// walked up at a crawl rather than hopped up one box at a time — and a kerb is still
        /// a kerb. 0 leaves only the free step.
        /// </summary>
        public Fix ClamberHeight;

        /// <summary>Share of the speed kept after hauling up. The cost that makes it read.</summary>
        public int ClamberSpeedPermille;
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
            c.VariableJumpHeight = false;
            c.JumpCutMul       = Fix.FromMilli(450);
            c.CoyoteFrames     = 6;   // 0.10 s
            c.JumpBufferFrames = 7;   // 0.12 s

            c.ClimbUpSpeed   = Fix.FromMilli(2000);
            c.ClimbDownSpeed = Fix.FromMilli(2600);
            c.LadderSnapSpeed = Fix.FromMilli(2000);
            c.LadderTopMargin = Fix.FromMilli(60);
            c.MantleReach = Fix.FromMilli(1600);
            c.ScriptSpeed = Fix.FromMilli(2400);
            c.ScriptMinFrames = 10;       // 0.17 s
            c.ScriptMaxFrames = 30;       // 0.50 s
            c.LadderRegrabFrames = 12;    // 0.20 s

            c.DodgeSpeed         = Fix.FromMilli(4800);   // 1.68 m, down from 2.45
            c.DodgeFrames        = 21;  // 0.35 s of invulnerability, straight from docs/02
            c.DodgeRecoverFrames = 9;
            c.DodgeStaminaCost   = 2;      // two dodges back to back, then 2.4 s of waiting

            c.StaminaMax            = 4;   // docs/02 says 3; 4 so a block can cost 1 and a
                                           // dodge 2, which is the "two in a row" the dodge
                                           // was retuned to in the movement pass
            c.StaminaRecoverFrames  = 72;  // 1.2 s a charge, straight from docs/02

            c.BodyWidth   = Fix.FromMilli(600);
            c.BodyDepth   = Fix.FromMilli(600);
            c.DepthSnapSpeed = Fix.FromMilli(4000);
            c.LaneLookahead  = Fix.FromMilli(1200);
            c.LaneReach      = Fix.FromMilli(2000);
            c.BodyHeight  = Fix.FromMilli(1800);
            c.CrouchHeight = Fix.FromMilli(1100);

            c.StepUpHeight  = Fix.FromMilli(300);
            c.ClamberHeight = Fix.FromMilli(550);   // one crate of this location's staircases
            c.ClamberSpeedPermille = 400;
            c.CornerCorrect = Fix.FromMilli(250);

            c.FallThroughFrames = 18;

            c.LoudSpeed       = Fix.FromMilli(2500);
            c.StepNoiseFrames = 18;  // a footstep every 0.3 s
            c.HardLandSpeed   = Fix.FromMilli(8000);
            return c;
        }
    }
}
