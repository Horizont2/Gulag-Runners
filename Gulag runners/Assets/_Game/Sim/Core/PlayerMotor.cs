namespace GulagRunners.Sim
{
    /// <summary>
    /// The movement simulation for one player.
    ///
    /// Pure, deterministic and engine-free: given the same state, input and world it produces
    /// the same result on every device, which is what rollback netcode needs (docs/06).
    /// Nothing here may use float, UnityEngine, Time, or any random source.
    ///
    /// It implements the movement described in docs/02-combat-and-controls.md:
    ///   - one flat gameplay plane, depth is presentation only
    ///   - stick left/right to move, up/down to climb ladders and hatches, a small jump button
    ///   - a dodge with invulnerability frames, paid for with stamina
    ///   - ledge assist so nobody falls because of the thickness of a thumb
    ///   - noise as simulation state, because during the scavenge phase sound is the only way
    ///     to know where the other player is (docs/01)
    /// </summary>
    public static class PlayerMotor
    {
        public const int TicksPerSecond = 60;

        /// <summary>One tick. Fixed step: the simulation never sees a variable delta time.</summary>
        public static readonly Fix Dt = new Fix(Fix.RawOne / TicksPerSecond);

        /// <summary>Separation kept after resolving a collision, so the body never re-overlaps.</summary>
        static readonly Fix Skin = new Fix(16);           // ~0.00024 m
        static readonly Fix GroundProbe = Fix.FromMilli(20);
        static readonly Fix GroundStick = Fix.FromMilli(200);

        public static void Step(ref PlayerSimState s, InputFlags input, SimWorld world, in MoveConfig cfg)
        {
            s.Noise = NoiseLevel.Silent;

            TickTimers(ref s, cfg);

            int wishX = input.MoveX();
            int wishY = input.MoveY();

            bool jumpHeldNow = input.Has(InputFlags.Jump);
            if (jumpHeldNow && !s.JumpHeld) s.JumpBufferTimer = cfg.JumpBufferFrames;
            s.JumpHeld = jumpHeldNow;

            if (wishX != 0) s.Facing = (sbyte)wishX;

            // Both scripted moves — the grab at the bottom of a ladder and the climb-out at the
            // top — run before anything else, because while one is playing the body is being
            // walked along a path and nothing else may touch it.
            if (s.Mode == MoveMode.Mantling || s.Mode == MoveMode.Mounting)
            {
                StepScripted(ref s, world, cfg);
                return;
            }

            if (s.Mode == MoveMode.Dodging)
            {
                StepDodge(ref s, world, cfg);
                return;
            }

            if (TryStartDodge(ref s, input, wishX, cfg))
            {
                StepDodge(ref s, world, cfg);
                return;
            }

            if (s.Mode == MoveMode.Climbing)
            {
                StepClimb(ref s, wishX, wishY, world, cfg);
                return;
            }

            if (TryMountLadder(ref s, wishY, world, cfg))
            {
                // A grab that has distance to cover plays its reach first; one that starts
                // already on the centre line goes straight to climbing, so standing at a ladder
                // and pressing up never costs a frame it does not need to.
                if (s.Mode == MoveMode.Mounting) StepScripted(ref s, world, cfg);
                else StepClimb(ref s, wishX, wishY, world, cfg);
                return;
            }

            StepWalk(ref s, wishX, wishY, world, cfg);
        }

        // ---------------------------------------------------------------- timers

        static void TickTimers(ref PlayerSimState s, in MoveConfig cfg)
        {
            if (s.CoyoteTimer > 0) s.CoyoteTimer--;
            if (s.JumpBufferTimer > 0) s.JumpBufferTimer--;
            if (s.DodgeRecoverTimer > 0) s.DodgeRecoverTimer--;
            if (s.FallThroughTimer > 0) s.FallThroughTimer--;
            if (s.LadderCooldownTimer > 0) s.LadderCooldownTimer--;
            if (s.StepNoiseTimer > 0) s.StepNoiseTimer--;

            if (s.StaminaCharges < cfg.StaminaMax)
            {
                s.StaminaTimer++;
                if (s.StaminaTimer >= cfg.StaminaRecoverFrames)
                {
                    s.StaminaTimer = 0;
                    s.StaminaCharges++;
                }
            }
            else
            {
                s.StaminaTimer = 0;
            }
        }

        // ---------------------------------------------------------------- walking

        static void StepWalk(ref PlayerSimState s, int wishX, int wishY, SimWorld world, in MoveConfig cfg)
        {
            bool grounded = s.Mode == MoveMode.Grounded;

            // Crouching: slower, shorter, and silent. The quiet option is the whole point —
            // running is Loud and tells the other player exactly where you are (docs/03).
            bool wantsCrouch = grounded && wishY < 0;
            if (s.Crouching && !wantsCrouch && !HasHeadroom(ref s, world, cfg))
                wantsCrouch = true;                       // cannot stand up under a low ceiling
            s.Crouching = wantsCrouch;

            // Drop through a one-way platform: hold down and press jump.
            if (grounded && wishY < 0 && s.JumpBufferTimer > 0 && StandingOnOneWayOnly(ref s, world, cfg))
            {
                s.JumpBufferTimer = 0;
                s.FallThroughTimer = cfg.FallThroughFrames;
                s.Mode = MoveMode.Airborne;
                grounded = false;
            }

            Fix targetSpeed = (s.Crouching ? cfg.CrouchSpeed : cfg.RunSpeed) * wishX;
            if (s.SpeedPermille > 0)
                targetSpeed = new Fix((int)(((long)targetSpeed.Raw * s.SpeedPermille) / 1000));

            // Knocked back: the body keeps what the blow gave it. Running the normal ground
            // deceleration over a hit would wipe the knockback in a tenth of a second, and a hit
            // that does not move anybody is a hit nobody can read.
            if (s.HitstunTimer > 0 || s.StaggerTimer > 0)
            {
                Fix drag = cfg.GroundDecel / 6;
                s.Velocity.X = Fix.MoveTowards(s.Velocity.X, Fix.Zero, drag * Dt);
            }
            else
            {
                Fix accel = wishX != 0
                    ? (grounded ? cfg.GroundAccel : cfg.AirAccel)
                    : (grounded ? cfg.GroundDecel : cfg.AirDecel);
                s.Velocity.X = Fix.MoveTowards(s.Velocity.X, targetSpeed, accel * Dt);
            }

            // Jump. Coyote time and the input buffer together are the "ledge assist" of docs/02:
            // a jump pressed just before landing, or just after stepping off, still fires.
            if (s.JumpBufferTimer > 0 && (grounded || s.CoyoteTimer > 0))
            {
                s.JumpBufferTimer = 0;
                s.CoyoteTimer = 0;
                s.Crouching = false;
                s.Velocity.Y = cfg.JumpSpeed;
                s.Mode = MoveMode.Airborne;
                grounded = false;
                s.Noise = NoiseLevel.Quiet;
            }

            // A jump is one height. Releasing the button early used to cut the rise short, which
            // reads well on a pad and badly on glass: the same tap gives a different height every
            // time, so no gap in the arena has a reliable answer.
            if (cfg.VariableJumpHeight && !s.JumpHeld && s.Velocity.Y > Fix.Zero)
                s.Velocity.Y = s.Velocity.Y * cfg.JumpCutMul;

            if (!grounded)
            {
                s.Velocity.Y = s.Velocity.Y - cfg.Gravity * Dt;
                if (s.Velocity.Y < -cfg.MaxFallSpeed) s.Velocity.Y = -cfg.MaxFallSpeed;
            }
            else if (s.Velocity.Y <= Fix.Zero)
            {
                s.Velocity.Y = -GroundStick;              // keeps contact on slopes and seams
            }

            Fix impactSpeed = -s.Velocity.Y;
            bool wasAirborne = s.Mode == MoveMode.Airborne;

            MoveX(ref s, s.Velocity.X * Dt, world, cfg);
            MoveY(ref s, s.Velocity.Y * Dt, world, cfg);

            bool nowGrounded = Grounded(ref s, world, cfg);
            if (nowGrounded)
            {
                if (wasAirborne)
                {
                    s.Noise = impactSpeed > cfg.HardLandSpeed ? NoiseLevel.Loud : NoiseLevel.Medium;
                    s.StepNoiseTimer = cfg.StepNoiseFrames;
                }
                s.Mode = MoveMode.Grounded;
                s.CoyoteTimer = cfg.CoyoteFrames;
                if (s.Velocity.Y < Fix.Zero) s.Velocity.Y = Fix.Zero;
            }
            else
            {
                s.Mode = MoveMode.Airborne;
            }

            FootstepNoise(ref s, cfg);
        }

        static void FootstepNoise(ref PlayerSimState s, in MoveConfig cfg)
        {
            if (s.Mode != MoveMode.Grounded || s.Noise != NoiseLevel.Silent) return;
            if (s.Crouching) return;                      // crouch-walking is silent, by design

            Fix speed = Fix.Abs(s.Velocity.X);
            if (speed <= Fix.FromMilli(100)) return;

            if (s.StepNoiseTimer == 0)
            {
                s.Noise = speed >= cfg.LoudSpeed ? NoiseLevel.Loud : NoiseLevel.Quiet;
                s.StepNoiseTimer = cfg.StepNoiseFrames;
            }
        }

        // ---------------------------------------------------------------- dodge

        static bool TryStartDodge(ref PlayerSimState s, InputFlags input, int wishX, in MoveConfig cfg)
        {
            if (!input.Has(InputFlags.Dodge)) return false;
            if (s.DodgeRecoverTimer > 0) return false;

            int cost = cfg.DodgeStaminaCost > 0 ? cfg.DodgeStaminaCost : 1;
            if (s.StaminaCharges < cost) return false;

            s.StaminaCharges -= cost;
            s.Mode = MoveMode.Dodging;
            s.DodgeTimer = cfg.DodgeFrames;
            s.Crouching = false;
            s.LadderIndex = -1;

            int dir = wishX != 0 ? wishX : s.Facing;
            s.Facing = (sbyte)dir;
            s.Velocity = new FixVec2(cfg.DodgeSpeed * dir, Fix.Zero);
            s.Noise = NoiseLevel.Medium;
            return true;
        }

        static void StepDodge(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            MoveX(ref s, s.Velocity.X * Dt, world, cfg);

            // The dodge visually steps into the background, so it ignores gravity for its
            // duration. Keeping it on one plane is what keeps the hit reads honest (docs/02).
            s.DodgeTimer--;
            if (s.DodgeTimer > 0) return;

            s.DodgeRecoverTimer = cfg.DodgeRecoverFrames;
            s.Velocity.X = s.Velocity.X / 2;
            s.Mode = Grounded(ref s, world, cfg) ? MoveMode.Grounded : MoveMode.Airborne;
        }

        // ---------------------------------------------------------------- ladders

        /// <summary>
        /// Grabbing a ladder. Succeeds into either Mounting — a short eased reach onto the centre
        /// line — or straight into Climbing when the body is already on it.
        ///
        /// Everything refused here is refused because accepting it would flicker: a grab that is
        /// undone by the next tick's automatic release is a character twitching on the spot, which
        /// is exactly how the old instant snap read.
        /// </summary>
        static bool TryMountLadder(ref PlayerSimState s, int wishY, SimWorld world, in MoveConfig cfg)
        {
            if (wishY == 0) return false;
            if (s.LadderCooldownTimer > 0) return false;

            // Nobody climbs a ladder crouched, and the grab clears the crouch — so there has to be
            // room to stand up first, or the grab would push the head into the ceiling it was
            // ducking under.
            if (s.Crouching && !HasHeadroom(ref s, world, cfg)) return false;

            Aabb body = s.Body(in cfg);
            int ladder = world.FindLadder(in body);

            // Standing on top of a ladder whose rungs start just under the feet. A ladder with
            // no hatch beside it is the whole level here, and without this the only ones you
            // could ever climb down are the ones with a hole to drop into first.
            bool steppingOn = false;
            if (ladder < 0 && wishY < 0 && s.Mode == MoveMode.Grounded)
            {
                ladder = LadderUnderFeet(ref s, world, in cfg);
                steppingOn = ladder >= 0;
            }
            if (ladder < 0) return false;

            Aabb box = world.Ladders[ladder];
            Fix centreX = world.LadderCentreX(ladder);

            if (wishY > 0)
            {
                // Nothing worth climbing: the body is already at the top of this ladder. Without
                // this, pressing up while standing beside a hatch grabs the ladder you just left
                // and the climb-out immediately puts you back where you were.
                if (s.Position.Y >= box.MaxY - cfg.LadderTopMargin - cfg.BodyHeight / 4)
                    return false;
            }
            else
            {
                // Pressing down on the floor means crouch, not climb, unless the ladder goes below.
                if (s.Mode == MoveMode.Grounded && box.MinY >= s.Position.Y) return false;

                // And there has to be a way down on the centre line — a hatch, or the lip of a
                // ledge. At the foot of a ladder there is floor instead, and grabbing it there
                // would be undone by the release at the bottom on the very next tick. Stepping
                // over the top of a ladder is the exception: the floor underfoot is the floor
                // the ladder runs through, which is exactly what you are leaving.
                if (!steppingOn &&
                    SupportUnder(centreX, s.Position.Y, s.FallThroughTimer, world, in cfg))
                    return false;
            }

            // The reach is a straight slide sideways, so it must not pass through anything. If it
            // would, grab the ladder where the body already stands instead of refusing: being on
            // the rungs slightly off centre is harmless, being pushed into a wall is not.
            Fix targetX = SweepFree(s.Position.X, centreX, s.Position.Y, world, in cfg)
                ? centreX : s.Position.X;

            // Stepping over the top means the grab also has to come DOWN onto the top rung,
            // which is below the floor being left.
            Fix targetY = steppingOn ? box.MaxY - cfg.LadderTopMargin : s.Position.Y;

            int frames = FramesFor(Fix.Max(Fix.Abs(targetX - s.Position.X),
                                           Fix.Abs(targetY - s.Position.Y)), in cfg);

            s.LadderIndex = ladder;
            s.Crouching = false;
            s.Velocity = FixVec2.Zero;
            s.ScriptDir = (sbyte)(wishY > 0 ? 1 : -1);

            if (frames <= 0)
            {
                s.Position.X = targetX;
                s.Position.Y = targetY;
                s.Mode = MoveMode.Climbing;
                return true;
            }

            s.ScriptFrom = s.Position;
            s.ScriptTo = new FixVec2(targetX, targetY);
            s.ScriptFrames = frames;
            s.ScriptTimer = frames;
            s.Mode = MoveMode.Mounting;
            s.Noise = NoiseLevel.Quiet;
            return true;
        }

        /// <summary>
        /// How long a scripted move takes: its distance at the scripted-move speed, within the
        /// configured floor and ceiling. Zero means there is nothing to show.
        ///
        /// Deriving it from the distance is the whole point. A fixed frame count has to be short
        /// enough not to feel like a pause on a short move, which makes it a lurch on a long one —
        /// the climb-out used to cover a metre and a half in fourteen frames, ten metres a second,
        /// faster than a dodge.
        /// </summary>
        static int FramesFor(Fix distance, in MoveConfig cfg)
        {
            if (distance <= Fix.FromMilli(20)) return 0;
            if (cfg.ScriptSpeed <= Fix.Zero) return cfg.ScriptMaxFrames;

            int frames = (distance / cfg.ScriptSpeed * TicksPerSecond).ToInt() + 1;
            if (frames < cfg.ScriptMinFrames) frames = cfg.ScriptMinFrames;
            if (frames > cfg.ScriptMaxFrames) frames = cfg.ScriptMaxFrames;
            return frames;
        }

        static void StepClimb(ref PlayerSimState s, int wishX, int wishY, SimWorld world, in MoveConfig cfg)
        {
            // Jumping off a ladder is always allowed: never trap the player on one.
            if (s.JumpBufferTimer > 0)
            {
                s.JumpBufferTimer = 0;
                s.Mode = MoveMode.Airborne;
                s.LadderIndex = -1;
                s.LadderCooldownTimer = cfg.LadderRegrabFrames;
                s.Velocity = new FixVec2(cfg.RunSpeed * wishX, cfg.JumpSpeed);
                s.Noise = NoiseLevel.Quiet;
                return;
            }

            if (s.LadderIndex < 0 || s.LadderIndex >= world.Ladders.Length)
            {
                s.Mode = Grounded(ref s, world, cfg) ? MoveMode.Grounded : MoveMode.Airborne;
                return;
            }

            Aabb ladder = world.Ladders[s.LadderIndex];

            // Keep the body on the ladder's centre line. The grab already put it there, so this
            // only has to undo a sideways nudge, and it does so at climbing speed: at the 6 m/s it
            // used to run at, letting go of the stick after edging along the rungs snapped the body
            // back twice as fast as a run.
            //
            // Only while the player is not pushing sideways, or it would fight the step off.
            if (wishX == 0)
            {
                Fix want = Fix.MoveTowards(s.Position.X, world.LadderCentreX(s.LadderIndex),
                                           cfg.LadderSnapSpeed * Dt);

                // The pull writes X directly, so it has to check its own way: a ladder mounted
                // from an awkward angle must not drag the body into the wall beside it.
                if (SweepFree(s.Position.X, want, s.Position.Y, world, in cfg))
                    s.Position.X = want;
            }

            s.Velocity.X = Fix.Zero;
            s.Velocity.Y = wishY > 0 ? cfg.ClimbUpSpeed
                         : wishY < 0 ? -cfg.ClimbDownSpeed
                         : Fix.Zero;

            MoveY(ref s, s.Velocity.Y * Dt, world, cfg, s.LadderIndex);

            // Never climb past the top of the ladder box. Doing so dropped the body out of the
            // ladder with nothing underneath — the hatch is a hole — so it fell, touched the
            // ladder again, re-grabbed, and climbed back out. That loop was the juddering.
            Fix ceiling = ladder.MaxY - cfg.LadderTopMargin;
            bool atTop = s.Position.Y >= ceiling;
            if (atTop)
            {
                s.Position.Y = ceiling;
                if (s.Velocity.Y > Fix.Zero) s.Velocity.Y = Fix.Zero;
            }

            // At the top, climbing out onto the floor beside the hatch is automatic. Asking the
            // player to nudge sideways while hanging over a hole is not a mechanic, it is a trap.
            // Hold a direction and you climb out that way; hold nothing and the climb-out picks
            // the side with more floor on it, rather than depositing you in whatever pocket
            // happens to be nearest.
            if (atTop && wishY > 0 && TryStartMantle(ref s, wishX, s.LadderIndex, world, cfg))
                return;

            // The same courtesy at the other end: keep holding down at the foot of a ladder and
            // you step off it onto the floor. Before this, the bottom was the trap the top used
            // to be — the body stopped on the ground still bolted to the rungs, in a climb-idle
            // pose, until the player thought to nudge sideways.
            if (wishY < 0 && Grounded(ref s, world, cfg, s.LadderIndex))
            {
                s.LadderIndex = -1;
                s.LadderCooldownTimer = cfg.LadderRegrabFrames;
                s.Mode = MoveMode.Grounded;
                s.Velocity = FixVec2.Zero;
                s.Noise = NoiseLevel.Quiet;
                return;
            }

            // Pushing sideways edges off the ladder.
            if (wishX != 0)
                MoveX(ref s, cfg.LadderDismountSpeed * wishX * Dt, world, cfg);

            if (wishY != 0 && s.StepNoiseTimer == 0)
            {
                s.Noise = NoiseLevel.Quiet;              // climbing is quiet, but not silent
                s.StepNoiseTimer = cfg.StepNoiseFrames;
            }

            Aabb body = s.Body(in cfg);
            if (!body.Overlaps(in ladder))
            {
                s.LadderIndex = -1;
                s.Mode = Grounded(ref s, world, cfg) ? MoveMode.Grounded : MoveMode.Airborne;
                return;
            }

            // Landed on a floor while edging sideways: let go of the ladder.
            if (wishX != 0 && Grounded(ref s, world, cfg, s.LadderIndex))
            {
                s.LadderIndex = -1;
                s.LadderCooldownTimer = cfg.LadderRegrabFrames;
                s.Mode = MoveMode.Grounded;
                s.Velocity = FixVec2.Zero;
            }
        }

        /// <summary>
        /// The ladder a standing body is on top of: one that reaches the feet from below and
        /// runs under them. This is how you get onto a ladder that has no hatch beside it.
        /// </summary>
        static int LadderUnderFeet(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            Fix reach = cfg.BodyHeight / 2;

            for (int i = 0; i < world.Ladders.Length; i++)
            {
                Aabb l = world.Ladders[i];
                if (l.MinX >= s.Position.X + half || l.MaxX <= s.Position.X - half) continue;

                Fix drop = s.Position.Y - l.MaxY;
                if (drop < Fix.Zero - Skin || drop > reach) continue;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Looks for floor beside the ladder to climb out onto, nearest first and preferring the
        /// side asked for. Returns false when there is nothing to step onto, in which case the
        /// player simply stays on the ladder rather than being dropped into a hole.
        /// </summary>
        static bool TryStartMantle(ref PlayerSimState s, int preferSide, int ladderIndex,
                                   SimWorld world, in MoveConfig cfg)
        {
            // A ladder that ends INSIDE its own landing has no hatch to climb out of: in the
            // level it stands in front of the platform, and the last move is a step onto the
            // floor the rungs end at, not a shuffle sideways. Try that first — a ladder with a
            // hatch has nothing above it to find, so it falls through to the sideways search on
            // its own, with no flag to set and nothing to keep in step with the art.
            if (TryStepOut(ref s, ladderIndex, world, in cfg, out FixVec2 landUp))
            {
                StartScripted(ref s, landUp, s.Facing, in cfg);
                return true;
            }

            bool right = TryFindLanding(ref s, 1, world, in cfg, out FixVec2 landRight);
            bool left = TryFindLanding(ref s, -1, world, in cfg, out FixVec2 landLeft);

            if (!right && !left) return false;

            int side;
            if (preferSide > 0 && right) side = 1;
            else if (preferSide < 0 && left) side = -1;
            else if (!left) side = 1;
            else if (!right) side = -1;
            else
            {
                // No direction asked for, and both sides work: take the one with more floor
                // beyond it. Climbing out of a hatch into the narrow side means the first step
                // back is into the hole you just left.
                side = FloorRun(landRight, 1, world, in cfg) >= FloorRun(landLeft, -1, world, in cfg)
                    ? 1 : -1;
            }

            StartScripted(ref s, side > 0 ? landRight : landLeft, side, in cfg);
            return true;
        }

        /// <summary>Hands the body to the climb-out animation, wherever it is climbing out to.</summary>
        static void StartScripted(ref PlayerSimState s, FixVec2 landing, int side,
                                  in MoveConfig cfg)
        {
            s.ScriptFrom = s.Position;
            s.ScriptTo = landing;

            // Each axis is charged for the part of the window it actually gets — Y the first two
            // thirds, X the last seven tenths — so neither has to hurry to fit a window the other
            // one sized.
            Fix spanX = Fix.Abs(s.ScriptTo.X - s.ScriptFrom.X) * 10 / 7;
            Fix spanY = Fix.Abs(s.ScriptTo.Y - s.ScriptFrom.Y) * 3 / 2;
            int frames = FramesFor(Fix.Max(spanX, spanY), in cfg);
            if (frames <= 0) frames = cfg.ScriptMinFrames;

            s.ScriptFrames = frames;
            s.ScriptTimer = frames;
            s.ScriptDir = (sbyte)(side >= 0 ? 1 : -1);
            s.Mode = MoveMode.Mantling;
            s.LadderIndex = -1;
            s.Velocity = FixVec2.Zero;
            s.Facing = (sbyte)(side >= 0 ? 1 : -1);
            s.Noise = NoiseLevel.Quiet;
        }

        /// <summary>
        /// The floor a ladder ends inside, which is the one it serves. Returns false for a
        /// ladder that comes up through a hatch, because the hatch is a hole and there is
        /// nothing overhead to step onto.
        /// </summary>
        static bool TryStepOut(ref PlayerSimState s, int ladderIndex, SimWorld world,
                               in MoveConfig cfg, out FixVec2 landing)
        {
            landing = default;
            if (ladderIndex < 0 || ladderIndex >= world.Ladders.Length) return false;

            Aabb ladder = world.Ladders[ladderIndex];
            Fix half = cfg.BodyWidth / 2;
            Fix x = s.Position.X;

            bool found = false;
            Fix top = Fix.Zero;
            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (solid.MinX >= x + half || solid.MaxX <= x - half) continue;
                if (ladder.MaxY < solid.MinY) continue;
                if (ladder.MaxY > solid.MaxY + cfg.LadderTopMargin) continue;
                if (!found || solid.MaxY > top) { top = solid.MaxY; found = true; }
            }
            if (!found) return false;

            // Climbing out has to end standing, not wedged under the next floor up.
            Aabb standing = new Aabb(x - half, top + Skin, x + half, top + cfg.BodyHeight);
            if (AnySolidOverlap(world, in standing)) return false;
            if (!FullySupported(x, top, world, in cfg)) return false;

            landing = new FixVec2(x, top + Skin);
            return true;
        }

        /// <summary>Nearest spot on one side where the whole body can stand, within reach.</summary>
        static bool TryFindLanding(ref PlayerSimState s, int side, SimWorld world,
                                   in MoveConfig cfg, out FixVec2 landing)
        {
            Fix half = cfg.BodyWidth / 2;
            Fix step = Fix.FromMilli(100);
            int steps = cfg.MantleReach.Raw / step.Raw;

            for (int i = 1; i <= steps; i++)
            {
                Fix x = s.Position.X + step * (i * side);

                FixVec2 probe = new FixVec2(x, s.Position.Y + cfg.BodyHeight);
                if (!TryFindGroundBelow(probe, world, in cfg, cfg.BodyHeight * 2, out Fix groundY))
                    continue;

                // Only ever climb UP and out, never down into something.
                if (groundY < s.Position.Y - cfg.LadderTopMargin * 4) continue;

                Aabb standing = new Aabb(x - half, groundY + Skin,
                                         x + half, groundY + cfg.BodyHeight);
                if (AnySolidOverlap(world, in standing)) continue;

                // The whole footprint must be supported, not just its middle. Without this the
                // climb-out happily picks the lip of a slab, leaving the body half over the hole
                // it just climbed out of, and the next step walks straight back in.
                if (!FullySupported(x, groundY, world, in cfg)) continue;

                landing = new FixVec2(x, groundY + Skin);
                return true;
            }

            landing = default;
            return false;
        }

        /// <summary>How far unbroken floor continues beyond a landing point.</summary>
        static Fix FloorRun(FixVec2 from, int dir, SimWorld world, in MoveConfig cfg)
        {
            Fix step = Fix.FromMilli(200);
            Fix reached = Fix.Zero;

            for (int i = 1; i <= 25; i++)                     // 5 m is plenty to decide on
            {
                Fix x = from.X + step * (i * dir);
                FixVec2 probe = new FixVec2(x, from.Y + cfg.BodyHeight);
                if (!TryFindGroundBelow(probe, world, in cfg, cfg.BodyHeight * 2, out Fix groundY))
                    break;
                if (!FullySupported(x, groundY, world, in cfg)) break;
                reached = step * i;
            }

            return reached;
        }

        /// <summary>Solid under the left edge, the middle and the right edge of the footprint.</summary>
        static bool FullySupported(Fix x, Fix groundY, SimWorld world, in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            for (int i = -1; i <= 1; i++)
            {
                Fix px = x + half * i;
                Aabb probe = new Aabb(px - Skin * 4, groundY - GroundProbe,
                                      px + Skin * 4, groundY - Skin);
                if (!AnySolidOverlap(world, in probe)) return false;
            }
            return true;
        }

        /// <summary>
        /// The two scripted ladder moves: the grab at the bottom and the climb-out at the top.
        ///
        /// Both walk the body from one checked position to another with no gravity and no
        /// collision, because the destination was proved free before the move started. They exist
        /// for the same reason: the frame where the body changes between standing and climbing is
        /// the frame that used to jump, and a jump cannot be smoothed away afterwards by
        /// presentation. It has to not happen in the simulation.
        /// </summary>
        static void StepScripted(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            // Never trap the player on a ladder — not even during the quarter second it takes to
            // reach for one. The climb-out is deliberately not cancellable: it is already
            // committed to a landing, and breaking it off leaves the body over the hole it was
            // leaving.
            if (s.Mode == MoveMode.Mounting && s.JumpBufferTimer > 0)
            {
                s.JumpBufferTimer = 0;
                s.ScriptTimer = 0;
                s.LadderIndex = -1;
                s.LadderCooldownTimer = cfg.LadderRegrabFrames;
                s.Mode = MoveMode.Airborne;
                s.Velocity = new FixVec2(Fix.Zero, cfg.JumpSpeed);
                s.Noise = NoiseLevel.Quiet;
                return;
            }

            s.ScriptTimer--;

            if (s.ScriptTimer <= 0)
            {
                s.Position = s.ScriptTo;
                s.Velocity = FixVec2.Zero;

                if (s.Mode == MoveMode.Mounting)
                {
                    // On the rungs, holding still. The climb itself starts on the next tick, from
                    // the input of that tick, so letting go of the stick during the grab does not
                    // smuggle a frame of climbing in behind it.
                    s.Mode = MoveMode.Climbing;
                }
                else
                {
                    s.Mode = Grounded(ref s, world, cfg) ? MoveMode.Grounded : MoveMode.Airborne;
                    s.LadderCooldownTimer = cfg.LadderRegrabFrames;
                    s.Noise = NoiseLevel.Quiet;
                }
                return;
            }

            int total = s.ScriptFrames > 0 ? s.ScriptFrames : 1;
            int done = total - s.ScriptTimer;
            Fix t = new Fix((int)(((long)Fix.RawOne * done) / total));

            Fix xT, yT;
            if (s.Mode == MoveMode.Mounting)
            {
                // One eased step sideways onto the centre line. Reaching for a ladder is a step,
                // not a hop: nothing about it is vertical, and the Y ends where it began.
                xT = SmoothStep(t);
                yT = xT;
            }
            else
            {
                // Height leads and the step across follows, which is the shape of a real mantle
                // rather than a diagonal slide.
                yT = SmoothStep(Clamp01(t * 3 / 2));
                xT = SmoothStep(Clamp01((t - Fix.FromMilli(300)) * 10 / 7));
            }

            s.Position = new FixVec2(
                s.ScriptFrom.X + (s.ScriptTo.X - s.ScriptFrom.X) * xT,
                s.ScriptFrom.Y + (s.ScriptTo.Y - s.ScriptFrom.Y) * yT);
        }

        static Fix Clamp01(Fix v) => Fix.Clamp(v, Fix.Zero, Fix.One);

        static Fix SmoothStep(Fix t) => t * t * (Fix.FromInt(3) - t * 2);

        // ---------------------------------------------------------------- collision

        static void MoveX(ref PlayerSimState s, Fix dx, SimWorld world, in MoveConfig cfg)
        {
            if (dx == Fix.Zero) return;

            Fix half = cfg.BodyWidth / 2;
            Fix height = s.Crouching ? cfg.CrouchHeight : cfg.BodyHeight;
            Fix targetX = s.Position.X + dx;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                Aabb body = new Aabb(targetX - half, s.Position.Y, targetX + half, s.Position.Y + height);
                if (!body.Overlaps(in solid)) continue;

                // Ledge assist: a low step is climbed instead of stopping you dead.
                Fix stepHeight = solid.MaxY - s.Position.Y;
                if (stepHeight > Fix.Zero && stepHeight <= cfg.StepUpHeight)
                {
                    Fix raised = solid.MaxY + Skin;
                    Aabb raisedBody = new Aabb(targetX - half, raised, targetX + half, raised + height);
                    if (!AnySolidOverlap(world, in raisedBody))
                    {
                        s.Position.Y = raised;
                        continue;
                    }
                }

                targetX = dx > Fix.Zero ? solid.MinX - half - Skin : solid.MaxX + half + Skin;
                s.Velocity.X = Fix.Zero;
            }

            s.Position.X = targetX;
        }

        static void MoveY(ref PlayerSimState s, Fix dy, SimWorld world, in MoveConfig cfg)
            => MoveY(ref s, dy, world, in cfg, -1);

        /// <summary>
        /// Vertical movement. <paramref name="shaft"/> is a ladder whose solids are not in the
        /// way: everything it passes through is the hole it climbs through.
        ///
        /// A level built in 3D puts its ladders in front of the floors they serve, and the hole
        /// is in depth — there is nothing to model in the flat world and nothing to cut out of
        /// it. Treating the ladder itself as the shaft is what lets the same motor climb both
        /// kinds of ladder, the one through a hatch and the one up a wall.
        /// </summary>
        static void MoveY(ref PlayerSimState s, Fix dy, SimWorld world, in MoveConfig cfg,
                          int shaft)
        {
            if (dy == Fix.Zero) return;

            Fix half = cfg.BodyWidth / 2;
            Fix height = s.Crouching ? cfg.CrouchHeight : cfg.BodyHeight;
            Fix startY = s.Position.Y;
            Fix targetY = startY + dy;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (shaft >= 0 && InShaft(in solid, in world.Ladders[shaft])) continue;
                Aabb body = new Aabb(s.Position.X - half, targetY, s.Position.X + half, targetY + height);
                if (!body.Overlaps(in solid)) continue;

                // Only resolve against a surface the body is actually moving into. Without
                // these two guards a body that already overlaps a box — a player spawned with
                // their head inside the floor above, say — gets snapped to the far side of it
                // and teleports a whole storey.
                if (dy > Fix.Zero)
                {
                    if (startY + height > solid.MinY + Skin) continue;   // not a ceiling from below

                    // Ledge assist: a rising jump that clips a corner is nudged past it rather
                    // than stopped, which is what makes the arena feel fair on a phone.
                    if (TryCornerCorrect(ref s, in solid, targetY, half, height, world, cfg))
                        continue;

                    targetY = solid.MinY - height - Skin;
                }
                else
                {
                    if (startY + Skin < solid.MaxY) continue;            // not a floor from above
                    targetY = solid.MaxY + Skin;
                }

                s.Velocity.Y = Fix.Zero;
            }

            if (dy < Fix.Zero && s.FallThroughTimer == 0)
            {
                for (int i = 0; i < world.OneWay.Length; i++)
                {
                    Aabb plat = world.OneWay[i];
                    if (startY + Skin < plat.MaxY) continue;   // we were already below it

                    Aabb body = new Aabb(s.Position.X - half, targetY, s.Position.X + half, targetY + height);
                    if (!body.Overlaps(in plat)) continue;

                    targetY = plat.MaxY + Skin;
                    s.Velocity.Y = Fix.Zero;
                }
            }

            s.Position.Y = targetY;
        }

        static bool TryCornerCorrect(ref PlayerSimState s, in Aabb solid, Fix targetY,
                                     Fix half, Fix height, SimWorld world, in MoveConfig cfg)
        {
            Fix overlapRight = (s.Position.X + half) - solid.MinX;   // clipped the left corner
            Fix overlapLeft  = solid.MaxX - (s.Position.X - half);   // clipped the right corner

            Fix shift;
            if (overlapRight > Fix.Zero && overlapRight <= cfg.CornerCorrect) shift = -overlapRight - Skin;
            else if (overlapLeft > Fix.Zero && overlapLeft <= cfg.CornerCorrect) shift = overlapLeft + Skin;
            else return false;

            Fix shiftedX = s.Position.X + shift;
            Aabb shifted = new Aabb(shiftedX - half, targetY, shiftedX + half, targetY + height);
            if (AnySolidOverlap(world, in shifted)) return false;

            s.Position.X = shiftedX;
            return true;
        }

        /// <summary>
        /// Is this solid something the ladder comes up INTO from below — the floor it climbs
        /// through — rather than the floor it stands on.
        ///
        /// The whole distinction is the solid's underside: a floor the ladder passes through
        /// has its underside inside the ladder's span, while the floor at the ladder's foot has
        /// its underside below the rungs entirely. Getting this wrong in either direction is
        /// loud: too narrow and the climb stops at the underside of its own landing, too wide
        /// and the ladder's own footing becomes a hole and the fighter falls out of the world.
        /// </summary>
        static bool InShaft(in Aabb solid, in Aabb ladder) =>
            solid.MinY > ladder.MinY && solid.MinY < ladder.MaxY;

        static bool AnySolidOverlap(SimWorld world, in Aabb box)
        {
            for (int i = 0; i < world.Solids.Length; i++)
                if (box.Overlaps(in world.Solids[i])) return true;
            return false;
        }

        static bool Grounded(ref PlayerSimState s, SimWorld world, in MoveConfig cfg) =>
            SupportUnder(s.Position.X, s.Position.Y, s.FallThroughTimer, world, in cfg, -1);

        static bool Grounded(ref PlayerSimState s, SimWorld world, in MoveConfig cfg, int shaft) =>
            SupportUnder(s.Position.X, s.Position.Y, s.FallThroughTimer, world, in cfg, shaft);

        /// <summary>
        /// Whether a footprint placed at this x, with its feet at this y, has something to stand
        /// on. Grounded is this question asked about where the body actually is; the ladder grab
        /// asks it about where the body is about to be, which is why it is a free function.
        /// </summary>
        static bool SupportUnder(Fix x, Fix y, int fallThroughTimer, SimWorld world,
                                 in MoveConfig cfg) =>
            SupportUnder(x, y, fallThroughTimer, world, in cfg, -1);

        static bool SupportUnder(Fix x, Fix y, int fallThroughTimer, SimWorld world,
                                 in MoveConfig cfg, int shaft)
        {
            Fix half = cfg.BodyWidth / 2;
            Aabb feet = new Aabb(x - half + Skin, y - GroundProbe, x + half - Skin, y);

            // Inside a ladder's shaft the floor it runs through is not floor: it is the hole.
            // Without this the climb lets go the instant it reaches the underside of its own
            // landing, and the fighter rides the ladder up and down forever.
            if (shaft >= 0)
            {
                for (int i = 0; i < world.Solids.Length; i++)
                {
                    if (InShaft(in world.Solids[i], in world.Ladders[shaft])) continue;
                    if (feet.Overlaps(in world.Solids[i])) return true;
                }
            }
            else if (AnySolidOverlap(world, in feet)) return true;

            if (fallThroughTimer == 0)
            {
                for (int i = 0; i < world.OneWay.Length; i++)
                {
                    Aabb plat = world.OneWay[i];
                    if (y + Skin < plat.MaxY) continue;
                    if (feet.Overlaps(in plat)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the body can slide along the ground from one x to another without passing
        /// through anything. One box covering both ends is enough: the world is axis-aligned, so
        /// anything in the way of a straight sideways slide is inside that box.
        /// </summary>
        static bool SweepFree(Fix fromX, Fix toX, Fix y, SimWorld world, in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            Aabb swept = new Aabb(Fix.Min(fromX, toX) - half, y + Skin,
                                  Fix.Max(fromX, toX) + half, y + cfg.BodyHeight);
            return !AnySolidOverlap(world, in swept);
        }

        static bool StandingOnOneWayOnly(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            Aabb feet = new Aabb(s.Position.X - half + Skin, s.Position.Y - GroundProbe,
                                 s.Position.X + half - Skin, s.Position.Y);

            if (AnySolidOverlap(world, in feet)) return false;

            for (int i = 0; i < world.OneWay.Length; i++)
                if (feet.Overlaps(in world.OneWay[i])) return true;

            return false;
        }

        /// <summary>
        /// Finds the top of the first solid under a body, within maxDistance.
        /// Used to seat a player on the floor at spawn instead of dropping them onto it:
        /// a spawn that starts with a fall reads as "the collision is broken" even when it
        /// is not.
        /// </summary>
        public static bool TryFindGroundBelow(FixVec2 feet, SimWorld world, in MoveConfig cfg,
                                              Fix maxDistance, out Fix groundY)
        {
            Fix half = cfg.BodyWidth / 2;
            Fix lowest = feet.Y - maxDistance;
            bool found = false;
            groundY = feet.Y;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (solid.MaxX <= feet.X - half || solid.MinX >= feet.X + half) continue;
                if (solid.MaxY > feet.Y + Skin) continue;      // above the feet
                if (solid.MaxY < lowest) continue;             // too far down

                if (!found || solid.MaxY > groundY)
                {
                    groundY = solid.MaxY;
                    found = true;
                }
            }

            return found;
        }

        static bool HasHeadroom(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            Aabb standing = new Aabb(s.Position.X - half, s.Position.Y,
                                     s.Position.X + half, s.Position.Y + cfg.BodyHeight);
            return !AnySolidOverlap(world, in standing);
        }
    }
}
