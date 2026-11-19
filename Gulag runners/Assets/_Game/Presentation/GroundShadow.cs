using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The pool of shade under a fighter's feet.
    ///
    /// docs/06 asks for exactly this and nothing more -- "нуль real-time тіней (тіні —
    /// запечені + один blob під персонажем)" -- and the blob was the half that never got
    /// built. A fighter with nothing under their feet does not read as standing on the
    /// floor; they read as pasted in front of it, and the taller the thing behind them the
    /// worse it gets. That became more true the day the parallax backdrop went in.
    ///
    /// WHERE THE FLOOR IS. Not from a raycast: there is no floor to hit. The arenas carry
    /// two BoxColliders between them, because the collision that matters lives in the
    /// deterministic sim as a list of AABBs and Unity's physics never sees it. So this asks
    /// the sim, the same way PlayerMotor does, and gets the one answer that is actually
    /// true. It also means the shadow is right on a one-way platform, which a downward
    /// raycast against scenery meshes would not be.
    ///
    /// PlayerController.GroundedY would have been free, and is wrong for the case that
    /// matters: it is the last ground STOOD on, so a fighter leaping from a low crate to a
    /// high ledge keeps a shadow back on the crate the whole way over, and it snaps across
    /// on landing. The shadow earns its place by saying where you will come down.
    ///
    /// WHY IT IS AN ELLIPSE, AND A LONG ONE. The arena cameras are pitched four degrees.
    /// A flat disc on the ground is therefore seen almost edge on: its height on screen is
    /// D * sin(4) = D * 0.0698, and against a 4.14 m frame a 0.6 m disc comes to 1% of the
    /// screen -- about eleven pixels on a 1080-tall phone, under a fighter who is two
    /// hundred and thirty. A circle is simply not visible from here.
    ///
    /// Depth is the way out. The Z axis projects onto the SAME screen vertical that the
    /// pitch is squashing, so stretching the blob into the screen buys back the height the
    /// pitch took, and in a camera this flat nobody can tell it is stretched. Hence a
    /// default two and a half times longer in Z than in X. From overhead it would look
    /// absurd. There is no overhead.
    /// </summary>
    [AddComponentMenu("Gulag Runners/Ground Shadow")]
    public sealed class GroundShadow : MonoBehaviour
    {
        [Tooltip("The fighter this shadow belongs to. Found in the parents when left empty.")]
        public PlayerController player;

        [Tooltip("The quad that gets drawn, built by Tools > Gulag Runners > Build the " +
                 "ground shadows.\n\n" +
                 "It is deliberately NOT a child of the fighter. The fighter's root is " +
                 "scaled to 0.5, so a child's localScale would mean half of what it says, " +
                 "and the root also turns to face the way they are walking -- neither of " +
                 "which a thing lying on the floor should inherit. It lives in the scene " +
                 "with an identity transform and is placed in world space every frame.")]
        public Transform quad;
        public MeshRenderer quadRenderer;

        [Header("Size")]
        [Tooltip("Width across the screen, in metres, when stood on the floor. Left at 0 it " +
                 "is taken from the fighter's own body width, which is what it should track: " +
                 "a shadow wider than its owner reads as a second object.")]
        public float width;

        [Tooltip("Multiplies the body width. 1.6 puts the edge of the pool a little outside " +
                 "the feet, which is what contact shade does.")]
        [Range(0.5f, 3f)] public float spread = 1.6f;

        [Tooltip("How many times longer the pool is along Z than across X.\n\n" +
                 "This is the whole reason the shadow is visible at all. The camera is " +
                 "pitched 4 degrees, so a circle on the ground is squashed to seven per cent " +
                 "of its size on screen; depth projects onto that same axis, so length in Z " +
                 "is what gives it back. At this pitch the stretch cannot be seen.")]
        [Range(1f, 5f)] public float lengthInDepth = 2.5f;

        [Header("Leaving the ground")]
        [Tooltip("Metres of rise over which the shadow dims and shrinks to its faintest.\n\n" +
                 "A jump tops out at jumpSpeed^2 / (2 * gravity) = 9.5^2 / 56 = 1.61 m, so " +
                 "2.4 puts the apex two thirds of the way along: clearly off the floor, with " +
                 "the pool still under you. Above that you are falling off something.")]
        public float fadeOverHeight = 2.4f;

        [Tooltip("How much of its darkness the shadow keeps at full height, rather than " +
                 "going out altogether.\n\n" +
                 "It is not zero on purpose. The dimming and the shrinking multiply, so " +
                 "fading to nothing took the pool to a two-pixel smudge at the top of an " +
                 "ordinary jump -- which is the one moment it is doing real work, because " +
                 "it is the only thing on screen saying where you are about to come down. " +
                 "A third of half opacity is about 17%: present, and far too faint to read " +
                 "as a sticker following the fighter.")]
        [Range(0f, 1f)] public float fadeTo = 0.35f;

        [Tooltip("Size at the top of that rise, as a share of the grounded size. It shrinks " +
                 "rather than only fading, because a shadow that keeps its size while going " +
                 "pale reads as the light dimming, not as the fighter going up.")]
        [Range(0.1f, 1f)] public float shrinkTo = 0.5f;

        [Header("Look")]
        [Tooltip("Darkness on the floor directly underfoot.")]
        [Range(0f, 1f)] public float opacity = 0.5f;

        [Tooltip("Tint. Never pure black: a black blob on a coloured floor reads as a hole " +
                 "in the geometry. A dark blue-grey sits under every arena's palette.")]
        public Color tint = new Color(0.07f, 0.08f, 0.12f, 1f);

        [Tooltip("Metres above the surface the quad is parked, so it does not fight the " +
                 "floor for the same pixels. One centimetre is under the eye's notice at " +
                 "seven metres and well clear of any depth precision at this range.")]
        public float lift = 0.012f;

        [Tooltip("Furthest the floor may be below the feet before the shadow is dropped " +
                 "entirely. Over a pit there is nothing to put it on, and a blob hovering " +
                 "in the dark is worse than none.")]
        public float giveUpBelow = 12f;

        MaterialPropertyBlock _ink;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (quadRenderer == null && quad != null)
                quadRenderer = quad.GetComponent<MeshRenderer>();
            _ink = new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            Hide();
        }

        void LateUpdate()
        {
            if (quad == null || quadRenderer == null || player == null || player.tuning == null)
            {
                Hide();
                return;
            }

            SimWorld world = player.World;
            if (world == null)
            {
                Hide();
                return;
            }

            PlayerSimState s = player.State;
            float feetX = s.Position.X.Raw / (float)Fix.RawOne;
            float feetY = s.Position.Y.Raw / (float)Fix.RawOne;
            float depth = s.Depth.Raw / (float)Fix.RawOne;

            // Nothing underneath within giveUpBelow metres: over a pit, or falling out of
            // the world. Floor() searches exactly that far, so there is no second range
            // test to do here.
            if (!Floor(world, in s, out float floorY))
            {
                Hide();
                return;
            }

            float above = feetY - floorY;

            // Below zero only happens for a frame while the solver is pushing the body out
            // of something it has overlapped. Treat it as standing on the floor.
            float t = Mathf.Clamp01(Mathf.Max(0f, above) / Mathf.Max(0.01f, fadeOverHeight));

            float across = (width > 0.001f
                               ? width
                               : player.tuning.bodyWidth * spread) * Mathf.Lerp(1f, shrinkTo, t);

            if (!quad.gameObject.activeSelf) quad.gameObject.SetActive(true);
            quad.SetPositionAndRotation(
                new Vector3(feetX, floorY + lift, depth),
                Quaternion.Euler(90f, 0f, 0f));          // face up, lying on the floor
            quad.localScale = new Vector3(across, across * lengthInDepth, 1f);

            Color ink = tint;
            ink.a = opacity * Mathf.Lerp(1f, fadeTo, t);
            _ink.SetColor(BaseColor, ink);
            quadRenderer.SetPropertyBlock(_ink);
        }

        void Hide()
        {
            if (quad != null && quad.gameObject.activeSelf) quad.gameObject.SetActive(false);
        }

        // Tolerance for "this surface is not above my feet", matching the skin the motor
        // keeps between a body and the things it stands on.
        static readonly Fix Skin = Fix.FromMilli(2);

        /// <summary>
        /// The highest surface under the fighter, out of everything that would hold them up.
        ///
        /// The solid half is PlayerMotor.TryFindGroundBelow, called rather than copied. It
        /// is the same search -- widest solid top under the feet, in this depth lane, with
        /// gates that have not closed yet skipped -- and a second copy of it here would be a
        /// second thing to keep in step with the gate rules.
        ///
        /// The one-way half is done here, and only here, on purpose. A fighter standing on a
        /// one-way platform plainly needs their shadow on it, but TryFindGroundBelow does
        /// not consider them, and teaching it to would change where the motor SNAPS people
        /// on spawn and when they can top out a ladder. That is deterministic lockstep code
        /// driving the actual match; a blob shadow is not a reason to touch it.
        /// </summary>
        bool Floor(SimWorld world, in PlayerSimState s, out float top)
        {
            MoveConfig cfg = player.Config;
            Fix reach = Fix.FromMilli(Mathf.RoundToInt(Mathf.Max(0.5f, giveUpBelow) * 1000f));

            bool found = PlayerMotor.TryFindGroundBelow(
                s.Position, s.Depth, world, in cfg, reach, out Fix best);

            Fix half = cfg.BodyWidth / 2;
            Fix halfZ = cfg.BodyDepth / 2;
            Fix left = s.Position.X - half;
            Fix right = s.Position.X + half;
            Fix lowest = s.Position.Y - reach;

            for (int i = 0; i < world.OneWay.Length; i++)
            {
                Aabb box = world.OneWay[i];
                if (box.MaxX <= left || box.MinX >= right) continue;
                if (box.MaxY > s.Position.Y + Skin) continue;     // above the feet
                if (box.MaxY < lowest) continue;                  // too far down to matter
                if (!world.OneWaySpan(i).Reaches(s.Depth, halfZ)) continue;
                if (found && box.MaxY <= best) continue;
                best = box.MaxY;
                found = true;
            }

            top = found ? best.Raw / (float)Fix.RawOne : 0f;
            return found;
        }

    }
}
