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

        [Header("The gameplay plane")]
        [Tooltip("Only bake colliders that reach the gameplay plane. docs/02: the arena is 3D, " +
                 "the fight is not — depth is presentation. Without this, scenery standing five " +
                 "metres behind the plane is flattened onto it and becomes a wall the player " +
                 "walks into for no visible reason.")]
        public bool restrictToPlane = true;

        [Tooltip("Z of the gameplay plane. Match it to the players' Plane Z.")]
        public float planeZ;

        [Tooltip("How far either side of the plane still counts as being on it, in metres. " +
                 "Wide enough for geometry authored a little off-centre, narrow enough to leave " +
                 "the backdrop alone.")]
        public float planeThickness = 1.5f;

        [Tooltip("Hold objects marked with a SimCollider to the same depth band.\n\n" +
                 "On by default. A marker says WHAT a box is — solid, one-way, a ladder — not " +
                 "that it is on the gameplay plane, and the quickest way to author a location " +
                 "is to mark everything in it. With this off, marking the whole level puts the " +
                 "whole level on the plane, including the wall behind it, and the first " +
                 "symptom is a fighter who spawns inside that wall and falls through the floor. " +
                 "Ignore is still honoured either way: that is the escape hatch for something " +
                 "that IS on the plane and still must not be solid.")]
        public bool planeFiltersMarked = true;

        [Header("Ladders")]
        [Tooltip("Cut a hole in the baked floor wherever a ladder runs through it.\n\n" +
                 "Off by default, and usually wants to stay off: the motor already treats a " +
                 "ladder as its own shaft, so the climb is never stopped by a floor the ladder " +
                 "passes through, and a ladder that ends inside its landing sets the fighter " +
                 "down ON it rather than beside it. Cutting the floor as well leaves a real " +
                 "hole the fighter can fall through, in a level whose art has no hole in it. " +
                 "Turn it on only where the art DOES have one and you want the hole to be " +
                 "walkable-into.")]
        public bool ladderCutsHatch;

        [Tooltip("Extra clearance either side of the hatch, in metres.")]
        public float hatchMargin = 0.1f;

        [Tooltip("How far a ladder has to reach INTO a solid before that counts as passing " +
                 "through it, in metres.\n\n" +
                 "Without this, a ladder whose foot is modelled one centimetre inside the " +
                 "platform it stands on punches a hole in that platform, and the fighter who " +
                 "spawns there falls through the floor. A ladder resting on a slab wants the " +
                 "slab, not a hatch; only one that runs through it wants a hatch.")]
        public float hatchMinOverlap = 0.05f;

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

        /// <summary>
        /// The Z each baked ladder was modelled at, in the same order as World.Ladders.
        ///
        /// The simulation is flat and has no use for it. Presentation does: a ladder standing
        /// in front of the platform it serves wants the body climbing at ITS depth, stepping
        /// back onto the plane at the top, or the climb reads as a fighter swimming through
        /// the floor.
        /// </summary>
        public float[] LadderDepths { get; private set; } = System.Array.Empty<float>();
        public string LastReport { get; private set; } = "not baked yet";

        readonly List<Rect> _gizmoSolid = new List<Rect>();
        readonly List<Rect> _gizmoOneWay = new List<Rect>();
        readonly List<Rect> _gizmoLadder = new List<Rect>();
        readonly List<SimCollider> _markedBuffer = new List<SimCollider>();
        readonly List<Collider> _colliderBuffer = new List<Collider>();
        readonly List<Chest> _chestBuffer = new List<Chest>();
        readonly List<Chest> _chestSources = new List<Chest>();
        readonly List<Rect> _gizmoChest = new List<Rect>();
        readonly List<float> _ladderDepth = new List<float>();

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
            _ladderDepth.Clear();

            int roots = 0, markedSeen = 0, markedSkipped = 0, markedIgnored = 0, markedOffPlane = 0,
                chestsSeen = 0, chestsSkipped = 0;
            List<string> rootNames = new List<string>();
            int collidersSeen = 0, skippedTrigger = 0, skippedPlayer = 0, skippedLayer = 0,
                skippedAlreadyMarked = 0, skippedInactive = 0, skippedChest = 0, skippedDepth = 0;

            HashSet<int> handled = new HashSet<int>();

            // Everything the depth band threw away, kept to hand: a band that culls the whole
            // arena is a band in the wrong place, not an empty arena, and handing the match a
            // world with no floor is the worst of the two answers.
            List<Aabb> culled = new List<Aabb>();

            foreach (GameObject root in RootObjects())
            {
                roots++;
                if (rootNames.Count < 12) rootNames.Add(root.name);

                // 1. Objects explicitly marked with a SimCollider always win.
                root.GetComponentsInChildren(true, _markedBuffer);
                foreach (SimCollider c in _markedBuffer)
                {
                    markedSeen++;

                    // enabled && activeInHierarchy, NOT isActiveAndEnabled. The two are not
                    // the same here: isActiveAndEnabled is false for a component Unity has
                    // not activated yet, and this bake runs in Awake at execution order -100,
                    // which is to say before anything else in the scene has been activated.
                    // It read every marker in a hand-marked location as inactive and baked an
                    // arena of two boxes, and both fighters fell through the world.
                    if (!c.enabled || !c.gameObject.activeInHierarchy)
                    { markedSkipped++; continue; }
                    handled.Add(c.gameObject.GetInstanceID());
                    if (c.kind == SimColliderKind.Ignore) { markedIgnored++; continue; }

                    Bounds mb = c.WorldBounds;
                    if (planeFiltersMarked && restrictToPlane && !ReachesPlane(mb))
                    {
                        markedOffPlane++;
                        if (c.kind == SimColliderKind.Solid)
                            culled.Add(SimCollider.RectToAabb(
                                new Rect(mb.min.x, mb.min.y, mb.size.x, mb.size.y)));
                        continue;
                    }

                    Add(c.kind, new Rect(mb.min.x, mb.min.y, mb.size.x, mb.size.y),
                        solids, oneWay, ladders, mb.center.z);
                }

                // 2. Chests. Collected before the ordinary colliders so that a chest's own
                //    collider is never baked into a wall: you walk into a chest, not against it.
                //    The order they are found in IS the chest order, and the round's chest states
                //    are an array parallel to it, so it has to be the scene order on both devices.
                root.GetComponentsInChildren(true, _chestBuffer);
                foreach (Chest chest in _chestBuffer)
                {
                    chestsSeen++;
                    if (!chest.enabled || !chest.gameObject.activeInHierarchy)
                    { chestsSkipped++; continue; }
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

                    Bounds cb = col.bounds;
                    if (restrictToPlane && !ReachesPlane(cb))
                    {
                        skippedDepth++;
                        culled.Add(SimCollider.RectToAabb(
                            new Rect(cb.min.x, cb.min.y, cb.size.x, cb.size.y)));
                        continue;
                    }

                    int layerBit = 1 << col.gameObject.layer;
                    SimColliderKind kind;
                    if ((ladderLayers.value & layerBit) != 0) kind = SimColliderKind.Ladder;
                    else if ((oneWayLayers.value & layerBit) != 0) kind = SimColliderKind.OneWay;
                    else if ((solidLayers.value & layerBit) != 0) kind = SimColliderKind.Solid;
                    else { skippedLayer++; continue; }

                    Add(kind, new Rect(cb.min.x, cb.min.y, cb.size.x, cb.size.y),
                        solids, oneWay, ladders, cb.center.z);
                }
            }

            // The one failure this must not pass on: a band so far from the level that it
            // leaves no floor at all. Take the band back rather than start a match nobody can
            // stand up in, and say so with both numbers so it can be put right.
            bool bandTookEverything = solids.Count == 0 && culled.Count > 0;
            if (bandTookEverything) solids.AddRange(culled);

            int hatches = ladderCutsHatch ? CutHatches(solids, ladders) : 0;

            World = new SimWorld
            {
                Solids = solids.ToArray(),
                OneWay = oneWay.ToArray(),
                Ladders = ladders.ToArray(),
                Chests = chests.ToArray()
            };
            ChestSources = _chestSources.ToArray();
            LadderDepths = _ladderDepth.ToArray();

            LastReport =
                $"{roots} roots [{string.Join(", ", rootNames)}]; {markedSeen} SimCollider " +
                $"({markedSkipped} inactive, {markedIgnored} ignored, " +
                $"{markedOffPlane} off the plane), " +
                $"{collidersSeen} Unity collider (skipped: {skippedAlreadyMarked} already marked, " +
                $"{skippedTrigger} trigger, {skippedPlayer} on a player, {skippedChest} on a chest, " +
                $"{skippedDepth} off the plane, {skippedLayer} wrong layer, " +
                $"{skippedInactive} inactive) -> baked {World.Solids.Length} solid " +
                $"({hatches} cut by ladders), " +
                $"{World.OneWay.Length} one-way, {World.Ladders.Length} ladder, " +
                $"{World.Chests.Length} chest ({chestsSkipped} inactive of {chestsSeen} seen)";

            if (markedSeen > 0 && markedSkipped == markedSeen)
                Debug.LogError(
                    $"{name}: every one of the {markedSeen} SimColliders in this " +
                    "scene was skipped as inactive, which is never a scene anybody " +
                    "built on purpose. If they look active in the hierarchy then " +
                    "the bake is asking at the wrong moment, not the scene being " +
                    $"wrong.\n{LastReport}", this);

            if (bandTookEverything)
                Debug.LogError(
                    $"{name}: the gameplay plane (z {planeZ} +-{planeThickness}) culled EVERY " +
                    $"solid box in this scene, so the band is in the wrong place rather than " +
                    $"the scene being empty. Baking all {culled.Count} of them anyway so the " +
                    $"match is playable, which means the scenery is in the fight too. Move the " +
                    $"band onto the level: the fighters' own Plane Z is where it belongs.\n" +
                    $"{LastReport}", this);

            if (Application.isPlaying)
            {
                if (World.Solids.Length == 0)
                    Debug.LogError($"{name}: baked no solid boxes, so there is no floor and the " +
                                   $"player will fall forever.\n{LastReport}\n" +
                                   "If SimCollider count is 0, the arena objects are not in this " +
                                   "scene or are inactive. If they were seen but skipped, the " +
                                   "reason is in the list above.", this);
                else if (World.Solids.Length < 4)
                    Debug.LogWarning($"{name}: only {World.Solids.Length} solid boxes came out " +
                                     $"of this scene, which is almost certainly not an arena.\n" +
                                     $"{LastReport}", this);
                else if (logBakeReport)
                    Debug.Log($"{name}: {LastReport}", this);
            }

            return World;
        }

        /// <summary>
        /// Opens a hatch through every solid a ladder runs through, and returns how many it cut.
        ///
        /// A solid the ladder merely STANDS on is left alone — that is the floor it starts from,
        /// not something in its way. That distinction is measured, not assumed: the ladder has to
        /// reach <see cref="hatchMinOverlap"/> into the solid before it counts as running through
        /// it, because hand-built levels have ladder feet a centimetre inside their own platform
        /// and a hole there is a fighter falling out of the arena.
        /// </summary>
        int CutHatches(List<Aabb> solids, List<Aabb> ladders)
        {
            if (ladders.Count == 0 || solids.Count == 0) return 0;

            Fix margin = Fix.FromMilli(Mathf.RoundToInt(Mathf.Max(0f, hatchMargin) * 1000f));
            Fix minOverlap =
                Fix.FromMilli(Mathf.RoundToInt(Mathf.Max(0f, hatchMinOverlap) * 1000f));
            Fix sliver = Fix.FromMilli(50);
            int cut = 0;

            List<Aabb> working = new List<Aabb>(solids.Count + ladders.Count);

            foreach (Aabb ladder in ladders)
            {
                Fix holeMin = ladder.MinX - margin;
                Fix holeMax = ladder.MaxX + margin;

                working.Clear();
                foreach (Aabb solid in solids)
                {
                    bool crossesX = solid.MinX < holeMax && solid.MaxX > holeMin;

                    // How much of the solid's height the ladder actually occupies. A ladder
                    // standing ON a slab shares an edge with it and no more; one running
                    // THROUGH it covers the whole thickness.
                    Fix overlapY = Fix.Min(solid.MaxY, ladder.MaxY)
                                 - Fix.Max(solid.MinY, ladder.MinY);

                    if (!crossesX || overlapY <= minOverlap) { working.Add(solid); continue; }

                    cut++;

                    if (holeMin - solid.MinX > sliver)
                        working.Add(new Aabb(solid.MinX, solid.MinY, holeMin, solid.MaxY));

                    if (solid.MaxX - holeMax > sliver)
                        working.Add(new Aabb(holeMax, solid.MinY, solid.MaxX, solid.MaxY));
                }

                solids.Clear();
                solids.AddRange(working);
            }

            return cut;
        }

        /// <summary>
        /// Does this collider reach the gameplay plane. A level built in 3D has a foreground and
        /// a background, and only the slab in the middle is the game.
        /// </summary>
        bool ReachesPlane(Bounds b) =>
            b.max.z >= planeZ - planeThickness && b.min.z <= planeZ + planeThickness;

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
                 List<Aabb> solids, List<Aabb> oneWay, List<Aabb> ladders, float depth)
        {
            if (r.width <= 0f || r.height <= 0f) return;

            Aabb box = SimCollider.RectToAabb(r);
            switch (kind)
            {
                case SimColliderKind.OneWay: oneWay.Add(box); _gizmoOneWay.Add(r); break;
                case SimColliderKind.Ladder:
                    ladders.Add(box); _gizmoLadder.Add(r); _ladderDepth.Add(depth); break;
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
