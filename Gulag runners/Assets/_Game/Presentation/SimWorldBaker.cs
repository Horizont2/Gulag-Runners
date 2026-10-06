using System.Collections.Generic;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Collects every <see cref="SimCollider"/> in the scene into one immutable
    /// <see cref="SimWorld"/>. Put one of these in the scene; the player finds it.
    ///
    /// Later this is replaced by the seeded generator of docs/05 — the arena will be built
    /// from a 64-bit seed on both clients instead of read from a scene. The SimWorld this
    /// produces is deliberately the same shape, so that swap touches nothing else.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimWorldBaker : MonoBehaviour
    {
        public static SimWorldBaker Instance { get; private set; }

        [Tooltip("Rebake every frame. Editor convenience while moving platforms around; " +
                 "leave it off in a build.")]
        public bool rebakeEveryFrame;

        public SimWorld World { get; private set; }

        void Awake()
        {
            Instance = this;
            Bake();
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

#if UNITY_2023_1_OR_NEWER
            SimCollider[] all = Object.FindObjectsByType<SimCollider>(FindObjectsSortMode.None);
#else
            SimCollider[] all = Object.FindObjectsOfType<SimCollider>();
#endif
            // Sorted by instance id so the baked order is identical on both clients.
            System.Array.Sort(all, (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

            foreach (SimCollider c in all)
            {
                if (!c.isActiveAndEnabled) continue;
                switch (c.kind)
                {
                    case SimColliderKind.Solid: solids.Add(c.ToAabb()); break;
                    case SimColliderKind.OneWay: oneWay.Add(c.ToAabb()); break;
                    case SimColliderKind.Ladder: ladders.Add(c.ToAabb()); break;
                }
            }

            World = new SimWorld
            {
                Solids = solids.ToArray(),
                OneWay = oneWay.ToArray(),
                Ladders = ladders.ToArray()
            };
            return World;
        }
    }
}
