using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts a Terrain under Arena_Demo so the location can be painted rather than assembled
    /// out of cubes.
    ///
    /// Made from a menu item rather than committed as a scene object because a TerrainData is
    /// a binary asset with a heightmap in it: authored by hand it is a 300 KB blob nobody can
    /// review, and authored by the API it is four lines that say what it is.
    ///
    /// Two things about this arena decide where the terrain goes, and both are easy to get
    /// wrong in a way that only shows up in play:
    ///
    ///   DEPTH. The camera sits in front of the fight and looks slightly down, so anything
    ///   modelled in FRONT of the fighters and below the floor line hides the basement. The
    ///   terrain therefore starts behind the fighters' own plane and runs away from the
    ///   camera. Dragging its front edge forward is the one move that will black out the
    ///   lower floor.
    ///
    ///   COLLISION. A Terrain carries a TerrainCollider, and the bake turns every collider it
    ///   finds into a solid box the size of that collider's bounds — which here would be a
    ///   forty-metre wall across the whole arena, exactly the fault that pinned both fighters
    ///   at their spawns. So the terrain is marked Ignore as it is created. The fight's
    ///   collision stays what it has always been: the boxes in the scene.
    /// </summary>
    public static class ForestTerrainSetup
    {
        const string AssetFolder = "Assets/_Game/Terrain";
        const string AssetPath = AssetFolder + "/Arena_Terrain.asset";

        // Arena_Demo measures x -18.12..14.34, y -1.94..6.00. The terrain is wider than that
        // on both sides so the tree line runs past the ends of the level rather than stopping
        // at them.
        static readonly Vector3 Origin = new Vector3(-26f, 0f, -2f);
        static readonly Vector3 Size = new Vector3(52f, 20f, 48f);

        [MenuItem("Tools/Gulag Runners/Setup/Create forest terrain")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder("Assets/_Game", "Terrain");

            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(AssetPath);
            if (data == null)
            {
                data = new TerrainData();

                // Set before size: TerrainData rescales its heights to the resolution, and a
                // resolution changed afterwards throws the size away.
                data.heightmapResolution = 513;
                data.size = Size;

                // Grass and the other detail meshes are painted onto this grid, not the
                // heightmap's. 1024 over 52 m is about one cell every 5 cm, which is fine
                // enough for a tree line and still cheap on a phone.
                data.SetDetailResolution(1024, 16);
                data.baseMapResolution = 1024;
                data.alphamapResolution = 512;

                AssetDatabase.CreateAsset(data, AssetPath);
                AssetDatabase.SaveAssets();
            }

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Forest Terrain";
            go.transform.position = Origin;

            Terrain terrain = go.GetComponent<Terrain>();
            if (terrain != null)
            {
                // A phone draws this every frame behind the whole fight.
                terrain.heightmapPixelError = 12f;
                terrain.basemapDistance = 120f;
                terrain.detailObjectDistance = 90f;
                terrain.treeDistance = 180f;
                terrain.treeBillboardDistance = 40f;
                terrain.castShadows = false;
            }

            // The line that keeps this out of the simulation. Without it the TerrainCollider
            // bakes as one solid box across the arena.
            SimCollider marker = go.GetComponent<SimCollider>();
            if (marker == null) marker = go.AddComponent<SimCollider>();
            marker.kind = SimColliderKind.Ignore;

            Undo.RegisterCreatedObjectUndo(go, "Create forest terrain");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log(
                "Forest Terrain created.\n" +
                $"  origin {Origin}, size {Size}, surface at y {Origin.y:0.00}\n" +
                "  marked SimCollider = Ignore, so the bake leaves it alone\n" +
                "  front edge at z " + Origin.z.ToString("0.00") +
                " — keep it behind the fighters' plane (z -2.99) or it hides the basement\n" +
                "Next: Terrain inspector -> Paint Trees -> Edit Trees -> Add Tree for each " +
                "prefab, and Paint Details -> Edit Details for grass. Both lists start empty " +
                "on purpose; they are the art, and it is not chosen yet.", go);
        }
    }
}
