using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// The sprites a UI builder is made of, named and dragged in by hand.
    ///
    /// WHY THIS EXISTS. The builders used to take a FOLDER and match the files inside it to
    /// the keys they needed, by name. That works exactly once -- on the export the matching
    /// was written against. Every time afterwards it is a guessing game: Figma renames a
    /// node and the sprite silently goes missing, an export comes out one folder deeper,
    /// @2x and @4x of the same shape both turn up, somebody saves a file as "bar fill 2"
    /// and the HUD builds with an empty bar and no complaint. The fault is never in the
    /// art; it is in the filename, which is the one thing about an asset nobody should have
    /// to get right.
    ///
    /// So the list is explicit. One row per thing the screen needs, each with the name the
    /// builder calls it, and you drag the asset into the row. No scanning, no names to
    /// match, and what you see in the window is what gets built.
    ///
    /// It is an ASSET rather than fields on the window because a window forgets: close it,
    /// recompile, or open another and twenty-eight drags are gone. An asset is wired once,
    /// lives in the project, and can be committed -- so the next person to press Build gets
    /// the same screen rather than a fresh archaeology problem.
    ///
    /// Several builders may share one set: the keys are prefixed by screen ("hud/...",
    /// "menu/...", "search/..."), so nothing collides and one file can hold the whole
    /// interface. Separate sets per screen work just as well; the builders do not care.
    ///
    /// Editor-only, like the builders it serves. Nothing at run time reads a sprite through
    /// this -- the builders write ordinary Images into the scene and the asset is done.
    /// </summary>
    [CreateAssetMenu(menuName = "Gulag Runners/UI sprite set", fileName = "UI Sprites",
                     order = 120)]
    public sealed class UiSpriteSet : ScriptableObject
    {
        /// <summary>One named place for one sprite.</summary>
        [System.Serializable]
        public sealed class Slot
        {
            [Tooltip("What the builder calls this piece. Do not edit it by hand: the " +
                     "builder fills these in, and a key it does not know is a row nothing " +
                     "reads.")]
            public string key = "";

            [Tooltip("The asset. Drag it here from the Project window.")]
            public Sprite sprite;
        }

        [Tooltip("One row per piece the screens are built from. Press \"List what this " +
                 "screen needs\" in a builder window to add its rows, then drag the " +
                 "sprites in.")]
        public List<Slot> slots = new List<Slot>();

        public Sprite Get(string key)
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && slots[i].key == key) return slots[i].sprite;
            return null;
        }

        public bool Has(string key) => Get(key) != null;

        /// <summary>
        /// Puts a sprite in a row, adding the row if it is not there. Used by the builders
        /// for the pieces they DRAW themselves, so a generated sprite appears in the list
        /// beside the dragged ones rather than being invisible magic.
        /// </summary>
        public void Put(string key, Sprite sprite)
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && slots[i].key == key)
                {
                    slots[i].sprite = sprite;
                    EditorUtility.SetDirty(this);
                    return;
                }

            slots.Add(new Slot { key = key, sprite = sprite });
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Makes sure there is a row for every key, in the order given, and returns how
        /// many were added. Rows for keys that are not in the list are kept and sink to the
        /// bottom: a sprite dragged in before a key was renamed is still a sprite somebody
        /// chose, and deleting it silently is how a tool loses an afternoon's work.
        /// </summary>
        /// <summary>
        /// How many of these keys have no row yet. Asked every frame by the builder
        /// windows, so it allocates nothing and writes nothing.
        /// </summary>
        public int Missing(IList<string> keys)
        {
            int n = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < slots.Count && !found; j++)
                    if (slots[j] != null && slots[j].key == keys[i]) found = true;
                if (!found) n++;
            }
            return n;
        }

        public int Sync(IList<string> keys)
        {
            List<Slot> ordered = new List<Slot>(keys.Count);
            int added = 0;

            for (int i = 0; i < keys.Count; i++)
            {
                Slot found = null;
                for (int j = 0; j < slots.Count; j++)
                    if (slots[j] != null && slots[j].key == keys[i]) { found = slots[j]; break; }

                if (found == null) { found = new Slot { key = keys[i] }; added++; }
                ordered.Add(found);
            }

            for (int j = 0; j < slots.Count; j++)
                if (slots[j] != null && !ordered.Contains(slots[j]) &&
                    !string.IsNullOrEmpty(slots[j].key))
                    ordered.Add(slots[j]);

            // Written only when it actually changed, because a builder window asks for
            // this every repaint and an asset marked dirty sixty times a second is an
            // asset Unity saves sixty times a second.
            bool same = ordered.Count == slots.Count;
            for (int i = 0; same && i < ordered.Count; i++)
                same = ReferenceEquals(ordered[i], slots[i]);
            if (same) return 0;

            slots = ordered;
            EditorUtility.SetDirty(this);
            return added;
        }

        /// <summary>How many of these keys have something in them.</summary>
        public int Filled(IList<string> keys)
        {
            int n = 0;
            for (int i = 0; i < keys.Count; i++) if (Has(keys[i])) n++;
            return n;
        }
    }
}
