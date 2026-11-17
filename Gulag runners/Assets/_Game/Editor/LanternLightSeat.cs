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

        // How close a light has to be to a lamp, horizontally, to be that lamp's light.
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

            int moved = 0, already = 0, orphan = 0;

            foreach (Light light in Object.FindObjectsByType<Light>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (light.type != LightType.Point) continue;

                // The lamp this light stands at: nearest in the horizontal plane, and only
                // if it is close enough to be plausibly its own. A poison glow or a key
                // light somewhere else in the arena is not claimed by a lantern.
                Transform lamp = null;
                float best = Claim;
                foreach (Transform t in lamps)
                {
                    Vector3 d = t.position - light.transform.position;
                    float flat = new Vector2(d.x, d.z).magnitude;
                    if (flat < best) { best = flat; lamp = t; }
                }
                if (lamp == null) { orphan++; continue; }

                // The foot is the lamp's own origin; the flame is a fixed height above it,
                // in the lamp's own space, so a lantern that is scaled or turned still gets
                // its light in the right place.
                Vector3 want = lamp.TransformPoint(Vector3.up * FlameAboveFoot);
                if ((light.transform.position - want).sqrMagnitude < 0.0004f)
                {
                    already++;
                    continue;
                }

                Undo.RecordObject(light.transform, "Seat the lantern lights");
                light.transform.position = want;
                EditorUtility.SetDirty(light.transform);
                moved++;
            }

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"{scene.name}: {lamps.Count} lantern(s) found.\n"
                + $"  {moved} light(s) moved into their flame, {already} already there, "
                + $"{orphan} point light(s) belong to no lantern and were left alone.\n"
                + "  The lamps are lit by probes, not the lightmap, so they also carry an "
                + "emissive glass now -- see LampSway. Re-bake for the light itself.");
        }
    }
}
