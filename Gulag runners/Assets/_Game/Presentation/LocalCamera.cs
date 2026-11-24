using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// One screen, one camera: the local player's. Every frame, whoever else writes it.
    ///
    /// EVERY ARENA SHIPS WITH TWO CAMERAS AND BOTH OF THEM RENDERING. Camera Player 1 is
    /// authored into the top half of the screen and Camera Player 2 into the bottom, each
    /// 61% wide and inset 20% from the left, with applyViewportRect ticked on both. That
    /// is the right layout for two people sharing a keyboard while somebody tunes the
    /// combat, and on a phone it is two letterboxed strips, one of which is a second view
    /// of a fight nobody on this device is watching.
    ///
    /// SideViewCamera's own tooltip has stated the rule the whole time, next to the tick
    /// box that breaks it: on a device each player owns the whole screen.
    ///
    /// WHY THIS IS NOT A FLAG ON SOMETHING ELSE, and why it runs every frame.
    ///
    /// NetMatch used to own half of this, in OneCamera(bool), and it was right about what
    /// to do and wrong about when. It ran from the point a network LINK took the clock, so
    /// a match against a bot -- the whole of a player's first games -- never called it and
    /// kept the split screen. Worse, its partner call put the split BACK on release, so
    /// the two components took turns: this one would decide, NetMatch would undo it, and
    /// this one never noticed because it only wrote when its own inputs changed.
    ///
    /// A fact with two owners goes stale, which is the argument MatchState.LocalFighter
    /// already makes about which fighter a device drives. So there is one owner now, and
    /// it re-asserts rather than remembers: every LateUpdate it compares what the cameras
    /// ARE with what they should be and writes only the fields that are wrong. That costs
    /// two comparisons a frame and cannot be undone by anything that writes after it.
    ///
    /// THE SPLIT IS OFF BY DEFAULT AND EDITOR-ONLY. There is one touch control rig in the
    /// HUD -- one stick, one set of rings -- and PlayerInputRouter merges one device
    /// source per fighter, so a second local human is a keyboard arrangement for
    /// development and not a mode this game has. A build therefore has exactly one person
    /// looking at the screen, always, and the only way to get two viewports is to ask for
    /// them in the editor on purpose.
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

        [Tooltip("Keep the arena's authored split-screen rects, so two people on one " +
                 "keyboard each get half the screen.\n\n" +
                 "IN THE EDITOR ONLY, and off even there unless you tick it. A build has " +
                 "one person looking at it -- the HUD has one stick -- so two viewports " +
                 "in a player is never right, and this is the switch that makes that " +
                 "impossible rather than unlikely.")]
        public bool splitInTheEditor;

        [Header("Debug")]
        [Tooltip("What it is doing, and why.")]
        public string showing = "";

        [Tooltip("Say it in the log as well, once per change.\n\n" +
                 "On by default, and worth keeping: the field above answers this in the " +
                 "editor and a player's log is the only place it can be answered in a " +
                 "build. One line per change is nothing, and the question -- which " +
                 "camera is live, and why -- is the one that took two builds to settle.")]
        public bool logChanges = true;

        string _said = "";

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

            if (splitInTheEditor && Application.isEditor)
            {
                // Hands off the rects entirely -- they were authored, and a rect written
                // back from a guess would be worse than the one in the scene. Only the
                // Camera components are made sure of, because something else may have
                // switched one off.
                foreach (SideViewCamera rig in cameras)
                {
                    Camera cam = CameraOf(rig);
                    if (cam != null && !cam.enabled) cam.enabled = true;
                }
                Report("both, split as the scene authored it (editor only)");
                return;
            }

            int local = matchState != null ? matchState.LocalFighter : 0;
            int shown = 0;

            foreach (SideViewCamera rig in cameras)
            {
                Camera cam = CameraOf(rig);
                if (cam == null) continue;

                bool mine = IndexOf(rig) == local;
                if (cam.enabled != mine) cam.enabled = mine;
                if (!mine) continue;
                shown++;

                // The whole glass. applyViewportRect is what the arena ticked to get the
                // split and SideViewCamera re-applies the rect every frame, so clearing
                // the FLAG is what sticks; writing a full-screen rect alone would be
                // overwritten on the next line of its own Update.
                if (rig.applyViewportRect) rig.applyViewportRect = false;
                if (cam.rect != Full) cam.rect = Full;
            }

            if (shown == 1)
            {
                Report($"fighter {local} only, full screen"
                       + (matchState != null && matchState.LocalWasWired
                              ? " (a wire said which side we are)"
                              : " (no wire; the fighter no brain is driving)"));
                return;
            }

            // No rig claims the local fighter. Rather than leave a black screen the first
            // one takes it and says so: a wrong camera is a bug somebody reports, and
            // nothing drawn at all is a bug they cannot describe.
            foreach (SideViewCamera rig in cameras)
            {
                Camera cam = CameraOf(rig);
                if (cam == null) continue;
                if (!cam.enabled) cam.enabled = true;
                if (rig.applyViewportRect) rig.applyViewportRect = false;
                if (cam.rect != Full) cam.rect = Full;
                break;
            }

            if (showing.StartsWith("no rig")) return;        // said once, not every frame
            Report($"no rig follows fighter {local}; the first one has the screen");
            Debug.LogWarning(
                $"{name}: no SideViewCamera's Player is the fighter at index {local}, so "
                + "the first rig has been given the whole screen. Each camera's Player "
                + "slot has to point at the PlayerController whose index it follows -- "
                + "that index is what MatchState.LocalFighter is an index INTO.", this);
        }

        /// <summary>Records what it decided, and says it once when it changes.</summary>
        void Report(string what)
        {
            showing = what;
            if (!logChanges || what == _said) return;
            _said = what;
            Debug.Log($"{name}: {what}.", this);
        }

        static readonly Rect Full = new Rect(0f, 0f, 1f, 1f);

        static Camera CameraOf(SideViewCamera rig) =>
            rig != null ? rig.GetComponent<Camera>() : null;

        /// <summary>Which fighter this rig follows, or -1 when it does not say.</summary>
        static int IndexOf(SideViewCamera rig) =>
            rig != null && rig.player != null ? rig.player.playerIndex : -1;
    }
}
