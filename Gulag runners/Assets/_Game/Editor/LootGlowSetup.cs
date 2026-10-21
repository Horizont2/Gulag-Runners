using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Makes the rarity glow actually visible: switches EMISSION on in the item materials,
    /// and hangs a light on every loot slot.
    ///
    /// WHY IT WAS NEVER SEEN. Both views write the rarity colour into a
    /// MaterialPropertyBlock as _EmissionColor. A property block can set a property; it
    /// cannot enable a shader keyword. URP/Lit compiles emission OUT unless the material
    /// has its Emission tick box on, and the weapon pack's Atlas.mat ships with it off and
    /// m_LightmapFlags 4 — EmissiveIsBlack. So every tier colour this project has ever
    /// written has gone into a property nothing reads. That is not a tuning problem, it is
    /// a switch, and it is three menus deep inside a third-party material.
    ///
    /// And a switch is not enough on its own. A 30 cm weapon glowing slightly is not
    /// something anybody notices from the other end of an arena, which is the one moment
    /// this is for: deciding whether that thing on the floor is worth the walk. So each
    /// loot slot gets a small light as well, coloured and pulsed by the same number.
    ///
    /// Both halves are assets and scene objects, visible and editable afterwards. Nothing
    /// is made at play time.
    /// </summary>
    public static class LootGlowSetup
    {
        const string LightName = "Rarity Glow";

        [MenuItem("Tools/Gulag Runners/Light up the loot")]
        public static void Setup()
        {
            int lit = Lights();
            int switched = Emission();

            Debug.Log($"Loot glow: {lit} light(s) on loot slots, emission switched on in " +
                      $"{switched} material(s).\nIf a rarity still does not read, raise " +
                      "Light Strength on the view — it is the COMMON brightness, and rare " +
                      "and legendary are multiples of it.");
        }

        /// <summary>A light under every loot slot that has not got one.</summary>
        static int Lights()
        {
            GroundItemView[] views =
                Object.FindObjectsByType<GroundItemView>(FindObjectsInactive.Include,
                                                          FindObjectsSortMode.None);
            int made = 0;

            foreach (GroundItemView view in views)
            {
                if (view.rarityLight != null) continue;

                Transform had = view.transform.Find(LightName);
                Light light = had != null ? had.GetComponent<Light>() : null;

                if (light == null)
                {
                    GameObject go = new GameObject(LightName);
                    Undo.RegisterCreatedObjectUndo(go, "Light up the loot");
                    go.transform.SetParent(view.transform, false);

                    // A little above the item, so it lights the floor around it rather than
                    // only the model: the halo on the ground is what is seen at a distance.
                    go.transform.localPosition = new Vector3(0f, 0.18f, 0f);

                    light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.shadows = LightShadows.None;   // a dozen shadowed lights on a phone
                    light.renderMode = LightRenderMode.ForceVertex;
                    light.intensity = 0f;                // the view drives it
                    light.range = 1.6f;
                }

                Undo.RecordObject(view, "Light up the loot");
                view.rarityLight = light;
                EditorUtility.SetDirty(view);
                made++;
            }

            return made;
        }

        /// <summary>
        /// Emission on, in every material a loot model or a held weapon actually uses.
        ///
        /// Found through the models rather than by sweeping the project: switching emission
        /// on in somebody's whole asset store folder is not this tool's business.
        /// </summary>
        static int Emission()
        {
            var seen = new HashSet<Material>();
            var roots = new List<GameObject>();

            foreach (GroundItemView view in
                     Object.FindObjectsByType<GroundItemView>(FindObjectsInactive.Include,
                                                               FindObjectsSortMode.None))
            {
                if (view.models != null)
                    foreach (ItemModel m in view.models)
                        if (m?.root != null) roots.Add(m.root);
                if (view.fallbackModel != null) roots.Add(view.fallbackModel);
            }

            foreach (PlayerLoadoutView view in
                     Object.FindObjectsByType<PlayerLoadoutView>(FindObjectsInactive.Include,
                                                                  FindObjectsSortMode.None))
            {
                foreach (ItemModel[] set in new[] { view.weapons, view.armour })
                    if (set != null)
                        foreach (ItemModel m in set)
                            if (m?.root != null) roots.Add(m.root);
            }

            // And the worn kit. Without this the rarity colour PlayerArmourView writes
            // goes into a property URP has compiled out, which is the same dead write the
            // loot models had before this tool existed -- and five slots means five ways
            // to hit it instead of one.
            foreach (PlayerArmourView view in
                     Object.FindObjectsByType<PlayerArmourView>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None))
            {
                if (view.allParts == null) continue;
                foreach (GameObject part in view.allParts)
                    if (part != null) roots.Add(part);
            }

            int switched = 0;
            foreach (GameObject root in roots)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material mat in r.sharedMaterials)
                    {
                        if (mat == null || !seen.Add(mat)) continue;
                        if (mat.IsKeywordEnabled("_EMISSION") &&
                            mat.globalIlluminationFlags != MaterialGlobalIlluminationFlags.EmissiveIsBlack)
                            continue;

                        Undo.RecordObject(mat, "Light up the loot");
                        mat.EnableKeyword("_EMISSION");

                        // Realtime GI must not bake this: the colour changes every frame.
                        mat.globalIlluminationFlags =
                            MaterialGlobalIlluminationFlags.RealtimeEmissive;

                        // Black to start with. The views write the real colour every frame,
                        // and a material left glowing white would show through everything
                        // that has no view driving it.
                        if (mat.HasProperty("_EmissionColor"))
                            mat.SetColor("_EmissionColor", Color.black);

                        EditorUtility.SetDirty(mat);
                        switched++;
                    }
                }
            }

            return switched;
        }
    }
}
