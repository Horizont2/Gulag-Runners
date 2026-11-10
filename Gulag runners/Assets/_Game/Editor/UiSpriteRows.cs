using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// The drag-the-asset-in list, drawn the same way by every UI builder.
    ///
    /// One row per piece: the name the builder calls it, an object field, and the design
    /// size as a hint so a 440x32 bar frame is not dragged into the 20x20 pip. Grouped by
    /// the middle of the key ("hud/button/..." becomes a "button" heading), because
    /// twenty-eight flat rows is a wall and five groups of five is a list.
    ///
    /// It draws straight into the ASSET rather than into window fields, so a drag is saved
    /// the moment it happens and survives the window closing, a recompile, and the next
    /// person opening it.
    /// </summary>
    public static class UiSpriteRows
    {
        /// <summary>
        /// The set field, with a button to make one when there is none.
        ///
        /// Offering to create it matters more than it looks: the first thing anybody does
        /// with a tool that needs an asset they have never heard of is close the tool.
        /// </summary>
        public static UiSpriteSet Field(UiSpriteSet set, string suggestedName)
        {
            EditorGUILayout.LabelField("Sprites", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "One row per piece below. Drag the asset into the row and press Build — " +
                "no folder to scan and no filenames to get right.",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                set = (UiSpriteSet)EditorGUILayout.ObjectField(
                    "Sprite set", set, typeof(UiSpriteSet), false);

                using (new EditorGUI.DisabledScope(set != null))
                    if (GUILayout.Button("New…", GUILayout.Width(60))) set = Create(suggestedName);
            }

            return set;
        }

        static UiSpriteSet Create(string suggestedName)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Where to keep the sprite list", suggestedName, "asset",
                "This file remembers which asset goes in which row. Keep it in the " +
                "project and commit it: it is the wiring, and it is wired once.");
            if (string.IsNullOrEmpty(path)) return null;

            UiSpriteSet made = ScriptableObject.CreateInstance<UiSpriteSet>();
            AssetDatabase.CreateAsset(made, path);
            AssetDatabase.SaveAssets();
            return made;
        }

        /// <summary>
        /// The rows themselves. `needed` is the builder's own table: key, and the design
        /// size to show beside it.
        /// </summary>
        public static void Rows(UiSpriteSet set, IList<string> keys, IList<Vector2> sizes,
                                ref Vector2 scroll, float height = 300f)
        {
            if (set == null) return;

            int filled = set.Filled(keys);
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{filled} of {keys.Count} filled in",
                                           EditorStyles.miniBoldLabel);
                if (GUILayout.Button("List what this screen needs", GUILayout.Width(190)))
                {
                    int added = set.Sync(keys);
                    Debug.Log(added == 0
                                  ? $"{set.name}: every row this screen needs is already there."
                                  : $"{set.name}: added {added} row(s).", set);
                }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(height));

            string group = null;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                string head = Group(key);
                if (head != group)
                {
                    group = head;
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(head, EditorStyles.miniBoldLabel);
                }

                Sprite was = set.Get(key);
                Vector2 size = i < sizes.Count ? sizes[i] : Vector2.zero;
                string label = Leaf(key);
                string hint = size.x > 0f && size.y > 0f
                    ? $"{label}   ({size.x:0}x{size.y:0})" : label;

                Sprite now = (Sprite)EditorGUILayout.ObjectField(
                    new GUIContent(hint, key), was, typeof(Sprite), false);

                if (now != was) set.Put(key, now);
            }

            // Anything in the set this screen has no row for. Shown rather than hidden: a
            // key renamed in the builder leaves its sprite here, and this is the only
            // place that says so.
            bool said = false;
            for (int i = 0; i < set.slots.Count; i++)
            {
                UiSpriteSet.Slot slot = set.slots[i];
                if (slot == null || string.IsNullOrEmpty(slot.key) || keys.Contains(slot.key))
                    continue;
                if (!said)
                {
                    said = true;
                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField("Rows this screen does not use",
                                               EditorStyles.miniBoldLabel);
                }
                EditorGUILayout.LabelField("   " + slot.key,
                                           slot.sprite != null ? slot.sprite.name : "empty",
                                           EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Fills the EMPTY rows from a folder, by name, and never touches a row somebody
        /// has already dragged into.
        ///
        /// The convenience the scanning used to be, demoted to what it is good for: the
        /// first import of a fresh export, where twenty-eight drags are a chore and a
        /// wrong guess costs one correction. It is not how the screen is built -- the rows
        /// are -- so a bad filename here is a row left empty rather than a silent swap.
        /// </summary>
        public static int FillEmptyFromFolder(UiSpriteSet set, Object folder, IList<string> keys)
        {
            if (set == null || folder == null) return 0;

            string root = AssetDatabase.GetAssetPath(folder);
            if (string.IsNullOrEmpty(root) || !AssetDatabase.IsValidFolder(root)) return 0;

            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { root });
            List<string> paths = new List<string>(guids.Length);
            foreach (string guid in guids) paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            int filled = 0;
            foreach (string key in keys)
            {
                if (set.Has(key)) continue;

                string best = null;
                int bestScore = 0;
                foreach (string path in paths)
                {
                    int score = Score(key, Path.GetFileNameWithoutExtension(path));
                    if (score > bestScore) { bestScore = score; best = path; }
                }

                if (best == null || bestScore < 2) continue;
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(best);
                if (sprite == null) continue;

                set.Put(key, sprite);
                filled++;
            }

            if (filled > 0) AssetDatabase.SaveAssets();
            return filled;
        }

        /// <summary>
        /// How well a filename answers to a key: how many of the key's own words it holds.
        /// Two is the floor, so "frame" alone never wins a row that wanted "bar/frame".
        /// </summary>
        static int Score(string key, string file)
        {
            string flat = file.ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            string[] words = key.ToLowerInvariant().Split('/');
            int score = 0;
            foreach (string word in words)
                if (flat.Contains(word)) score++;

            // The leaf is what the row is really called, so a file that has it counts
            // double -- "hud/bar/fill" against "bar-fill" beats it against "bar-frame".
            if (flat.Contains(words[words.Length - 1])) score++;
            return score;
        }

        static string Group(string key)
        {
            string[] parts = key.Split('/');
            return parts.Length >= 3 ? parts[1] : parts[0];
        }

        static string Leaf(string key)
        {
            string[] parts = key.Split('/');
            return parts.Length >= 3 ? parts[parts.Length - 1] : key.Replace("/", " / ");
        }
    }
}
