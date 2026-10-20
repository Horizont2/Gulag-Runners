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
                      "is used, exactly as before.", library);
        }
    }
}
