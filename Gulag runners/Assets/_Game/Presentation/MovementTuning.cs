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
        [Range(0f, 1f)] public float jumpCutMultiplier = 0.45f;
        public int coyoteFrames = 6;
        public int jumpBufferFrames = 7;

        [Header("Ladders")]
        public float climbUpSpeed = 2.0f;
        public float climbDownSpeed = 2.6f;

        [Header("Dodge")]
        public float dodgeSpeed = 7f;
        [Tooltip("0.35 s of invulnerability, from docs/02.")]
        public int dodgeFrames = 21;
        public int dodgeRecoverFrames = 9;

        [Header("Stamina")]
        public int staminaMax = 3;
        [Tooltip("1.2 s per charge, from docs/02.")]
        public int staminaRecoverFrames = 72;

        [Header("Body")]
        public float bodyWidth = 0.6f;
        public float bodyHeight = 1.8f;
        public float crouchHeight = 1.1f;

        [Header("Ledge assist")]
        [Tooltip("Steps this low are climbed instead of blocking you.")]
        public float stepUpHeight = 0.3f;
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
            c.JumpCutMul = M(jumpCutMultiplier);
            c.CoyoteFrames = coyoteFrames;
            c.JumpBufferFrames = jumpBufferFrames;
            c.ClimbUpSpeed = M(climbUpSpeed);
            c.ClimbDownSpeed = M(climbDownSpeed);
            c.DodgeSpeed = M(dodgeSpeed);
            c.DodgeFrames = dodgeFrames;
            c.DodgeRecoverFrames = dodgeRecoverFrames;
            c.StaminaMax = staminaMax;
            c.StaminaRecoverFrames = staminaRecoverFrames;
            c.BodyWidth = M(bodyWidth);
            c.BodyHeight = M(bodyHeight);
            c.CrouchHeight = M(crouchHeight);
            c.StepUpHeight = M(stepUpHeight);
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
