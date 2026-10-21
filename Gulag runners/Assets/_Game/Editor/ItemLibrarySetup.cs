using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Writes one data file per item, filled in from the table in ItemTable.cs, and a library
    /// listing them.
    ///
    /// Generated rather than hand-written so the files START identical to the code table: a
    /// set of assets typed out by hand is a second balance pass nobody asked for, and the
    /// first time the two disagree it is not obvious which one the game is using.
    ///
    /// Re-running it refreshes the list and creates whatever is missing. It does NOT overwrite
    /// a file that already exists — that would throw away the tuning the files are for.
    /// </summary>
    public static class ItemLibrarySetup
    {
        const string Folder = "Assets/_Game/Items";
        const string LibraryPath = Folder + "/ItemLibrary.asset";

        [MenuItem("Tools/Gulag Runners/Create item data files")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Game", "Items");

            List<ItemDefAsset> all = new List<ItemDefAsset>();
            int made = 0, kept = 0;

            foreach (ItemId id in System.Enum.GetValues(typeof(ItemId)))
            {
                if (id == ItemId.None) continue;

                ItemDef def = ItemTable.Get(id);
                if (def.Kind == ItemKind.None) continue;

                string path = $"{Folder}/{def.Kind}_{id}.asset";
                ItemDefAsset asset = AssetDatabase.LoadAssetAtPath<ItemDefAsset>(path);

                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<ItemDefAsset>();
                    asset.ReadFrom(in def);
                    AssetDatabase.CreateAsset(asset, path);
                    made++;
                }
                else kept++;

                all.Add(asset);
            }

            ItemLibrary library = AssetDatabase.LoadAssetAtPath<ItemLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ItemLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            // A file for an item the table no longer has is the dangerous case, and the one
            // that actually happened: the shield was deleted from ItemId and its data file
            // stayed behind, so the library went on applying id 23 and a BUILD had an item
            // no test and no mirror knew about. Re-running this used to rebuild the list
            // from the live table and say nothing about the file left over.
            List<string> stale = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefAsset", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ItemDefAsset asset = AssetDatabase.LoadAssetAtPath<ItemDefAsset>(path);
                if (asset == null || all.Contains(asset)) continue;
                stale.Add($"{path} (item {(int)asset.item})");
            }

            library.items = all.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = library;
            EditorGUIUtility.PingObject(library);

            Debug.Log($"Item data files: {made} written, {kept} already there, " +
                      $"{all.Count} in the library.\n" +
                      "Drop ItemLibrary on MatchState's Item Library slot to make the files " +
                      "the ones the game reads. Leave it empty and the table in ItemTable.cs " +
                      "is used, exactly as before.\n" +
                      "This never overwrites a file that already exists — that would throw " +
                      "away the tuning the files are for — so docs/refs/item_library_check.py " +
                      "is what tells you the two have drifted apart.", library);

            if (stale.Count > 0)
                Debug.LogWarning(
                    $"{stale.Count} data file(s) for items the simulation no longer has. " +
                    "They are out of the library now, but delete them: a file left in this " +
                    "folder is one \"create\" away from being applied again.\n  " +
                    string.Join("\n  ", stale), library);
        }
    }
}
