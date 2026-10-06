using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Turns the colliders in the scene into one immutable <see cref="SimWorld"/>.
    ///
    /// The simulation does its own collision and never uses Unity physics, because PhysX is not
    /// deterministic across devices and rollback needs bit-identical results (docs/06). Unity
    /// colliders are therefore only an authoring surface: their world-space bounds are read once
    /// and copied into the simulation.
    ///
    /// Any ordinary BoxCollider works — no special component is needed. Add a
    /// <see cref="SimCollider"/> only to mark something as a one-way platform or a ladder, or use
    /// the layer masks below.
    ///
    /// The scan walks the scene's root objects rather than calling FindObjectsByType. It is the
    /// same result when everything is healthy, but it is explicit about what it looked at, which
    /// is what makes the report below possible — and a bake that silently finds nothing is the
    /// single most confusing failure in this project, because it looks exactly like broken
    /// physics.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class SimWorldBaker : MonoBehaviour
    {
        public static SimWorldBaker Instance { get; private set; }

        [Header("What to bake")]
        [Tooltip("Bake every ordinary Unity collider in the scene, not only objects carrying a " +
                 "SimCollider. Leave this on unless you want to hand-pick every box.")]
        public bool bakeUnityColliders = true;

        [Tooltip("Colliders on these layers become solid walls and floors.")]
        public LayerMask solidLayers = ~0;

        [Tooltip("Colliders on these layers become one-way platforms. A SimCollider overrides it.")]
        public LayerMask oneWayLayers = 0;

        [Tooltip("Colliders on these layers become ladders. A SimCollider overrides it.")]
        public LayerMask ladderLayers = 0;

        [Tooltip("Trigger colliders are skipped by default: they are usually zones, not geometry.")]
        public bool includeTriggers;

        [Header("Options")]
        [Tooltip("Rebake every frame. Editor convenience while dragging platforms about; " +
                 "turn it off in a build.")]
        public bool rebakeEveryFrame;

        [Tooltip("Draw the baked boxes in the scene view, so what the simulation sees is visible " +
                 "rather than assumed.")]
        public bool drawBakedBoxes = true;

        [Tooltip("Log a one-line summary of every bake. Worth leaving on until the arena is final.")]
        public bool logBakeReport = true;

        public SimWorld World { get; private set; }

        /// <summary>
        /// The Chest components behind World.Chests, in the same order. Presentation uses it to
        /// find which baked chest it is drawing, which is a lookup rather than a guess.
        /// </summary>
        public Chest[] ChestSources { get; private set; } = System.Array.Empty<Chest>();
        public string LastReport { get; private set; } = "not baked yet";

        readonly List<Rect> _gizmoSolid = new List<Rect>();
        readonly List<Rect> _gizmoOneWay = new List<Rect>();
        readonly List<Rect> _gizmoLadder = new List<Rect>();
        readonly List<SimCollider> _markedBuffer = new List<SimCollider>();
        readonly List<Collider> _colliderBuffer = new List<Collider>();
        readonly List<Chest> _chestBuffer = new List<Chest>();
        readonly List<Chest> _chestSources = new List<Chest>();
        readonly List<Rect> _gizmoChest = new List<Rect>();

        void Awake()
        {
            Instance = this;
            Bake();
        }

        void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        void Start()
        {
            // Second chance. If anything about load order left the first bake empty, this catches
            // it before the player has fallen anywhere, and the report says what was seen.
            if (World == null || World.Solids.Length == 0) Bake();
        }

        void LateUpdate()
        {
            if (rebakeEveryFrame) Bake();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [ContextMenu("Bake now")]
        public SimWorld Bake()
        {
            List<Aabb> solids = new List<Aabb>();
            List<Aabb> oneWay = new List<Aabb>();
            List<Aabb> ladders = new List<Aabb>();
            List<ChestDef> chests = new List<ChestDef>();

            _gizmoSolid.Clear();
            _gizmoOneWay.Clear();
            _gizmoLadder.Clear();
            _gizmoChest.Clear();
            _chestSources.Clear();

            int roots = 0, markedSeen = 0, markedSkipped = 0, chestsSeen = 0, chestsSkipped = 0;
            List<string> rootNames = new List<string>();
            int collidersSeen = 0, skippedTrigger = 0, skippedPlayer = 0, skippedLayer = 0,
                skippedAlreadyMarked = 0, skippedInactive = 0, skippedChest = 0;

            HashSet<int> handled = new HashSet<int>();

            foreach (GameObject root in RootObjects())
            {
                roots++;
                if (rootNames.Count < 12) rootNames.Add(root.name);

                // 1. Objects explicitly marked with a SimCollider always win.
                root.GetComponentsInChildren(true, _markedBuffer);
                foreach (SimCollider c in _markedBuffer)
                {
                    markedSeen++;
                    if (!c.isActiveAndEnabled) { markedSkipped++; continue; }
                    handled.Add(c.gameObject.GetInstanceID());
                    Add(c.kind, c.ToRect(), solids, oneWay, ladders);
                }

                // 2. Chests. Collected before the ordinary colliders so that a chest's own
                //    collider is never baked into a wall: you walk into a chest, not against it.
                //    The order they are found in IS the chest order, and the round's chest states
                //    are an array parallel to it, so it has to be the scene order on both devices.
                root.GetComponentsInChildren(true, _chestBuffer);
                foreach (Chest chest in _chestBuffer)
                {
                    chestsSeen++;
                    if (!chest.isActiveAndEnabled) { chestsSkipped++; continue; }
                    handled.Add(chest.gameObject.GetInstanceID());
                    chests.Add(chest.ToDef());
                    _chestSources.Add(chest);
                    _gizmoChest.Add(chest.ToRect());
                }

                if (!bakeUnityColliders) continue;

                // 3. Then every ordinary collider not already handled.
                root.GetComponentsInChildren(true, _colliderBuffer);
                foreach (Collider col in _colliderBuffer)
                {
                    collidersSeen++;
                    if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                    { skippedInactive++; continue; }
                    if (col.isTrigger && !includeTriggers) { skippedTrigger++; continue; }
                    if (handled.Contains(col.gameObject.GetInstanceID()))
                    { skippedAlreadyMarked++; continue; }

                    // Never bake a player's own body: it would become a wall it stands inside.
                    if (col.GetComponentInParent<PlayerController>() != null)
                    { skippedPlayer++; continue; }

                    // Nor anything belonging to a chest, including whatever colliders came with
                    // its model. You walk into a chest to open it; a chest you bump against is
                    // one you can never reach.
                    if (col.GetComponentInParent<Chest>() != null)
                    { skippedChest++; continue; }

                    int layerBit = 1 << col.gameObject.layer;
                    SimColliderKind kind;
                    if ((ladderLayers.value & layerBit) != 0) kind = SimColliderKind.Ladder;
                    else if ((oneWayLayers.value & layerBit) != 0) kind = SimColliderKind.OneWay;
                    else if ((solidLayers.value & layerBit) != 0) kind = SimColliderKind.Solid;
                    else { skippedLayer++; continue; }

                    Bounds b = col.bounds;
                    Add(kind, new Rect(b.min.x, b.min.y, b.size.x, b.size.y),
                        solids, oneWay, ladders);
                }
            }

            World = new SimWorld
            {
                Solids = solids.ToArray(),
                OneWay = oneWay.ToArray(),
                Ladders = ladders.ToArray(),
                Chests = chests.ToArray()
            };
            ChestSources = _chestSources.ToArray();

            LastReport =
                $"{roots} roots [{string.Join(", ", rootNames)}]; {markedSeen} SimCollider ({markedSkipped} inactive), " +
                $"{collidersSeen} Unity collider (skipped: {skippedAlreadyMarked} already marked, " +
                $"{skippedTrigger} trigger, {skippedPlayer} on a player, {skippedChest} on a chest, " +
                $"{skippedLayer} wrong layer, " +
                $"{skippedInactive} inactive) -> baked {World.Solids.Length} solid, " +
                $"{World.OneWay.Length} one-way, {World.Ladders.Length} ladder, " +
                $"{World.Chests.Length} chest ({chestsSkipped} inactive of {chestsSeen} seen)";

            if (Application.isPlaying)
            {
                if (World.Solids.Length == 0)
                    Debug.LogError($"{name}: baked no solid boxes, so there is no floor and the " +
                                   $"player will fall forever.\n{LastReport}\n" +
                                   "If SimCollider count is 0, the arena objects are not in this " +
                                   "scene or are inactive. If they were seen but skipped, the " +
                                   "reason is in the list above.", this);
                else if (logBakeReport)
                    Debug.Log($"{name}: {LastReport}", this);
            }

            return World;
        }

        /// <summary>
        /// The scene's roots. Falls back to the active scene if this object's own scene is not
        /// usable yet, which can happen very early in the load.
        /// </summary>
        IEnumerable<GameObject> RootObjects()
        {
            Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) yield break;

            foreach (GameObject go in scene.GetRootGameObjects()) yield return go;
        }

        void Add(SimColliderKind kind, Rect r,
                 List<Aabb> solids, List<Aabb> oneWay, List<Aabb> ladders)
        {
            if (r.width <= 0f || r.height <= 0f) return;

            Aabb box = SimCollider.RectToAabb(r);
            switch (kind)
            {
                case SimColliderKind.OneWay: oneWay.Add(box); _gizmoOneWay.Add(r); break;
                case SimColliderKind.Ladder: ladders.Add(box); _gizmoLadder.Add(r); break;
                default: solids.Add(box); _gizmoSolid.Add(r); break;
            }
        }

        void OnDrawGizmosSelected()
        {
            if (!drawBakedBoxes) return;
            if (!Application.isPlaying && World == null) Bake();

            Draw(_gizmoSolid, new Color(0.2f, 0.9f, 1f, 0.8f));
            Draw(_gizmoOneWay, new Color(1f, 0.85f, 0.2f, 0.8f));
            Draw(_gizmoLadder, new Color(0.4f, 1f, 0.4f, 0.8f));
            Draw(_gizmoChest, new Color(1f, 0.45f, 0.85f, 0.8f));
        }

        void Draw(List<Rect> rects, Color color)
        {
            Gizmos.color = color;
            foreach (Rect r in rects)
                Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, 0f),
                                    new Vector3(r.width, r.height, 0.05f));
        }
    }
}
