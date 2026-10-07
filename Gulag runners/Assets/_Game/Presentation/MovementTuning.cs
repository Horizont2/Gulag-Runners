using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Inspector-friendly mirror of <see cref="MoveConfig"/>.
    ///
    /// Designers tune in metres and seconds here; the values are converted to fixed point
    /// once, at startup, and never again. This is the only place in the project where a float
    /// is allowed to touch a simulation number — past this boundary the sim is integer-only,
    /// which is what keeps rollback deterministic (docs/06).
    /// </summary>
    [Serializable]
    public class MovementTuning
    {
        [Header("Ground (m/s, m/s^2)")]
        [Tooltip("A room is ~7.2 m wide and docs/03 budgets ~2 s to cross it.")]
        public float runSpeed = 3.6f;
        public float crouchSpeed = 1.6f;
        public float groundAccel = 40f;
        public float groundDecel = 50f;

        [Header("Air")]
        public float airAccel = 22f;
        public float airDecel = 10f;
        public float gravity = 28f;
        public float maxFallSpeed = 18f;

        [Header("Jump")]
        [Tooltip("Apex = jumpSpeed^2 / (2*gravity). Keep it BELOW the 2.5 m floor height: " +
                 "if a jump cleared a floor, ladders would be pointless (docs/05).")]
        public float jumpSpeed = 9.5f;

        [Tooltip("Let go early and the jump is cut short. OFF by default: a jump whose height " +
                 "depends on how long a thumb stayed on glass is a jump nobody can aim, and " +
                 "every ledge then needs a different tap.")]
        public bool variableJumpHeight;
        [Range(0f, 1f)] public float jumpCutMultiplier = 0.45f;
        public int coyoteFrames = 6;
        public int jumpBufferFrames = 7;

        [Header("Ladders")]
        public float climbUpSpeed = 2.0f;
        public float climbDownSpeed = 2.6f;
        [Tooltip("How fast you edge sideways off a ladder onto a landing.")]
        public float ladderDismountSpeed = 1.5f;

        [Tooltip("How fast the body is pulled back to a ladder's centre line after drifting off " +
                 "it. The grab eases on by itself, so this only has a sideways nudge to undo: at " +
                 "climbing speed it is invisible, and at four times that it is a yank.")]
        public float ladderSnapSpeed = 2f;

        [Tooltip("Clearance kept below the top of a ladder. Climbing past the top is what made " +
                 "the character pop off the ladder and fall straight back onto it.")]
        public float ladderTopMargin = 0.06f;

        [Tooltip("How far to either side the climb-out looks for floor to step onto.")]
        public float mantleReach = 1.6f;

        [Tooltip("Average speed of the two scripted ladder moves: the grab at the bottom and the " +
                 "climb-out at the top. Each lasts as long as its own distance at this speed, so " +
                 "neither can be a snap. Raise it for brisker ladders, lower it for heavier ones.")]
        public float scriptSpeed = 2.4f;
        [Tooltip("Shortest and longest a scripted ladder move may take, in frames.")]
        public int scriptMinFrames = 10;
        public int scriptMaxFrames = 30;

        [Tooltip("Frames after letting go of a ladder during which it cannot be grabbed again. " +
                 "Without it, jumping off while still holding up re-grabs the ladder at once.")]
        public int ladderRegrabFrames = 12;

        [Header("Dodge")]
        [Tooltip("Travel is speed x duration: 4.8 m/s over 0.35 s is 1.68 m — about two body " +
                 "widths, which is a step out of reach rather than a teleport across the room.")]
        public float dodgeSpeed = 4.8f;
        [Tooltip("0.35 s of invulnerability, from docs/02. This is the window, not the distance.")]
        public int dodgeFrames = 21;
        public int dodgeRecoverFrames = 9;
        [Tooltip("Charges one dodge costs. Raising this against the pool below is what makes " +
                 "dodges rare: three of six means two in a row and no more.")]
        public int dodgeStaminaCost = 3;

        [Header("Stamina")]
        [Tooltip("A pool of small charges, not a count of dodges: docs/02 spends stamina on the " +
                 "dodge AND on holding a guard, and those two cannot share a resource unless one " +
                 "of them can cost more.")]
        public int staminaMax = 6;
        [Tooltip("Seconds per charge. At 0.55 s a spent dodge is back in 1.65 s.")]
        public int staminaRecoverFrames = 33;

        [Header("Body")]
        [Tooltip("Collision width. Match it to the character's silhouette, not to its T-pose " +
                 "bounds: those span both outstretched arms and are useless as a body width.")]
        public float bodyWidth = 0.45f;
        public float bodyHeight = 1.8f;
        [Tooltip("Height while crouching. Clamped to 75% of the body height on conversion: a " +
                 "crouch taller than the character makes the hitbox GROW when you duck, which " +
                 "inverts every ceiling check that depends on it.")]
        public float crouchHeight = 1.1f;

        [Header("Ledge assist")]
        [Tooltip("Steps this low are climbed instead of blocking you.")]
        public float stepUpHeight = 0.3f;

        [Tooltip("The tallest ledge a standing fighter hauls themselves onto without jumping. " +
                 "Anything above Step Up Height costs most of the speed they were carrying, so " +
                 "a slope built out of boxes — the only kind an AABB world has — is walked up " +
                 "at a crawl instead of hopped up one box at a time, and a kerb is still a " +
                 "kerb. 0 leaves only the free step.")]
        public float clamberHeight = 0.55f;

        [Tooltip("Share of the speed kept after hauling up. The cost that makes it read.")]
        [Range(0f, 1f)] public float clamberSpeed = 0.4f;
        [Tooltip("A rising jump clipping a corner by less than this is nudged through.")]
        public float cornerCorrect = 0.25f;
        public int fallThroughFrames = 18;

        [Header("Noise (docs/01: sound is the only way to find the other player)")]
        public float loudSpeed = 2.5f;
        public int stepNoiseFrames = 18;
        public float hardLandSpeed = 8f;

        static Fix M(float metres) => Fix.FromMilli(Mathf.RoundToInt(metres * 1000f));

        public MoveConfig ToConfig()
        {
            MoveConfig c = MoveConfig.Default();
            c.RunSpeed = M(runSpeed);
            c.CrouchSpeed = M(crouchSpeed);
            c.GroundAccel = M(groundAccel);
            c.GroundDecel = M(groundDecel);
            c.AirAccel = M(airAccel);
            c.AirDecel = M(airDecel);
            c.Gravity = M(gravity);
            c.MaxFallSpeed = M(maxFallSpeed);
            c.JumpSpeed = M(jumpSpeed);
            c.VariableJumpHeight = variableJumpHeight;
            c.JumpCutMul = M(jumpCutMultiplier);
            c.CoyoteFrames = coyoteFrames;
            c.JumpBufferFrames = jumpBufferFrames;
            c.ClimbUpSpeed = M(climbUpSpeed);
            c.ClimbDownSpeed = M(climbDownSpeed);
            c.LadderDismountSpeed = M(ladderDismountSpeed);
            c.LadderSnapSpeed = M(ladderSnapSpeed);
            c.LadderTopMargin = M(ladderTopMargin);
            c.MantleReach = M(mantleReach);
            c.ScriptSpeed = M(scriptSpeed);
            c.ScriptMinFrames = scriptMinFrames;
            c.ScriptMaxFrames = Mathf.Max(scriptMinFrames, scriptMaxFrames);
            c.LadderRegrabFrames = ladderRegrabFrames;
            c.DodgeSpeed = M(dodgeSpeed);
            c.DodgeFrames = dodgeFrames;
            c.DodgeRecoverFrames = dodgeRecoverFrames;
            c.DodgeStaminaCost = Mathf.Max(1, dodgeStaminaCost);
            c.StaminaMax = staminaMax;
            c.StaminaRecoverFrames = staminaRecoverFrames;
            c.BodyWidth = M(bodyWidth);
            c.BodyHeight = M(bodyHeight);
            c.CrouchHeight = M(Mathf.Min(crouchHeight, bodyHeight * 0.75f));
            c.StepUpHeight = M(stepUpHeight);
            c.ClamberHeight = M(clamberHeight);
            c.ClamberSpeedPermille = Mathf.RoundToInt(Mathf.Clamp01(clamberSpeed) * 1000f);
            c.CornerCorrect = M(cornerCorrect);
            c.FallThroughFrames = fallThroughFrames;
            c.LoudSpeed = M(loudSpeed);
            c.StepNoiseFrames = stepNoiseFrames;
            c.HardLandSpeed = M(hardLandSpeed);
            return c;
        }

        /// <summary>Jump apex in metres, for the inspector and for sanity checks.</summary>
        public float JumpApex => gravity <= 0f ? 0f : (jumpSpeed * jumpSpeed) / (2f * gravity);
    }
}
