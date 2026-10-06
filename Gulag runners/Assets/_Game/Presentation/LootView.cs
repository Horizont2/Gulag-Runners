using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Binds the loot pool in <see cref="MatchState"/> to the views in the scene.
    ///
    /// One view per pool slot, fixed for the whole match. An item keeps the same object from the
    /// moment it leaves a chest to the moment somebody takes it, which is the only way the arc
    /// reads as one thing moving rather than a model appearing somewhere new each frame.
    ///
    /// Nothing is created here. If the pool is larger than the number of views, the extra slots
    /// are invisible but still pickable, and this says so once rather than letting a player walk
    /// through loot they cannot see.
    /// </summary>
    [DefaultExecutionOrder(120)]      // after the players have ticked
    [DisallowMultipleComponent]
    public sealed class LootView : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("One per slot of the loot pool, in order. Match this count to Ground Item " +
                 "Capacity on MatchState.")]
        public GroundItemView[] views;

        [Tooltip("Z of the gameplay plane the loot sits on.")]
        public float planeZ;

        bool _warned;

        void Awake()
        {
            if (matchState == null) matchState = MatchState.Instance;
            CollectViews(false);
        }

        /// <summary>
        /// Fills the list from the children, in hierarchy order, when it has not been filled by
        /// hand. The views are prefab instances, and a scene cannot hold a direct reference into
        /// one without a stripped component entry — so this resolves it the same way every other
        /// component here resolves its wiring, and the inspector button writes it down properly.
        /// </summary>
        public int CollectViews(bool force)
        {
            if (!force && views != null && views.Length > 0) return views.Length;
            views = GetComponentsInChildren<GroundItemView>(true);
            return views.Length;
        }

        void LateUpdate()
        {
            if (matchState == null) matchState = MatchState.Instance;
            if (matchState == null || views == null) return;

            GroundItem[] items = matchState.GroundItems;

            if (items.Length > views.Length && !_warned)
            {
                _warned = true;
                Debug.LogWarning($"{name}: the loot pool holds {items.Length} items but there are " +
                                 $"only {views.Length} views, so loot in the slots past {views.Length} " +
                                 "will be invisible and still pickable. Add views, or lower " +
                                 "Ground Item Capacity on MatchState.", this);
            }

            for (int i = 0; i < views.Length; i++)
            {
                GroundItemView view = views[i];
                if (view == null) continue;

                if (i < items.Length && items[i].Live) view.Show(in items[i], planeZ, i);
                else if (view.Visible) view.PlayTaken();      // it was there a frame ago
                else view.Hide();
            }
        }
    }
}
