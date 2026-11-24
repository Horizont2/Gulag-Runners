using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// One screen, one camera: the local player's.
    ///
    /// EVERY ARENA SHIPS WITH TWO CAMERAS AND BOTH OF THEM RENDERING. Camera Player 1 is
    /// authored into the top half of the screen and Camera Player 2 into the bottom, each
    /// 61% wide and inset 20% from the left, with applyViewportRect ticked on both. That
    /// is a useful layout and it is the right one for exactly one situation: two people
    /// sharing a keyboard while somebody tunes the combat. In an online match it is a
    /// phone showing the player two letterboxed strips, one of which is somebody else's
    /// fight -- which is what "I have two cameras in matchmaking" is.
    ///
    /// SideViewCamera's own tooltip has said the rule the whole time, next to the tick box
    /// that breaks it: on a device each player owns the whole screen. Nothing was applying
    /// it, because nothing in the scene knew how many people were looking at it.
    ///
    /// SO IT IS DECIDED FROM WHO IS DRIVING, not from a platform test and not from a flag
    /// somebody has to remember. Two cameras are right when there are two local humans, and
    /// there are two local humans only when no wire has claimed a side AND no brain is
    /// driving one of the fighters:
    ///
    ///   online        MatchState.LocalWasWired -- a second device is at the far end, so
    ///                 there is one person here and the screen is theirs.
    ///   against a bot FighterBrain.TakingOver on the other fighter. One human.
    ///   two on a desk  neither. The authored split stands, untouched.
    ///
    /// Which means the editor keeps its split-screen and a build does not, without either
    /// of them being named anywhere.
    ///
    /// Polled in LateUpdate, and it writes only when the answer CHANGES. Both inputs move
    /// after the scene has loaded: the wire sets the local side at the top of a match, and
    /// docs/22 makes Take Over a tick box somebody flips mid-round without restarting. A
    /// camera rig that read either of them once at Awake would be wrong for the whole
    /// match, which is the exact fault MatchState.LocalFighter exists to describe.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)]         // after SideViewCamera has framed itself
    public sealed class LocalCamera : MonoBehaviour
    {
        [Tooltip("Empty finds the one in the scene.")]
        public MatchState matchState;

        [Tooltip("The rigs this chooses between. Empty finds every SideViewCamera in the " +
                 "scene, which is what an arena wants; fill it in to leave one alone.")]
        public SideViewCamera[] cameras;

        [Tooltip("Keep the authored split-screen when two local humans are playing.\n\n" +
                 "Off, and the first fighter's camera takes the whole screen even with two " +
                 "people on one keyboard -- which is worth having while framing a shot.")]
        public bool splitForTwoLocalHumans = true;

        [Header("Debug")]
        [Tooltip("What it decided, and why.")]
        public string showing = "";

        int _was = -2;
        bool _wasSplit;
        bool _written;

        void Awake()
        {
            if (matchState == null) matchState = FindFirstObjectByType<MatchState>();
            if (cameras == null || cameras.Length == 0)
                cameras = FindObjectsByType<SideViewCamera>(FindObjectsInactive.Include,
                                                            FindObjectsSortMode.None);
        }

        void LateUpdate()
        {
            if (cameras == null || cameras.Length < 2) return;

            int local = matchState != null ? matchState.LocalFighter : 0;
            bool split = splitForTwoLocalHumans && TwoLocalHumans();

            if (_written && local == _was && split == _wasSplit) return;
            _was = local;
            _wasSplit = split;
            _written = true;

            if (split)
            {
                // Hands off entirely. Restoring the authored rect is not this component's
                // to do -- it never changed it -- and a rect written back from a guess
                // would be worse than the one in the scene.
                foreach (SideViewCamera rig in cameras)
                {
                    if (rig == null) continue;
                    Camera cam = rig.GetComponent<Camera>();
                    if (cam != null) cam.enabled = true;
                }
                showing = "both: two local humans, the scene's own split";
                return;
            }

            int shown = 0;
            foreach (SideViewCamera rig in cameras)
            {
                if (rig == null) continue;
                Camera cam = rig.GetComponent<Camera>();
                if (cam == null) continue;

                bool mine = IndexOf(rig) == local;
                cam.enabled = mine;

                if (!mine) continue;
                shown++;

                // The whole glass. applyViewportRect is what the arena ticked to get the
                // split, and SideViewCamera re-applies the rect every frame, so this has
                // to clear the FLAG rather than the rect -- writing a full-screen rect
                // would be overwritten on the next line of its own Update.
                rig.applyViewportRect = false;
                cam.rect = new Rect(0f, 0f, 1f, 1f);
            }

            if (shown == 1)
            {
                showing = $"fighter {local} only, full screen"
                          + (matchState != null && matchState.LocalWasWired
                                 ? " (a wire said so)" : " (the other side is a bot)");
                return;
            }

            // No camera claims the local fighter. Rather than leave a black screen, the
            // first rig takes it and says so: a wrong camera is a bug somebody reports,
            // and nothing drawn at all is a bug they cannot describe.
            foreach (SideViewCamera rig in cameras)
            {
                if (rig == null) continue;
                Camera cam = rig.GetComponent<Camera>();
                if (cam == null) continue;
                cam.enabled = true;
                rig.applyViewportRect = false;
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                break;
            }

            showing = $"no rig follows fighter {local}; the first one has the screen";
            Debug.LogWarning(
                $"{name}: no SideViewCamera's Player is the fighter at index {local}, so "
                + "the first rig has been given the whole screen. Each camera's Player "
                + "slot has to point at the PlayerController whose index it follows -- "
                + "that index is what MatchState.LocalFighter is an index INTO.", this);
        }

        /// <summary>Which fighter this rig follows, or -1 when it does not say.</summary>
        static int IndexOf(SideViewCamera rig) =>
            rig != null && rig.player != null ? rig.player.playerIndex : -1;

        /// <summary>
        /// Are two people looking at this screen?
        ///
        /// Only when nothing has claimed a side for a far end, and at least two of the
        /// rigs follow a fighter no brain is driving. Both halves are needed: a networked
        /// match switches every brain OFF -- neither fighter is a bot any more -- so the
        /// brain test alone would call a 1v1 over the wire a pair of local players, which
        /// is the split screen this exists to remove.
        /// </summary>
        bool TwoLocalHumans()
        {
            if (matchState != null && matchState.LocalWasWired) return false;

            int humans = 0;
            foreach (SideViewCamera rig in cameras)
            {
                if (rig == null || rig.player == null) continue;
                FighterBrain brain = rig.player.GetComponentInChildren<FighterBrain>(true);
                if (brain == null || !brain.TakingOver) humans++;
            }
            return humans > 1;
        }
    }
}
