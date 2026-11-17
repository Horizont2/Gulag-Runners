using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts each lantern's light inside the lantern, at the flame.
    ///
    /// Measured in Arena_Mine: every Lantern Light sits 9 cm BELOW the lamp it belongs to
    /// -- and the lamp's transform origin is at its FOOT, with the flame 0.21 m above that
    /// (LampSway's own figures, taken off PP_Lantern_02 when the sway was written). So the
    /// light was 30 cm under the flame, hanging in the air beneath a closed iron cage,
    /// which is why the beam above was lit and the lamp itself was a black shape.
    ///
    /// The fix is not a tuning number. A lamp's light belongs where the flame is, and the
    /// flame's height is already written down in the shader that swings it; this reads it
    /// from there rather than inventing a second one.
    ///
    /// THE OTHER HALF IS IN THE SHADER. A hung lamp cannot be in the lightmap -- a vertex
    /// that moves cannot carry a baked shadow that does not -- so it is lit by light
    /// probes, and a probe in a dark mine averages to nearly black. Probe-lit geometry can
    /// never see the lamp it IS. LampSway grows an emissive term for that, weighted by the
    /// texture's own brightness so the glass glows and the iron does not.
    /// </summary>
    public static class LanternLightSeat
    {
        // From LampSway.shader: "the flame sits 0.21 m above the foot and the hook 0.575".
        const float FlameAboveFoot = 0.21f;

        /// <summary>
        /// How close a light has to be to a lamp to be that lamp's light. In THREE
        /// dimensions, and that is the whole of the bug this constant used to carry.
        ///
        /// It was a horizontal distance, measured across x and z with the height thrown
        /// away, and in Arena_Mine three pairs of lanterns hang on the same pillar one
        /// storey above the other -- same x, same z, two metres apart in y. Each lower lamp
        /// was therefore exactly as close to the upper lamp's light as to its own, and the
        /// run collapsed both of them onto itself: six lights became three, each pair in
        /// one spot, and every upper lantern went dark. It is visible in the scene diff as
        /// two different heights replaced by one identical number.
        ///
        /// Two thirds of a metre is comfortably more than the 30 cm a light was ever found
        /// away from its flame, and comfortably less than the 2 m between stacked lamps.
        /// </summary>
        const float Claim = 0.6f;

        [MenuItem("Tools/Gulag Runners/Setup/Seat the lantern lights in their flames")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            List<Transform> lamps = new List<Transform>();
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool swaying = false;
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.shader != null
                        && m.shader.name.Contains("Lamp Sway")) swaying = true;
                if (swaying) lamps.Add(r.transform);
            }

            if (lamps.Count == 0)
            {
                Debug.LogWarning($"{scene.name} has no lantern wearing the Lamp Sway shader, "
                                 + "so there is nothing to seat a light in.");
                return;
            }

            List<Light> points = new List<Light>();
            foreach (Light light in Object.FindObjectsByType<Light>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (light.type == LightType.Point) points.Add(light);

            int moved = 0, already = 0, dark = 0;

            // EACH LAMP TAKES ONE LIGHT, rather than each light looking for a lamp, and the
            // direction matters. Asked the other way about, two lamps can settle on the same
            // light and both move it -- which is exactly what happened in Arena_Mine, where
            // the lower lantern of each stacked pair swallowed the upper one's light and
            // left it dark. A light can only be taken once if the taking is done by the
            // thing there is one of.
            HashSet<Light> taken = new HashSet<Light>();

            foreach (Transform lamp in lamps)
            {
                // The foot is the lamp's own origin; the flame is a fixed height above it,
                // in the lamp's own space, so a lantern that is scaled or turned still gets
                // its light in the right place.
                Vector3 want = lamp.TransformPoint(Vector3.up * FlameAboveFoot);

                Light mine = null;
                float best = Claim;
                foreach (Light light in points)
                {
                    if (taken.Contains(light)) continue;
                    float gap = Vector3.Distance(light.transform.position, lamp.position);
                    if (gap < best) { best = gap; mine = light; }
                }

                if (mine == null)
                {
                    // A lantern with no light within reach. Said out loud rather than passed
                    // over: it is either a lamp nobody lit, or a light this run has already
                    // given to its neighbour, and both are worth seeing.
                    dark++;
                    continue;
                }

                taken.Add(mine);

                if ((mine.transform.position - want).sqrMagnitude < 0.0004f)
                {
                    already++;
                    continue;
                }

                Undo.RecordObject(mine.transform, "Seat the lantern lights");
                mine.transform.position = want;
                EditorUtility.SetDirty(mine.transform);
                moved++;
            }

            int orphan = points.Count - taken.Count;

            EditorSceneManager.MarkSceneDirty(scene);

            string report =
                $"{scene.name}: {lamps.Count} lantern(s) found.\n"
                + $"  {moved} light(s) moved into their flame, {already} already there, "
                + $"{orphan} point light(s) belong to no lantern and were left alone.\n"
                + "  The lamps are lit by probes, not the lightmap, so they also carry an "
                + "emissive glass now -- see LampSway. Re-bake for the light itself.";

            if (dark > 0)
            {
                Debug.LogWarning(
                    report + $"\n  {dark} lantern(s) have no point light within "
                    + $"{Claim:0.00} m of them and are unlit. If this scene was run through "
                    + "an older version of this tool, its lights may have been stacked on "
                    + "top of each other -- that version measured only across the ground, "
                    + "so a lantern claimed the light of the one hanging above it. Restore "
                    + "the scene's light positions from git and run this again.");
                return;
            }

            Debug.Log(report);
        }
    }
}
