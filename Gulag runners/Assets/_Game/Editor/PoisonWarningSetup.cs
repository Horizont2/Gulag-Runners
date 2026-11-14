using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Wires the gas announcement into an arena (docs/33).
    ///
    /// <see cref="PoisonWarning"/> is three beats of theatre and none of them happen unless
    /// something hands it the arena's lamps, the pool and a collar. That is a job of
    /// finding things in a scene, which is a job for a tool rather than for a person
    /// dragging eleven lights into a list four times over.
    ///
    /// What it will NOT do is invent the vents. Those are emitters somebody has to author
    /// -- a jet of gas coming out from under the floor -- and an empty vent list is a
    /// working announcement that leans on the pool's own smoke instead. The tool says so
    /// when it finds none, rather than leaving the field quietly blank.
    /// </summary>
    public static class PoisonWarningSetup
    {
        [MenuItem("Tools/Gulag Runners/Announce the gas")]
        public static void Run()
        {
            PoisonPool pool = Object.FindFirstObjectByType<PoisonPool>();
            if (pool == null)
            {
                Debug.LogWarning("No PoisonPool in this scene, so there is no gas to "
                                 + "announce. Open an arena.");
                return;
            }

            MatchState match = Object.FindFirstObjectByType<MatchState>();
            SideViewCamera camera = Object.FindFirstObjectByType<SideViewCamera>();

            // On the pool, because that is the thing it is about, and because a component
            // on the pool is a component somebody finds when they ask why the gas behaves
            // as it does.
            PoisonWarning warn = pool.GetComponent<PoisonWarning>();
            if (warn == null) warn = Undo.AddComponent<PoisonWarning>(pool.gameObject);
            Undo.RecordObject(warn, "Announce the gas");

            warn.matchState = match;
            warn.pool = pool;
            warn.shake = camera;

            // Every light in the arena except the pool's own: the brown-out has to be the
            // ARENA dipping, and dipping the green glow that is about to arrive would be
            // the announcement cancelling itself.
            List<Light> lamps = new List<Light>();
            Transform glow = pool.glow;
            foreach (Light light in Object.FindObjectsByType<Light>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light == null || light.type == LightType.Directional) continue;
                if (glow != null && light.transform.IsChildOf(glow)) continue;
                if (light.transform.IsChildOf(pool.transform)) continue;
                lamps.Add(light);
            }
            warn.lamps = lamps.ToArray();

            // The collar, taken the way SirenView takes it: the one piece of hardware both
            // fighters wear, and where docs/01 puts the game's own signals.
            int collars = 0;
            foreach (SirenView siren in Object.FindObjectsByType<SirenView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (siren == null || siren.collar == null) continue;
                if (warn.collar == null) { warn.collar = siren.collar; collars++; }
            }

            EditorUtility.SetDirty(warn);
            Selection.activeGameObject = pool.gameObject;

            Debug.Log($"Gas announcement: {lamps.Count} lamp(s) will dip, "
                      + (camera != null ? "the camera gets one shove, " : "NO camera found, ")
                      + (collars > 0 ? "and a collar pulses as it nears your feet. "
                                     : "and no collar was found, so the third beat is "
                                       + "silent — run Tools > Gulag Runners > Set up the "
                                       + "siren first, which is what puts one on a fighter. ")
                      + (warn.vents.Length == 0
                          ? "No vents are wired: the burst leans on the pool's own source "
                            + "smoke. Drop emitters into Vents for a jet that says WHERE it "
                            + "comes out."
                          : $"{warn.vents.Length} vent(s) burst."),
                      warn);
        }
    }
}
