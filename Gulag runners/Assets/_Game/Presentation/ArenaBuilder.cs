using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Authoring tool for the M0 greybox arena (docs/07).
    ///
    /// It never runs at play time. Pressing Build in the inspector creates ordinary scene
    /// objects — cubes with a <see cref="SimCollider"/> on each — that you can then select,
    /// move and tune by hand, and that get saved with the scene. Nothing in this project is
    /// spawned from code while the game is running.
    ///
    /// Proportions follow docs/05: floors 2.5 m high, rooms about 7.2 m wide. That is what
    /// makes the movement tuning meaningful — a 1.53 m jump apex visibly cannot clear a 2.5 m
    /// floor, so ladders are the only way up, which is exactly what the arena topology wants.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ArenaBuilder : MonoBehaviour
    {
        [Header("Shape")]
        [Min(1)] public int floors = 3;
        [Min(1)] public int roomsPerFloor = 3;
        public float roomWidth = 7.2f;
        [Tooltip("Floor to floor. Clear headroom is this minus the slab, and a 1.8 m fighter " +
                 "needs headroom well above their own height or jumping indoors does nothing. " +
                 "3.0 m leaves 2.7 m clear, which is 0.9 m of usable jump.")]
        public float floorHeight = 3.0f;
        public float slabThickness = 0.3f;
        public float wallThickness = 0.4f;

        [Tooltip("Height of the doorway left in every interior wall. The wall is built as a " +
                 "lintel above it, so a room is never sealed — docs/05 requires at least two " +
                 "ways out of every room, because a dead end is lethal in a game where you " +
                 "cannot see the other player coming.")]
        public float doorHeight = 2.2f;
        [Tooltip("The single gameplay plane. The arena is 3D; the fight is not (docs/02).")]
        public float planeZ;

        [Header("Connections")]
        public bool buildLadders = true;
        public float ladderWidth = 0.9f;
        public float hatchWidth = 1.8f;

        [Tooltip("Put each floor's ladder at the opposite end from the one below, so reaching " +
                 "the top floor means crossing the whole arena rather than climbing one " +
                 "chimney. Routes that differ by side are what docs/05 asks of the two wings.")]
        public bool alternateLadderSides = true;

        [Header("Movement test features (M0 greybox)")]
        [Tooltip("Adds the obstacles the movement needs to be tested against: a low step for " +
                 "ledge assist, a beam you must crouch under, a stack of one-way platforms, " +
                 "and a gap in the first floor you have to jump.")]
        public bool buildTestFeatures = true;

        [Tooltip("Size the obstacles from the player's own body instead of these fixed numbers. " +
                 "A gap, a step and a beam only mean anything relative to how tall the character " +
                 "is — change the character's height and fixed numbers quietly stop working: " +
                 "a beam stops forcing a crouch, a gap stops being jumpable.")]
        public bool scaleFeaturesToPlayer = true;

        [Tooltip("Width of the hole in the TOP floor, the only one open to the sky. Indoors the " +
                 "ceiling cuts a jump short, so a gap to jump only makes sense up here. Ignored " +
                 "when Scale Features To Player is on.")]
        public float floorGapWidth = 1.4f;

        public bool buildCages = true;

        [Header("Materials (optional)")]
        public Material solidMaterial;
        public Material oneWayMaterial;
        public Material ladderMaterial;

        [Header("After building")]
        [Tooltip("Border these to the arena once it is built.")]
        public SideViewCamera cameraToFit;
        [Tooltip("Moved to the left spawn cage once the arena is built.")]
        public Transform playerToPlace;

        public const string GeneratedRootName = "GeneratedArena";

        public float TotalWidth => roomsPerFloor * roomWidth;
        public float TotalHeight => floors * floorHeight;
        public float LeftEdge => -TotalWidth * 0.5f;
        public float RightEdge => TotalWidth * 0.5f;
        /// <summary>X of the ladder that leads up from the given floor.</summary>
        public float LadderXForFloor(int floor)
        {
            bool leftSide = !alternateLadderSides || floor % 2 == 0;
            return leftSide ? LeftEdge + roomWidth * 0.5f : RightEdge - roomWidth * 0.5f;
        }

        public Transform FindGeneratedRoot()
        {
            Transform t = transform.Find(GeneratedRootName);
            return t;
        }

        void OnDrawGizmos()
        {
            // Draw the arena envelope even before anything is built, so the shape can be
            // judged from the inspector numbers alone.
            Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.6f);
            Gizmos.DrawWireCube(
                new Vector3(0f, TotalHeight * 0.5f, planeZ),
                new Vector3(TotalWidth, TotalHeight, 0.05f));

            Gizmos.color = new Color(1f, 1f, 1f, 0.18f);
            for (int f = 1; f < floors; f++)
            {
                float y = f * floorHeight;
                Gizmos.DrawLine(new Vector3(LeftEdge, y, planeZ), new Vector3(RightEdge, y, planeZ));
            }
        }
    }
}
