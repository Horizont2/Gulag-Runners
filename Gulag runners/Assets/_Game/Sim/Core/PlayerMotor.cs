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

        public static void Step(ref PlayerSimState s, InputFlags input, SimWorld world,
                                in MoveConfig cfg)
        {
            StepFlat(ref s, input, world, in cfg);
            UpdateDepth(ref s, world, in cfg);
        }

        /// <summary>
        /// Which slice of the level the body is on, and how it gets there.
        ///
        /// A location built in 3D does not have one floor at one depth: this one has a ground
        /// floor nine metres deep carrying a ramp at z -3.0 and a crate staircase at z -1.4,
        /// and a gallery above at z -0.6 that the ladder in between reaches at z -2.1. Climbing
        /// is therefore a move AWAY from the camera and coming down is a move towards it, which
        /// is the thing the fighter has to work out for themselves because there is no button
        /// for it and docs/06 has no input bit to spare for one.
        ///
        /// Three rules, in order: a ladder is a thing you are holding, so it wins; otherwise
        /// whatever is just ahead and low enough to step onto, because that is where you are
        /// evidently going; otherwise the floor underfoot, which only moves you if it does not
        /// already hold you.
        /// </summary>
        static void UpdateDepth(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            if (cfg.DepthSnapSpeed <= Fix.Zero || world.SolidZ.Length == 0) return;

            Fix inset = cfg.BodyDepth / 2;
            Fix want;

            if (s.LadderIndex >= 0 && s.LadderIndex < world.Ladders.Length)
                want = world.LadderSpan(s.LadderIndex).Nearest(s.Depth, inset);
            else if (s.Mode != MoveMode.Grounded)
                return;                                   // mid-air you keep the slice you left
            else if (!TryLaneAhead(ref s, world, in cfg, out want) &&
                     !TryFloorLane(ref s, world, in cfg, out want))
                return;

            s.Depth = Fix.MoveTowards(s.Depth, want, cfg.DepthSnapSpeed * Dt);
        }

        /// <summary>The slice of the nearest thing ahead that is low enough to climb onto.</summary>
        static bool TryLaneAhead(ref PlayerSimState s, SimWorld world, in MoveConfig cfg,
                                 out Fix lane)
        {
            lane = s.Depth;
            if (cfg.LaneLookahead <= Fix.Zero) return false;

            Fix half = cfg.BodyWidth / 2;
            Fix limit = cfg.ClamberHeight > cfg.StepUpHeight ? cfg.ClamberHeight
                                                             : cfg.StepUpHeight;
            Fix from = s.Facing > 0 ? s.Position.X + half : s.Position.X - half - cfg.LaneLookahead;
            Fix to = s.Facing > 0 ? s.Position.X + half + cfg.LaneLookahead : s.Position.X - half;

            // Ladders are looked for on BOTH sides: you face a ladder to climb it, and which
            // way you happen to be facing when you reach for one is not information.
            Fix ladderFrom = s.Position.X - half - cfg.LaneLookahead;
            Fix ladderTo = s.Position.X + half + cfg.LaneLookahead;

            bool found = false;
            Fix nearestX = Fix.Zero;
            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (solid.MaxX <= from || solid.MinX >= to) continue;

                Fix step = solid.MaxY - s.Position.Y;
                if (step <= Fix.Zero || step > limit) continue;

                // Strictly ahead. A box whose X already surrounds the body is not somewhere
                // the body is going — and the wall at the back of a building runs the length
                // of the arena, so measured by its near edge it is ahead of everything and
                // would win every time.
                Fix gap = s.Facing > 0 ? solid.MinX - (s.Position.X + half)
                                       : (s.Position.X - half) - solid.MaxX;
                if (gap < Fix.Zero) continue;
                if (found && gap >= nearestX) continue;

                Fix want = world.SolidSpan(i).Nearest(s.Depth, cfg.BodyDepth / 2);
                if (Fix.Abs(want - s.Depth) > cfg.LaneReach) continue;

                nearestX = gap;
                lane = want;
                found = true;
            }

            // A ladder counts as somewhere you are going too, and it has to: this location's
            // ladders stand a metre in front of the floor they rise from, so a fighter who
            // could not walk onto their slice could never reach one at all.
            for (int i = 0; i < world.Ladders.Length; i++)
            {
                Aabb l = world.Ladders[i];
                if (l.MaxX <= ladderFrom || l.MinX >= ladderTo) continue;
                if (l.MaxY <= s.Position.Y || l.MinY > s.Position.Y + cfg.BodyHeight) continue;

                // Unlike a wall, a ladder you are ALREADY standing at is exactly where you
                // are going: clamped to zero rather than skipped, or pressing up in front of
                // one does nothing at all, because the lane never comes to meet it.
                Fix gap = s.Facing > 0 ? l.MinX - (s.Position.X + half)
                                       : (s.Position.X - half) - l.MaxX;
                if (gap < Fix.Zero) gap = Fix.Zero;
                if (found && gap >= nearestX) continue;

                Fix want = world.LadderSpan(i).Nearest(s.Depth, cfg.BodyDepth / 2);
                if (Fix.Abs(want - s.Depth) > cfg.LaneReach) continue;

                nearestX = gap;
                lane = want;
                found = true;
            }
            return found;
        }

        /// <summary>The slice of the floor underfoot. Keeps the body where it is if it fits.</summary>
        static bool TryFloorLane(ref PlayerSimState s, SimWorld world, in MoveConfig cfg,
                                 out Fix lane)
        {
            lane = s.Depth;
            Fix half = cfg.BodyWidth / 2;
            Fix halfZ = cfg.BodyDepth / 2;
            Aabb feet = new Aabb(s.Position.X - half + Skin, s.Position.Y - GroundProbe,
                                 s.Position.X + half - Skin, s.Position.Y);

            bool found = false;
            Fix top = Fix.Zero;
            for (int i = 0; i < world.Solids.Length; i++)
            {
                if (!feet.Overlaps(in world.Solids[i])) continue;
                if (!world.SolidSpan(i).Reaches(s.Depth, halfZ)) continue;
                if (found && world.Solids[i].MaxY <= top) continue;

                top = world.Solids[i].MaxY;
                lane = world.SolidSpan(i).Nearest(s.Depth, halfZ);
                found = true;
            }
            return found;
        }

        static void StepFlat(ref PlayerSimState s, InputFlags input, SimWorld world,
                             in MoveConfig cfg)
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

            // Nothing comes back while the guard is up: a brace you can hold and recover
            // through is a brace you never have to drop.
            if (s.StaminaCharges < cfg.StaminaMax && !s.Blocking)
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
                    s.LandedSpeed = impactSpeed;
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

            // Feet on the floor or it does not happen. A dodge in the air is a second jump
            // with invulnerability on it, which is a different game from the one docs/02
            // describes — there the ground is where you answer an attack.
            if (s.Mode != MoveMode.Grounded) return false;

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
            int ladder = FindLadderInReach(in body, s.Depth, world, in cfg);

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
            Fix centreX = world.LadderCentreAt(ladder, s.Position.Y);

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
                    SupportUnder(centreX, s.Position.Y, s.Depth, s.FallThroughTimer,
                                 world, in cfg))
                    return false;
            }

            // The reach is a straight slide sideways, so it must not pass through anything. If it
            // would, grab the ladder where the body already stands instead of refusing: being on
            // the rungs slightly off centre is harmless, being pushed into a wall is not.
            Fix targetX = SweepFree(s.Position.X, centreX, s.Position.Y, s.Depth, world, in cfg)
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
            Fix rung = world.LadderCentreAt(s.LadderIndex, s.Position.Y);

            // Keep the body on the ladder's centre line, every frame: nothing else moves X
            // while climbing, and on a leaning ladder that line walks sideways as the body
            // rises, so it is a line to follow rather than a point to sit on. At climbing speed
            // it cannot be seen; at the 6 m/s this used to run at it was a yank.
            Fix want = Fix.MoveTowards(s.Position.X, rung, cfg.LadderSnapSpeed * Dt);

            // The pull writes X directly, so it has to check its own way: a ladder mounted from
            // an awkward angle must not drag the body into the wall beside it.
            if (SweepFree(s.Position.X, want, s.Position.Y, s.Depth, world, in cfg))
                s.Position.X = want;

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
            //
            // A direction on its own counts as asking, not only up. At the top of a ladder
            // sideways means "get me out this way", and the climb-out is the move that does it:
            // edging off the rungs instead walks the body out over the hole it came up through,
            // which is how a fighter ends up standing on air beside a hatch.
            if (atTop && (wishY > 0 || wishX != 0) &&
                TryStartMantle(ref s, wishX, s.LadderIndex, world, cfg))
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

            // A direction, with the climb let go of, asks to get off the ladder on that side,
            // and the climb-out is the move that does it: it looks for floor within reach at or
            // above the feet, and refuses — leaving the fighter on the rungs — when there is
            // none. Nothing edges the body sideways off the rungs any more. That was the trap
            // under two separate complaints: you walk INTO a ladder holding a direction, so
            // pressing up while still holding it slid the body straight back off and dropped
            // it, and pressing sideways at the top walked it out over the hole it had just come
            // up through, standing on air beside the hatch.
            if (wishX != 0 && wishY == 0 &&
                TryFindLanding(ref s, wishX, world, in cfg, out FixVec2 stepOff))
            {
                StartScripted(ref s, stepOff, wishX, s.Depth, in cfg);
                return;
            }

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

        }

        /// <summary>
        /// Can a body standing on this slice get onto that piece of collision by changing lane,
        /// and which slice would it be standing on. True with no shift at all when the body
        /// already reaches it.
        ///
        /// This is the question a location modelled in 3D keeps asking. Nothing here lines its
        /// floors up: the ladder stands in FRONT of the walkway it serves, and the walkway is
        /// half a metre further from the camera than the platform the ladder's foot is on. A
        /// climb-out that insists on one slice finds nothing to step onto and leaves the fighter
        /// hanging at the top of the ladder — which is precisely what it did.
        ///
        /// LaneReach bounds it, so this is a step back onto the next floor, never a teleport
        /// into the scenery behind it.
        /// </summary>
        static bool LaneFor(Span span, Fix depth, in MoveConfig cfg, out Fix lane)
        {
            Fix half = cfg.BodyDepth / 2;
            if (span.Reaches(depth, half)) { lane = depth; return true; }

            lane = span.Nearest(depth, half);
            return cfg.LaneReach > Fix.Zero && Fix.Abs(lane - depth) <= cfg.LaneReach;
        }

        /// <summary>
        /// The ladder a body is touching, on its own slice or one lane away. Nearest lane wins,
        /// so a ladder the body already stands on is never passed over for one behind it.
        /// </summary>
        static int FindLadderInReach(in Aabb body, Fix depth, SimWorld world,
                                     in MoveConfig cfg)
        {
            int best = -1;
            Fix bestShift = Fix.Zero;

            for (int i = 0; i < world.Ladders.Length; i++)
            {
                if (!body.Overlaps(in world.Ladders[i])) continue;
                if (!LaneFor(world.LadderSpan(i), depth, in cfg, out Fix lane)) continue;

                Fix shift = Fix.Abs(lane - depth);
                if (best >= 0 && shift >= bestShift) continue;
                best = i;
                bestShift = shift;
            }
            return best;
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
                if (!LaneFor(world.LadderSpan(i), s.Depth, in cfg, out _)) continue;
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
            if (TryStepOut(ref s, ladderIndex, world, in cfg, out FixVec2 landUp,
                           out Fix laneUp))
            {
                StartScripted(ref s, landUp, s.Facing, laneUp, in cfg);
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
                side = FloorRun(landRight, 1, s.Depth, world, in cfg)
                       >= FloorRun(landLeft, -1, s.Depth, world, in cfg)
                    ? 1 : -1;
            }

            // A sideways climb-out stays on its own slice: it is a step along this floor, not
            // onto the next one.
            StartScripted(ref s, side > 0 ? landRight : landLeft, side, s.Depth, in cfg);
            return true;
        }

        /// <summary>Hands the body to the climb-out animation, wherever it is climbing out to.</summary>
        static void StartScripted(ref PlayerSimState s, FixVec2 landing, int side, Fix toDepth,
                                  in MoveConfig cfg)
        {
            s.ScriptFrom = s.Position;
            s.ScriptTo = landing;
            s.ScriptFromDepth = s.Depth;
            s.ScriptToDepth = toDepth;

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
                               in MoveConfig cfg, out FixVec2 landing, out Fix lane)
        {
            landing = default;
            lane = s.Depth;
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

                // The landing may be a lane behind the rungs — here it always is.
                if (!LaneFor(world.SolidSpan(i), s.Depth, in cfg, out Fix candidate)) continue;
                if (found && solid.MaxY <= top) continue;

                // Climbing out has to end standing, not wedged under the next floor up, and the
                // whole footprint has to be on it. Both judged on the slice it would land on,
                // and both now only rule THIS candidate out: the old version took the highest
                // floor it could see and gave up if that one did not work, so one awkward box
                // over a ladder's head cancelled the climb-out altogether.
                Aabb standing = new Aabb(x - half, solid.MaxY + Skin,
                                         x + half, solid.MaxY + cfg.BodyHeight);
                if (AnySolidOverlap(world, in standing, candidate, in cfg)) continue;
                if (!FullySupported(x, solid.MaxY, candidate, world, in cfg)) continue;

                top = solid.MaxY;
                lane = candidate;
                found = true;
            }
            if (!found) return false;

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
                if (!TryFindGroundBelow(probe, s.Depth, world, in cfg, cfg.BodyHeight * 2,
                                        out Fix groundY))
                    continue;

                // Only ever climb UP and out, never down into something.
                if (groundY < s.Position.Y - cfg.LadderTopMargin * 4) continue;

                Aabb standing = new Aabb(x - half, groundY + Skin,
                                         x + half, groundY + cfg.BodyHeight);
                if (AnySolidOverlap(world, in standing, s.Depth, in cfg)) continue;

                // The whole footprint must be supported, not just its middle. Without this the
                // climb-out happily picks the lip of a slab, leaving the body half over the hole
                // it just climbed out of, and the next step walks straight back in.
                if (!FullySupported(x, groundY, s.Depth, world, in cfg)) continue;

                landing = new FixVec2(x, groundY + Skin);
                return true;
            }

            landing = default;
            return false;
        }

        /// <summary>How far unbroken floor continues beyond a landing point.</summary>
        static Fix FloorRun(FixVec2 from, int dir, Fix depth, SimWorld world,
                            in MoveConfig cfg)
        {
            Fix step = Fix.FromMilli(200);
            Fix reached = Fix.Zero;

            for (int i = 1; i <= 25; i++)                     // 5 m is plenty to decide on
            {
                Fix x = from.X + step * (i * dir);
                FixVec2 probe = new FixVec2(x, from.Y + cfg.BodyHeight);
                if (!TryFindGroundBelow(probe, depth, world, in cfg, cfg.BodyHeight * 2,
                                        out Fix groundY))
                    break;
                if (!FullySupported(x, groundY, depth, world, in cfg)) break;
                reached = step * i;
            }

            return reached;
        }

        /// <summary>Solid under the left edge, the middle and the right edge of the footprint.</summary>
        static bool FullySupported(Fix x, Fix groundY, Fix depth, SimWorld world,
                                   in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            for (int i = -1; i <= 1; i++)
            {
                Fix px = x + half * i;
                Aabb probe = new Aabb(px - Skin * 4, groundY - GroundProbe,
                                      px + Skin * 4, groundY - Skin);
                if (!AnySolidOverlap(world, in probe, depth, in cfg)) return false;
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

                // The climb-out owns the slice for its whole length, so it also has to finish on
                // the one it promised. A mount does not: UpdateDepth is already drawing the body
                // onto the ladder's own slice while the reach plays.
                if (s.Mode == MoveMode.Mantling) s.Depth = s.ScriptToDepth;

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

            // Depth rides with the height: the fighter leans back onto the next floor as they
            // come up over its edge, which is the one moment in the game where the lane moves
            // because the move says so rather than because the floor underfoot asked for it.
            if (s.Mode == MoveMode.Mantling)
                s.Depth = s.ScriptFromDepth + (s.ScriptToDepth - s.ScriptFromDepth) * yT;
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

            // Two passes, because a stack of boxes is not a list of separate problems.
            // Resolving them one at a time takes the first ledge in array order, finds the
            // next layer of the stack sitting on top of it, gives up on the step — and the
            // body stops dead at the foot of a staircase it could have walked up. So: find
            // the HIGHEST surface this step could put the feet on, and only if there is none
            // treat what is left as a wall.
            Fix limit = cfg.ClamberHeight > cfg.StepUpHeight ? cfg.ClamberHeight
                                                             : cfg.StepUpHeight;
            bool found = false;
            Fix bestTop = Fix.Zero;

            Fix halfZ = cfg.BodyDepth / 2;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (!world.SolidSpan(i).Reaches(s.Depth, halfZ)) continue;
                Aabb body = new Aabb(targetX - half, s.Position.Y,
                                     targetX + half, s.Position.Y + height);
                if (!body.Overlaps(in solid)) continue;

                Fix step = solid.MaxY - s.Position.Y;
                if (step <= Fix.Zero || step > limit) continue;

                // Hauling up is for a body with its feet on something. Mid-jump it would be
                // a second, free climb out of anything you happened to brush.
                if (step > cfg.StepUpHeight && s.Mode != MoveMode.Grounded) continue;
                if (found && solid.MaxY <= bestTop) continue;

                Fix raised = solid.MaxY + Skin;
                Aabb raisedBody = new Aabb(targetX - half, raised,
                                           targetX + half, raised + height);
                if (AnySolidOverlap(world, in raisedBody, s.Depth, in cfg)) continue;

                bestTop = solid.MaxY;
                found = true;
            }

            if (found)
            {
                bool clamber = bestTop - s.Position.Y > cfg.StepUpHeight;
                s.Position.Y = bestTop + Skin;
                s.Position.X = targetX;

                // A haul costs the speed it was walked at, so a stepped slope is climbed at a
                // crawl and a kerb still feels like nothing.
                if (clamber)
                    s.Velocity.X = new Fix((int)(((long)s.Velocity.X.Raw *
                                                  cfg.ClamberSpeedPermille) / 1000));
                return;
            }

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (!world.SolidSpan(i).Reaches(s.Depth, halfZ)) continue;
                Aabb body = new Aabb(targetX - half, s.Position.Y,
                                     targetX + half, s.Position.Y + height);
                if (!body.Overlaps(in solid)) continue;

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
                if (!world.SolidSpan(i).Reaches(s.Depth, cfg.BodyDepth / 2)) continue;
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
                    if (!world.OneWaySpan(i).Reaches(s.Depth, cfg.BodyDepth / 2)) continue;
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
            if (AnySolidOverlap(world, in shifted, s.Depth, in cfg)) return false;

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

        /// <summary>
        /// Is anything solid in this box, on this slice of the level. Depth is never moved
        /// through — it only decides which boxes are here at all, which is what stops the
        /// backdrop of a location being a wall across the fight.
        /// </summary>
        static bool AnySolidOverlap(SimWorld world, in Aabb box, Fix depth, in MoveConfig cfg)
        {
            Fix half = cfg.BodyDepth / 2;
            for (int i = 0; i < world.Solids.Length; i++)
                if (box.Overlaps(in world.Solids[i]) && world.SolidSpan(i).Reaches(depth, half))
                    return true;
            return false;
        }

        static bool Grounded(ref PlayerSimState s, SimWorld world, in MoveConfig cfg) =>
            SupportUnder(s.Position.X, s.Position.Y, s.Depth, s.FallThroughTimer,
                         world, in cfg, -1);

        static bool Grounded(ref PlayerSimState s, SimWorld world, in MoveConfig cfg, int shaft) =>
            SupportUnder(s.Position.X, s.Position.Y, s.Depth, s.FallThroughTimer,
                         world, in cfg, shaft);

        /// <summary>
        /// Whether a footprint placed at this x, with its feet at this y, has something to stand
        /// on. Grounded is this question asked about where the body actually is; the ladder grab
        /// asks it about where the body is about to be, which is why it is a free function.
        /// </summary>
        static bool SupportUnder(Fix x, Fix y, Fix depth, int fallThroughTimer, SimWorld world,
                                 in MoveConfig cfg) =>
            SupportUnder(x, y, depth, fallThroughTimer, world, in cfg, -1);

        static bool SupportUnder(Fix x, Fix y, Fix depth, int fallThroughTimer, SimWorld world,
                                 in MoveConfig cfg, int shaft)
        {
            Fix half = cfg.BodyWidth / 2;
            Fix halfZ = cfg.BodyDepth / 2;
            Aabb feet = new Aabb(x - half + Skin, y - GroundProbe, x + half - Skin, y);

            // Inside a ladder's shaft the floor it runs through is not floor: it is the hole.
            // Without this the climb lets go the instant it reaches the underside of its own
            // landing, and the fighter rides the ladder up and down forever.
            if (shaft >= 0)
            {
                for (int i = 0; i < world.Solids.Length; i++)
                {
                    if (InShaft(in world.Solids[i], in world.Ladders[shaft])) continue;
                    if (feet.Overlaps(in world.Solids[i]) &&
                        world.SolidSpan(i).Reaches(depth, halfZ)) return true;
                }
            }
            else if (AnySolidOverlap(world, in feet, depth, in cfg)) return true;

            if (fallThroughTimer == 0)
            {
                for (int i = 0; i < world.OneWay.Length; i++)
                {
                    Aabb plat = world.OneWay[i];
                    if (!world.OneWaySpan(i).Reaches(depth, halfZ)) continue;
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
        static bool SweepFree(Fix fromX, Fix toX, Fix y, Fix depth, SimWorld world,
                              in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            Aabb swept = new Aabb(Fix.Min(fromX, toX) - half, y + Skin,
                                  Fix.Max(fromX, toX) + half, y + cfg.BodyHeight);
            return !AnySolidOverlap(world, in swept, depth, in cfg);
        }

        static bool StandingOnOneWayOnly(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            Aabb feet = new Aabb(s.Position.X - half + Skin, s.Position.Y - GroundProbe,
                                 s.Position.X + half - Skin, s.Position.Y);

            if (AnySolidOverlap(world, in feet, s.Depth, in cfg)) return false;

            for (int i = 0; i < world.OneWay.Length; i++)
                if (feet.Overlaps(in world.OneWay[i]) &&
                    world.OneWaySpan(i).Reaches(s.Depth, cfg.BodyDepth / 2)) return true;

            return false;
        }

        /// <summary>
        /// Finds the top of the first solid under a body, within maxDistance.
        /// Used to seat a player on the floor at spawn instead of dropping them onto it:
        /// a spawn that starts with a fall reads as "the collision is broken" even when it
        /// is not.
        /// </summary>
        /// <summary>
        /// Gets a body out of a solid it is standing inside, and says which one it was.
        ///
        /// Nothing else will. MoveY deliberately leaves a box the body already overlaps alone,
        /// because resolving it would teleport a fighter who merely clipped a floor up a whole
        /// storey — so a body that STARTS inside geometry is never pushed out, it falls
        /// through the world instead, forever. That is one bad box in a hand-marked level away
        /// at all times, and it has to be a nudge and a line in the console, not a match that
        /// cannot be played.
        /// </summary>
        public static bool TryLiftClear(ref PlayerSimState s, SimWorld world, in MoveConfig cfg,
                                        out Aabb stuckIn)
        {
            stuckIn = default;
            Aabb body = s.Body(in cfg);

            bool inside = false;
            Fix top = Fix.Zero;
            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (!world.SolidSpan(i).Reaches(s.Depth, cfg.BodyDepth / 2)) continue;
                if (!body.Overlaps(in solid)) continue;
                if (!inside || solid.MaxY > top) { top = solid.MaxY; stuckIn = solid; }
                inside = true;
            }
            if (!inside) return false;

            // Up and out, onto the thing it was inside. Only if there is room to stand there:
            // lifting a body into a ceiling would trade one trap for another.
            Fix half = cfg.BodyWidth / 2;
            Fix raised = top + Skin;
            Aabb standing = new Aabb(s.Position.X - half, raised,
                                     s.Position.X + half, raised + cfg.BodyHeight);
            if (AnySolidOverlap(world, in standing, s.Depth, in cfg))
                return true;                                   // stuck, and we said so

            s.Position.Y = raised;
            s.Velocity = FixVec2.Zero;
            return true;
        }

        /// <summary>
        /// The top of the floor under a body, on that body's own slice of the level.
        ///
        /// Depth is not a refinement here, it is the whole answer. This used to have an
        /// overload that searched every baked box regardless of depth, and the spawn used it:
        /// the wall standing behind this location is a metre taller than the platform in front
        /// of it, so the spawn snapped the fighter onto the backdrop — a box their own lane
        /// cannot touch — and the first frame of the match dropped them through the floor.
        /// </summary>
        public static bool TryFindGroundBelow(FixVec2 feet, Fix depth, SimWorld world,
                                              in MoveConfig cfg, Fix maxDistance,
                                              out Fix groundY)
        {
            Fix half = cfg.BodyWidth / 2;
            Fix halfZ = cfg.BodyDepth / 2;
            Fix lowest = feet.Y - maxDistance;
            bool found = false;
            groundY = feet.Y;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (!world.SolidSpan(i).Reaches(depth, halfZ)) continue;
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
            return !AnySolidOverlap(world, in standing, s.Depth, in cfg);
        }
    }
}
