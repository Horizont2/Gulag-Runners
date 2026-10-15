using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Every item file in one list, applied to the simulation's table once at load.
    ///
    /// One asset rather than a scene reference per item, so that the set travels with the
    /// project instead of with one scene, and so that two arenas cannot quietly balance
    /// differently.
    /// </summary>
    [CreateAssetMenu(menuName = "Gulag Runners/Item Library", fileName = "ItemLibrary")]
    public sealed class ItemLibrary : ScriptableObject
    {
        [Tooltip("The item files. Order does not matter; each one says which item it is for.")]
        public ItemDefAsset[] items = System.Array.Empty<ItemDefAsset>();

        /// <summary>
        /// Pushes every file into <see cref="ItemTable"/>. Called once, before the first tick.
        ///
        /// Resets first, so removing a file from the list puts that item back to the code
        /// table rather than leaving the last values it was given lying in a static array
        /// across a domain reload.
        /// </summary>
        public int Apply()
        {
            ItemTable.Reset();
            if (items == null) return 0;

            int applied = 0;
            for (int i = 0; i < items.Length; i++)
            {
                ItemDefAsset a = items[i];
                if (a == null || a.item == ItemId.None) continue;
                ItemTable.Apply(a.ToDef());
                applied++;
            }
            return applied;
        }
    }
}
