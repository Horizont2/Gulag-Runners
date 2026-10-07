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

        [Tooltip("Drop whatever stands wholly behind the gameplay plane instead of baking it.\n\n" +
                 "docs/02: the location is 3D, the fight is a flat lane through the front of it. " +
                 "Everything behind that lane — the wall at the back, the pillars holding the " +
                 "walkway up, the handrail — is there to be looked at. The depth band alone does " +
                 "not settle it: the band is wide enough to catch geometry authored a little off " +
                 "the plane, so the backdrop comes in too, and from then on it is only each box's " +
                 "own depth span that keeps it out of the way. That holds exactly as long as the " +
                 "fighter stays in their lane, and a fighter does not: they shift a lane to walk " +
                 "up a ramp or onto a ladder, and the backdrop they were never meant to touch is " +
                 "suddenly in front of them. Dropping it at the bake is the one answer no later " +
                 "mistake can undo.")]
        public bool dropBackdrop = true;

        [Tooltip("How far behind the gameplay plane the backdrop starts, in metres.\n\n" +
                 "A box counts as level, not backdrop, when its NEAREST face is in front of this " +
                 "line — so anything the fight can see the front of is kept, including a slab " +
                 "that starts on the plane and runs back into the scenery. Leave room for " +
                 "geometry authored just behind the plane and still meant to be stood on.")]
        public float backdropBehind = 0.5f;

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

        [Header("Slopes")]
        [Tooltip("Step width when a tilted box is turned into a staircase, in metres.\n\n" +
                 "An AABB world has no slopes. A plank leaning against a platform bakes as its " +
                 "bounding box, which is a wall nobody can climb and a lip sticking out of the " +
                 "level — so instead it is cut into steps that follow its top edge. 0.3 m of " +
                 "run on a one-in-four slope is 0.08 m of rise, well under the free step, so a " +
                 "ramp is walked up rather than climbed. 0 bakes the bounding box.")]
        public float slopeStep = 0.2f;

        [Tooltip("How far a box's top edge has to rise across its own width before it counts " +
                 "as a slope rather than a box, in metres.")]
        public float slopeMinRise = 0.12f;

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
            List<Span> solidZ = new List<Span>();
            List<Span> oneWayZ = new List<Span>();
            List<Span> ladderZ = new List<Span>();
            List<Span> ladderLean = new List<Span>();
            List<ChestDef> chests = new List<ChestDef>();

            _gizmoSolid.Clear();
            _gizmoOneWay.Clear();
            _gizmoLadder.Clear();
            _gizmoChest.Clear();
            _chestSources.Clear();
            _ladderDepth.Clear();

            int roots = 0, slopes = 0, markedSeen = 0, markedSkipped = 0, markedIgnored = 0, markedOffPlane = 0,
                chestsSeen = 0, chestsSkipped = 0;
            List<string> rootNames = new List<string>();
            int collidersSeen = 0, skippedTrigger = 0, skippedPlayer = 0, skippedLayer = 0,
                skippedAlreadyMarked = 0, skippedInactive = 0, skippedChest = 0, skippedDepth = 0,
                backdrop = 0;

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

                    // Claimed before it is judged. A SimCollider says this object's collision
                    // is described HERE, so switching the marker off has to mean the object has
                    // no collision — not "fall back to whatever BoxCollider came with the art",
                    // which is what claiming it afterwards did: turning off the markers on the
                    // backdrop left every one of those boxes to be baked again by the ordinary
                    // collider pass, on its layer, as a solid wall.
                    handled.Add(c.gameObject.GetInstanceID());

                    // enabled && activeInHierarchy, NOT isActiveAndEnabled. The two are not
                    // the same here: isActiveAndEnabled is false for a component Unity has
                    // not activated yet, and this bake runs in Awake at execution order -100,
                    // which is to say before anything else in the scene has been activated.
                    // It read every marker in a hand-marked location as inactive and baked an
                    // arena of two boxes, and both fighters fell through the world.
                    if (!c.enabled || !c.gameObject.activeInHierarchy)
                    { markedSkipped++; continue; }
                    if (c.kind == SimColliderKind.Ignore) { markedIgnored++; continue; }

                    Bounds mb = c.WorldBounds;

                    // Backdrop goes nowhere, not even to the rescue list below: a location with
                    // no floor left is a band in the wrong place, and the backdrop is not the
                    // floor it is missing.
                    if (IsBackdrop(mb)) { backdrop++; continue; }

                    if (planeFiltersMarked && restrictToPlane && !ReachesPlane(mb))
                    {
                        markedOffPlane++;
                        if (c.kind == SimColliderKind.Solid)
                            culled.Add(SimCollider.RectToAabb(
                                new Rect(mb.min.x, mb.min.y, mb.size.x, mb.size.y)));
                        continue;
                    }

                    Span mz = new Span(ToFix(mb.min.z), ToFix(mb.max.z));
                    int before = solids.Count;
                    if (c.kind == SimColliderKind.Solid &&
                        TrySlope(c.transform, c.GetComponent<Collider>(), solids))
                        slopes++;
                    else
                        Add(c.kind, new Rect(mb.min.x, mb.min.y, mb.size.x, mb.size.y),
                            solids, oneWay, ladders, mb.center.z);
                    Fill(solidZ, solids.Count, mz);
                    Fill(oneWayZ, oneWay.Count, mz);
                    Fill(ladderZ, ladders.Count, mz);
                    FillLean(ladderLean, ladders.Count, c.transform,
                             c.GetComponent<Collider>());
                }

                // 2. Chests. Collected before the ordinary colliders so that a chest's own
                //    collider is never baked into a wall: you walk into a chest, not against it.
                //    The order they are found in IS the chest order, and the round's chest states
                //    are an array parallel to it, so it has to be the scene order on both devices.
                root.GetComponentsInChildren(true, _chestBuffer);
                foreach (Chest chest in _chestBuffer)
                {
                    chestsSeen++;
                    handled.Add(chest.gameObject.GetInstanceID());
                    if (!chest.enabled || !chest.gameObject.activeInHierarchy)
                    { chestsSkipped++; continue; }
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
                    if (IsBackdrop(cb)) { backdrop++; continue; }
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

                    Span cz = new Span(ToFix(cb.min.z), ToFix(cb.max.z));
                    if (kind == SimColliderKind.Solid && TrySlope(col.transform, col, solids))
                        slopes++;
                    else
                        Add(kind, new Rect(cb.min.x, cb.min.y, cb.size.x, cb.size.y),
                            solids, oneWay, ladders, cb.center.z);
                    Fill(solidZ, solids.Count, cz);
                    Fill(oneWayZ, oneWay.Count, cz);
                    Fill(ladderZ, ladders.Count, cz);
                }
            }

            // The one failure this must not pass on: a band so far from the level that it
            // leaves no floor at all. Take the band back rather than start a match nobody can
            // stand up in, and say so with both numbers so it can be put right.
            bool bandTookEverything = solids.Count == 0 && culled.Count > 0;
            if (bandTookEverything)
            {
                solids.AddRange(culled);
                Fill(solidZ, solids.Count, Span.Everywhere);
            }

            int hatches = ladderCutsHatch ? CutHatches(solids, ladders) : 0;

            // CutHatches rewrites the solid list, so the depths have to be rebuilt against
            // whatever came out of it rather than assumed to still line up.
            while (solidZ.Count < solids.Count) solidZ.Add(Span.Everywhere);
            if (solidZ.Count > solids.Count) solidZ.RemoveRange(solids.Count,
                                                                solidZ.Count - solids.Count);

            World = new SimWorld
            {
                Solids = solids.ToArray(),
                OneWay = oneWay.ToArray(),
                Ladders = ladders.ToArray(),
                Chests = chests.ToArray(),
                SolidZ = solidZ.ToArray(),
                OneWayZ = oneWayZ.ToArray(),
                LadderZ = ladderZ.ToArray(),
                LadderLean = ladderLean.ToArray()
            };
            ChestSources = _chestSources.ToArray();
            LadderDepths = _ladderDepth.ToArray();

            LastReport =
                $"{roots} roots [{string.Join(", ", rootNames)}]; {markedSeen} SimCollider " +
                $"({markedSkipped} inactive, {markedIgnored} ignored, " +
                $"{markedOffPlane} off the plane), {backdrop} backdrop, " +
                $"{collidersSeen} Unity collider (skipped: {skippedAlreadyMarked} already marked, " +
                $"{skippedTrigger} trigger, {skippedPlayer} on a player, {skippedChest} on a chest, " +
                $"{skippedDepth} off the plane, {skippedLayer} wrong layer, " +
                $"{skippedInactive} inactive) -> baked {World.Solids.Length} solid " +
                $"({hatches} cut by ladders), " +
                $"{World.OneWay.Length} one-way, {World.Ladders.Length} ladder, " +
                $"{slopes} slope(s) cut into steps, " +
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

        static Fix ToFix(float metres) => Fix.FromMilli(Mathf.RoundToInt(metres * 1000f));

        /// <summary>Gives every box added since the last call the depth it was modelled at.</summary>
        static void Fill(List<Span> depths, int upTo, Span z)
        {
            while (depths.Count < upTo) depths.Add(z);
        }

        static readonly Vector2[] _corners = new Vector2[8];

        /// <summary>
        /// Where a ladder's centre line sits at its foot and at its head. A ladder leaning
        /// against a wall has a bounding box wider than itself, and climbing the middle of
        /// that box is climbing the air beside the rungs.
        /// </summary>
        void FillLean(List<Span> lean, int upTo, Transform t, Collider col)
        {
            while (lean.Count < upTo)
            {
                Corners(t, col);
                int[] order = { 0, 1, 2, 3, 4, 5, 6, 7 };
                System.Array.Sort(order, (p, q) => _corners[p].y.CompareTo(_corners[q].y));

                float bottom = 0f, top = 0f;
                for (int i = 0; i < 4; i++) bottom += _corners[order[i]].x;
                for (int i = 4; i < 8; i++) top += _corners[order[i]].x;
                lean.Add(new Span(ToFix(bottom / 4f), ToFix(top / 4f)));
            }
        }

        /// <summary>The eight world corners of a box, as (x, y).</summary>
        static void Corners(Transform t, Collider col)
        {
            Vector3 centre = Vector3.zero, size = Vector3.one;
            if (col is BoxCollider box) { centre = box.center; size = box.size; }

            Vector3 e = size * 0.5f;
            int n = 0;
            for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
            for (int k = -1; k <= 1; k += 2)
            {
                Vector3 w = t.TransformPoint(centre + new Vector3(e.x * i, e.y * j, e.z * k));
                _corners[n++] = new Vector2(w.x, w.y);
            }
        }

        /// <summary>
        /// Cuts a tilted box into a staircase that follows its top edge, and says whether it
        /// did. An AABB world has no slopes; a plank leaning against a platform would otherwise
        /// bake as its bounding box, which is a wall nobody can climb.
        /// </summary>
        bool TrySlope(Transform t, Collider col, List<Aabb> solids)
        {
            if (slopeStep <= 0.01f) return false;

            if (col != null && !(col is BoxCollider)) return false;
            Corners(t, col);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                if (_corners[i].x < minX) minX = _corners[i].x;
                if (_corners[i].x > maxX) maxX = _corners[i].x;
                if (_corners[i].y < minY) minY = _corners[i].y;
            }
            if (maxX - minX < slopeStep) return false;

            float riseL = TopAt(minX), riseR = TopAt(maxX);
            if (Mathf.Abs(riseL - riseR) < slopeMinRise) return false;   // flat enough to be a box

            int steps = Mathf.Clamp(Mathf.CeilToInt((maxX - minX) / slopeStep), 1, 64);
            float run = (maxX - minX) / steps;
            for (int i = 0; i < steps; i++)
            {
                float a = minX + run * i, b = a + run;

                // The surface at the MIDDLE of the step, not the higher of its two ends.
                // Taking the high end puts every step above the board it is standing in for,
                // and a fighter walking down the ramp walks down it through the air. The
                // middle splits the error either way, and it is half a step's rise — four
                // centimetres on this location's planks.
                float top = TopAt((a + b) * 0.5f);
                if (top - minY <= 0.001f) continue;

                Rect r = new Rect(a, minY, b - a, top - minY);
                solids.Add(SimCollider.RectToAabb(r));
                _gizmoSolid.Add(r);
            }
            return true;
        }

        /// <summary>The highest the silhouette reaches at this x. Convex, so a scan is exact.</summary>
        static float TopAt(float x)
        {
            float top = float.MinValue;
            for (int i = 0; i < 8; i++)
            for (int j = i + 1; j < 8; j++)
            {
                Vector2 p = _corners[i], q = _corners[j];
                if (p.x == q.x) continue;
                if (x < Mathf.Min(p.x, q.x) || x > Mathf.Max(p.x, q.x)) continue;
                float y = p.y + (q.y - p.y) * (x - p.x) / (q.x - p.x);
                if (y > top) top = y;
            }
            return top;
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
        /// <summary>
        /// Is this box behind the fight rather than part of it. Measured on the box's NEAREST
        /// face, so a slab that starts on the plane and runs back into the scenery is level and
        /// only something standing entirely behind the plane is backdrop.
        /// </summary>
        bool IsBackdrop(Bounds b) => dropBackdrop && b.min.z >= planeZ + backdropBehind;

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
