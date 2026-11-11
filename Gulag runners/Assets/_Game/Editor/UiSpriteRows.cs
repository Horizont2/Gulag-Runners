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
        public static UiSpriteSet Field(UiSpriteSet set, string suggestedName,
                                        IList<string> keys = null)
        {
            // FOUND, NOT ASKED FOR. A window that opens on an empty object field for an
            // asset type nobody has ever made is a window with nothing to do: the one
            // report this change exists for was "it still wants a folder", from somebody
            // looking at exactly that -- one field they could not fill and no way in.
            if (set == null) set = Existing(suggestedName);

            EditorGUILayout.LabelField("Sprites", EditorStyles.boldLabel);

            if (set == null)
            {
                EditorGUILayout.LabelField(
                    "This screen is built from a list of named rows -- one per piece -- " +
                    "that you drag the assets into. There is no list in the project yet.",
                    EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button($"Make the list ({suggestedName})", GUILayout.Height(30)))
                    set = Create(suggestedName, keys);

                EditorGUILayout.Space();
                set = (UiSpriteSet)EditorGUILayout.ObjectField(
                    new GUIContent("or use this one", "An existing list, if you keep more " +
                                                      "than one."),
                    set, typeof(UiSpriteSet), false);
                return set;
            }

            EditorGUILayout.LabelField(
                "One row per piece below. Drag the asset into the row and press Build — " +
                "no folder to scan and no filenames to get right.",
                EditorStyles.wordWrappedMiniLabel);

            set = (UiSpriteSet)EditorGUILayout.ObjectField(
                "Sprite list", set, typeof(UiSpriteSet), false);

            // And the rows this screen needs, there before the first click. Sync writes
            // only when something actually changed, so this is free per repaint.
            if (set != null && keys != null && set.Missing(keys) > 0) set.Sync(keys);

            return set;
        }

        /// <summary>
        /// A list already in the project: the one named after this screen if there is one,
        /// otherwise any of them -- several builders are meant to be able to share one.
        /// </summary>
        static UiSpriteSet Existing(string suggestedName)
        {
            string[] guids = AssetDatabase.FindAssets("t:UiSpriteSet");
            UiSpriteSet any = null;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UiSpriteSet found = AssetDatabase.LoadAssetAtPath<UiSpriteSet>(path);
                if (found == null) continue;
                if (found.name == suggestedName) return found;
                any = any ?? found;
            }

            return any;
        }

        /// <summary>
        /// Makes the list where the rest of the interface lives, with this screen's rows
        /// already in it. No file dialog: the question "where should this go" has one
        /// right answer and asking it is the step people give up on.
        /// </summary>
        static UiSpriteSet Create(string suggestedName, IList<string> keys)
        {
            const string home = "Assets/_Game/UI";
            if (!AssetDatabase.IsValidFolder(home))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Game"))
                    AssetDatabase.CreateFolder("Assets", "_Game");
                AssetDatabase.CreateFolder("Assets/_Game", "UI");
            }

            string path = AssetDatabase.GenerateUniqueAssetPath($"{home}/{suggestedName}.asset");
            UiSpriteSet made = ScriptableObject.CreateInstance<UiSpriteSet>();
            if (keys != null) made.Sync(keys);
            AssetDatabase.CreateAsset(made, path);
            AssetDatabase.SaveAssets();

            Debug.Log($"Made {path}. It is the wiring for this screen -- keep it in the " +
                      "project and commit it, and it only has to be filled in once.", made);
            return made;
        }

        /// <summary>
        /// The folder, folded away. It is a convenience for the first import of a fresh
        /// export and nothing else, and it sat in the middle of the window looking like
        /// the thing the tool wanted -- which is how "it still asks for a folder" happens
        /// to somebody who has read none of the tooltips.
        /// </summary>
        public static void FolderFill(UiSpriteSet set, IList<string> keys,
                                      ref bool open, ref Object folder)
        {
            if (set == null) return;

            open = EditorGUILayout.Foldout(
                open, "Optional: guess the empty rows from a folder", true);
            if (!open) return;

            EditorGUILayout.LabelField(
                "For the first import of a fresh export, when twenty-eight drags are a " +
                "chore. It fills only EMPTY rows, by name, and never touches one you have " +
                "dragged into — a bad guess leaves the row empty rather than swapping a " +
                "sprite behind your back.", EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                folder = EditorGUILayout.ObjectField("Folder", folder, typeof(DefaultAsset),
                                                     false);
                using (new EditorGUI.DisabledScope(folder == null))
                    if (GUILayout.Button("Fill", GUILayout.Width(60)))
                    {
                        int filled = FillEmptyFromFolder(set, folder, keys);
                        Debug.Log(filled == 0
                                      ? "Nothing in that folder answered to an empty row."
                                      : $"Filled {filled} empty row(s). Check them: a guess " +
                                        "is a guess.", set);
                    }
            }
        }

        /// <summary>
        /// The rows themselves. `keys` is the builder's own table, `sizes` the design size
        /// to show beside each one, and `notes` what the piece IS -- in English, under the
        /// field, for whoever is filling the tool in.
        ///
        /// The note is not decoration. "hud/bar/fill" is obvious to the person who wrote
        /// the builder and a guess to everybody else: is it the health bar or the stamina
        /// bar, does it include the frame, is it drawn stretched or nine-sliced, and which
        /// end does it empty from. Somebody dragging twenty-eight sprites in should not
        /// have to open the source to find out, and a wrong guess here is a screen that
        /// builds without complaint and looks wrong.
        /// </summary>
        public static void Rows(UiSpriteSet set, IList<string> keys, IList<Vector2> sizes,
                                IList<string> notes, ref Vector2 scroll, float height = 360f)
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
                string note = notes != null && i < notes.Count ? notes[i] : null;

                Sprite now = (Sprite)EditorGUILayout.ObjectField(
                    new GUIContent(hint, string.IsNullOrEmpty(note) ? key : $"{key}\n\n{note}"),
                    was, typeof(Sprite), false);

                if (!string.IsNullOrEmpty(note))
                {
                    // Under the field rather than only in the tooltip: a tooltip is found
                    // by somebody who already suspects they need it.
                    Rect line = EditorGUILayout.GetControlRect(
                        false, EditorStyles.wordWrappedMiniLabel.CalcHeight(
                            new GUIContent(note), EditorGUIUtility.currentViewWidth - 40f));
                    line.xMin += EditorGUIUtility.labelWidth;
                    GUI.Label(line, note, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(2);
                }

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
