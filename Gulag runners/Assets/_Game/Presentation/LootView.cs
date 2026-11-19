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
                 "Capacity on MatchState.\n\n" +
                 "Optional: left empty it is filled from the children at load, in hierarchy " +
                 "order, and a scene with no views in it is a scene with no loot drawn — the " +
                 "movement sandbox of docs/11, say, where the pool still works and nothing " +
                 "needs to be seen.")]
        public GroundItemView[] views;

        [Tooltip("Take the plane from the fighters rather than from the number below.\n\n" +
                 "On, and it cannot go stale. This was a hand-typed zero while the fighters " +
                 "stood at -2.51, so every item a chest coughed up was drawn two and a half " +
                 "metres behind the person who could pick it up — and picking it up still " +
                 "worked, because the simulation is flat, which is exactly the kind of wrong " +
                 "that survives a playtest.")]
        public bool planeFromFighters = true;

        [Tooltip("Z of the gameplay plane the loot sits on, when it is not taken from them.")]
        public float planeZ;

        [Tooltip("Lay each item on the slice of the STOREY it fell on, rather than all of " +
                 "them on the gameplay plane.\n\n" +
                 "A location built in 3D does not have one floor at one depth, and the " +
                 "fighter already knows that -- the lane rule stands him on the part of the " +
                 "floor nearest the fight, which on the mine's top storey is 1.40 m behind " +
                 "the plane. The loot did not know it: every item was drawn ON the plane, so " +
                 "a chest two storeys up paid out onto the floor and the item appeared a " +
                 "metre and a half in front of it, hanging over the drop towards the camera. " +
                 "Picking it up still worked, because the simulation is flat.\n\n" +
                 "Off, everything lies on the plane as it used to.")]
        public bool restOnTheirStorey = true;

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

            // Sized from here rather than from each view's own Start, because the rule is
            // the simulation's -- a weapon on the floor is the same length as the one in a
            // hand, and both are ArmMotor.BladeLength. A view cannot reach the match's arm
            // tuning on its own; this already holds it. Each view does it once.
            FitViews();

            if (planeFromFighters)
                for (int p = 0; p < matchState.fighters.Count; p++)
                    if (matchState.fighters[p] != null)
                    {
                        planeZ = matchState.fighters[p].planeZ;
                        break;
                    }

            if (items.Length > views.Length && !_warned)
            {
                _warned = true;
                Debug.LogWarning($"{name}: the loot pool holds {items.Length} items but there are " +
                                 $"only {views.Length} views, so loot in the slots past {views.Length} " +
                                 "will be invisible and still pickable. Add views, or lower " +
                                 "Ground Item Capacity on MatchState.", this);
            }

            // Which item, if any, the action button would swap for. docs/03 wants a
            // contextual button that says what it would do BEFORE it is pressed, and there is
            // no interface to say it in — so the item says it, by lifting out of the bob.
            // Walking over something with a free slot just takes it, so the only case worth
            // marking is the one that costs you what you are holding.
            for (int p = 0; p < matchState.fighters.Count; p++)
            {
                PlayerController fighter = matchState.fighters[p];
                if (fighter == null) continue;
                int over = fighter.State.StandingOn;
                if (over >= 0 && over < views.Length && views[over] != null)
                    views[over].Offer(true);
            }

            for (int i = 0; i < views.Length; i++)
            {
                GroundItemView view = views[i];
                if (view == null) continue;

                if (i < items.Length && items[i].Live)
                    view.Show(in items[i], StoreyZ(in items[i]), i);
                else if (view.Visible) view.PlayTaken();      // it was there a frame ago
                else view.Hide();
            }
        }

        /// <summary>
        /// The depth to lay one item at: the slice of the floor it is lying ON.
        ///
        /// The same rule the fighter is held by, deliberately -- the part of that floor
        /// nearest the fight, kept a body's half-depth off its lip. It has to be the same
        /// rule and not merely a similar one, because the point of it is that the item
        /// ends up exactly where the fighter who comes to take it will be standing.
        ///
        /// Falls back to the plane when there is nothing under the item, which is every
        /// frame of the arc it flies out of the chest on: a thing in the air has no
        /// storey, and the plane is the right guess until it lands.
        /// </summary>
        float StoreyZ(in GroundItem item)
        {
            if (!restOnTheirStorey || matchState == null) return planeZ;

            SimWorldBaker baker = matchState.worldBaker;
            SimWorld world = baker != null ? baker.World : null;
            if (world == null || world.Solids.Length == 0) return planeZ;

            Fix home = Fix.FromMilli(Mathf.RoundToInt(planeZ * 1000f));
            Fix half = matchState.fighters.Count > 0 && matchState.fighters[0] != null
                ? matchState.fighters[0].Config.BodyDepth / 2
                : Fix.FromMilli(300);

            Fix x = item.Position.X, y = item.Position.Y;
            bool found = false;
            Fix top = Fix.Zero, lane = home;
            for (int i = 0; i < world.Solids.Length; i++)
            {
                if (!world.SolidLive(i)) continue;
                Aabb solid = world.Solids[i];
                if (solid.MinX > x || solid.MaxX < x) continue;
                if (solid.MaxY > y + Fix.FromMilli(60)) continue;     // above it, not under
                if (found && solid.MaxY <= top) continue;

                top = solid.MaxY;
                lane = world.SolidSpan(i).Nearest(home, half);
                found = true;
            }
            return found ? lane.Raw / (float)Fix.RawOne : planeZ;
        }

        /// <summary>
        /// Hands every view the arm tuning this match is running, once each.
        ///
        /// Off a fighter when there is one, because that is where the body height a blade
        /// is a share of comes from. With no fighter in the scene the defaults are the
        /// right answer and no longer a wrong one: MoveConfig's used to describe a 1.8 m
        /// man nobody plays.
        /// </summary>
        void FitViews()
        {
            if (_fitted) return;
            _fitted = true;

            PlayerController fighter = null;
            for (int p = 0; p < matchState.fighters.Count && fighter == null; p++)
                fighter = matchState.fighters[p];

            MoveConfig move = fighter != null ? fighter.Config : MoveConfig.Default();
            ArmConfig arm = fighter != null ? fighter.ArmSettings : matchState.Arm;

            for (int i = 0; i < views.Length; i++)
                if (views[i] != null) views[i].FitModels(in move, in arm);
        }

        bool _fitted;
    }
}
