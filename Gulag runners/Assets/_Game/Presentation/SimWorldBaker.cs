using System.Collections.Generic;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Turns the colliders in the scene into one immutable <see cref="SimWorld"/>.
    ///
    /// The simulation does its own collision and never uses Unity physics, because PhysX is
    /// not deterministic across devices and rollback needs bit-identical results (docs/06).
    /// So Unity colliders here are only an authoring surface: their world-space bounds are
    /// read once and copied into the simulation.
    ///
    /// Any ordinary BoxCollider works — you do not need a special component. Add a
    /// <see cref="SimCollider"/> only to mark something as a one-way platform or a ladder,
    /// or use the layer masks below.
    ///
    /// Later this is replaced by the seeded generator of docs/05, which will build the same
    /// SimWorld from a 64-bit seed instead of from a scene.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class SimWorldBaker : MonoBehaviour
    {
        public static SimWorldBaker Instance { get; private set; }

        [Header("What to bake")]
        [Tooltip("Bake every ordinary Unity collider in the scene, not just objects carrying a " +
                 "SimCollider. Leave this on unless you want to hand-pick every box.")]
        public bool bakeUnityColliders = true;

        [Tooltip("Colliders on these layers become solid walls and floors.")]
        public LayerMask solidLayers = ~0;

        [Tooltip("Colliders on these layers become one-way platforms. A SimCollider on the " +
                 "object overrides this.")]
        public LayerMask oneWayLayers = 0;

        [Tooltip("Colliders on these layers become ladders. A SimCollider on the object " +
                 "overrides this.")]
        public LayerMask ladderLayers = 0;

        [Tooltip("Trigger colliders are skipped by default: they are usually zones, not geometry.")]
        public bool includeTriggers;

        [Header("Options")]
        [Tooltip("Rebake every frame. Editor convenience while dragging platforms about; " +
                 "turn it off in a build.")]
        public bool rebakeEveryFrame;

        [Tooltip("Draw the baked boxes in the scene view, so what the simulation sees is " +
                 "visible rather than assumed.")]
        public bool drawBakedBoxes = true;

        public SimWorld World { get; private set; }
        public int SolidCount => World?.Solids.Length ?? 0;
        public int OneWayCount => World?.OneWay.Length ?? 0;
        public int LadderCount => World?.Ladders.Length ?? 0;

        readonly List<Rect> _gizmoSolid = new List<Rect>();
        readonly List<Rect> _gizmoOneWay = new List<Rect>();
        readonly List<Rect> _gizmoLadder = new List<Rect>();

        void Awake()
        {
            Instance = this;
            Bake();
        }

        void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        void LateUpdate()
        {
            if (rebakeEveryFrame) Bake();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public SimWorld Bake()
        {
            List<Aabb> solids = new List<Aabb>();
            List<Aabb> oneWay = new List<Aabb>();
            List<Aabb> ladders = new List<Aabb>();

            _gizmoSolid.Clear();
            _gizmoOneWay.Clear();
            _gizmoLadder.Clear();

            // 1. Objects explicitly marked with a SimCollider always win.
            SimCollider[] marked = FindObjectsByType<SimCollider>(FindObjectsSortMode.None);
            System.Array.Sort(marked, CompareById);

            HashSet<int> handled = new HashSet<int>();
            foreach (SimCollider c in marked)
            {
                if (!c.isActiveAndEnabled) continue;
                handled.Add(c.gameObject.GetInstanceID());
                Add(c.kind, c.ToRect(), solids, oneWay, ladders);
            }

            // 2. Then every ordinary collider that was not already handled.
            if (bakeUnityColliders)
            {
                Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
                System.Array.Sort(colliders, CompareById);

                foreach (Collider col in colliders)
                {
                    if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
                    if (col.isTrigger && !includeTriggers) continue;
                    if (handled.Contains(col.gameObject.GetInstanceID())) continue;

                    // Never bake a player's own body: it would become a wall it stands inside.
                    if (col.GetComponentInParent<PlayerController>() != null) continue;

                    int layerBit = 1 << col.gameObject.layer;
                    SimColliderKind kind;
                    if ((ladderLayers.value & layerBit) != 0) kind = SimColliderKind.Ladder;
                    else if ((oneWayLayers.value & layerBit) != 0) kind = SimColliderKind.OneWay;
                    else if ((solidLayers.value & layerBit) != 0) kind = SimColliderKind.Solid;
                    else continue;

                    Bounds b = col.bounds;
                    Add(kind, new Rect(b.min.x, b.min.y, b.size.x, b.size.y),
                        solids, oneWay, ladders);
                }
            }

            World = new SimWorld
            {
                Solids = solids.ToArray(),
                OneWay = oneWay.ToArray(),
                Ladders = ladders.ToArray()
            };

            if (Application.isPlaying && World.Solids.Length == 0)
                Debug.LogError(
                    $"{name}: baked 0 solid boxes, so there is no floor and the player will fall " +
                    "forever. Check that your platforms have a collider, are on a layer included " +
                    "in Solid Layers, and are not triggers.", this);

            return World;
        }

        static int CompareById(Object a, Object b) =>
            a.GetInstanceID().CompareTo(b.GetInstanceID());

        void Add(SimColliderKind kind, Rect r,
                 List<Aabb> solids, List<Aabb> oneWay, List<Aabb> ladders)
        {
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
