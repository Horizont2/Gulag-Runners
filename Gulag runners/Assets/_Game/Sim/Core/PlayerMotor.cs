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

            if (s.Mode == MoveMode.Mantling)
            {
                StepMantle(ref s, world, cfg);
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
                StepClimb(ref s, wishX, wishY, world, cfg);
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
            Fix accel = wishX != 0
                ? (grounded ? cfg.GroundAccel : cfg.AirAccel)
                : (grounded ? cfg.GroundDecel : cfg.AirDecel);
            s.Velocity.X = Fix.MoveTowards(s.Velocity.X, targetSpeed, accel * Dt);

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

            // Variable jump height: releasing the button early cuts the rise short.
            if (!s.JumpHeld && s.Velocity.Y > Fix.Zero)
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
            if (s.StaminaCharges <= 0) return false;

            s.StaminaCharges--;
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

        static bool TryMountLadder(ref PlayerSimState s, int wishY, SimWorld world, in MoveConfig cfg)
        {
            if (wishY == 0) return false;

            Aabb body = s.Body(in cfg);
            int ladder = world.FindLadder(in body);
            if (ladder < 0) return false;

            // Pressing down on the floor means crouch, not climb, unless the ladder goes below.
            if (wishY < 0 && s.Mode == MoveMode.Grounded && world.Ladders[ladder].MinY >= s.Position.Y)
                return false;

            s.Mode = MoveMode.Climbing;
            s.LadderIndex = ladder;
            s.Crouching = false;
            s.Velocity = FixVec2.Zero;
            s.Position.X = world.LadderCentreX(ladder);
            return true;
        }

        static void StepClimb(ref PlayerSimState s, int wishX, int wishY, SimWorld world, in MoveConfig cfg)
        {
            // Jumping off a ladder is always allowed: never trap the player on one.
            if (s.JumpBufferTimer > 0)
            {
                s.JumpBufferTimer = 0;
                s.Mode = MoveMode.Airborne;
                s.LadderIndex = -1;
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

            // Slide onto the ladder's centre line instead of teleporting there. Snapping X in a
            // single tick is a visible jump sideways, and it was half of what made ladders feel
            // broken.
            //
            // Only while the player is not pushing sideways: the pull towards the centre is four
            // times faster than the sideways step off, so left running it would make stepping off
            // a ladder impossible.
            if (wishX == 0)
                s.Position.X = Fix.MoveTowards(s.Position.X, world.LadderCentreX(s.LadderIndex),
                                               cfg.LadderSnapSpeed * Dt);

            s.Velocity.X = Fix.Zero;
            s.Velocity.Y = wishY > 0 ? cfg.ClimbUpSpeed
                         : wishY < 0 ? -cfg.ClimbDownSpeed
                         : Fix.Zero;

            MoveY(ref s, s.Velocity.Y * Dt, world, cfg);

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
            if (atTop && wishY > 0 && TryStartMantle(ref s, wishX, world, cfg))
                return;

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
            if (wishX != 0 && Grounded(ref s, world, cfg))
            {
                s.LadderIndex = -1;
                s.Mode = MoveMode.Grounded;
                s.Velocity = FixVec2.Zero;
            }
        }

        /// <summary>
        /// Looks for floor beside the ladder to climb out onto, nearest first and preferring the
        /// side asked for. Returns false when there is nothing to step onto, in which case the
        /// player simply stays on the ladder rather than being dropped into a hole.
        /// </summary>
        static bool TryStartMantle(ref PlayerSimState s, int preferSide, SimWorld world,
                                   in MoveConfig cfg)
        {
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

            s.MantleFrom = s.Position;
            s.MantleTo = side > 0 ? landRight : landLeft;
            s.MantleTimer = cfg.MantleFrames;
            s.Mode = MoveMode.Mantling;
            s.LadderIndex = -1;
            s.Velocity = FixVec2.Zero;
            s.Facing = (sbyte)side;
            s.Noise = NoiseLevel.Quiet;
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
        /// The climb-out itself: a short scripted move with no gravity and no collision, because
        /// the destination was checked to be free before it started. Height leads and the step
        /// across follows, which is the shape of a real mantle rather than a diagonal slide.
        /// </summary>
        static void StepMantle(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            s.MantleTimer--;

            if (s.MantleTimer <= 0)
            {
                s.Position = s.MantleTo;
                s.Velocity = FixVec2.Zero;
                s.Mode = Grounded(ref s, world, cfg) ? MoveMode.Grounded : MoveMode.Airborne;
                s.Noise = NoiseLevel.Quiet;
                return;
            }

            int done = cfg.MantleFrames - s.MantleTimer;
            Fix t = new Fix((int)(((long)Fix.RawOne * done) / cfg.MantleFrames));

            Fix yT = SmoothStep(Clamp01(t * 3 / 2));
            Fix xT = SmoothStep(Clamp01((t - Fix.FromMilli(300)) * 10 / 7));

            s.Position = new FixVec2(
                s.MantleFrom.X + (s.MantleTo.X - s.MantleFrom.X) * xT,
                s.MantleFrom.Y + (s.MantleTo.Y - s.MantleFrom.Y) * yT);
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
        {
            if (dy == Fix.Zero) return;

            Fix half = cfg.BodyWidth / 2;
            Fix height = s.Crouching ? cfg.CrouchHeight : cfg.BodyHeight;
            Fix startY = s.Position.Y;
            Fix targetY = startY + dy;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
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

        static bool AnySolidOverlap(SimWorld world, in Aabb box)
        {
            for (int i = 0; i < world.Solids.Length; i++)
                if (box.Overlaps(in world.Solids[i])) return true;
            return false;
        }

        static bool Grounded(ref PlayerSimState s, SimWorld world, in MoveConfig cfg)
        {
            Fix half = cfg.BodyWidth / 2;
            Aabb feet = new Aabb(s.Position.X - half + Skin, s.Position.Y - GroundProbe,
                                 s.Position.X + half - Skin, s.Position.Y);

            if (AnySolidOverlap(world, in feet)) return true;

            if (s.FallThroughTimer == 0)
            {
                for (int i = 0; i < world.OneWay.Length; i++)
                {
                    Aabb plat = world.OneWay[i];
                    if (s.Position.Y + Skin < plat.MaxY) continue;
                    if (feet.Overlaps(in plat)) return true;
                }
            }

            return false;
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
