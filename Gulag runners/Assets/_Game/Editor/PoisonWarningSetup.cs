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

            // The vents. The file used to say "an empty vent list is a working announcement
            // that leans on the pool's own smoke instead", and that was honest while there
            // was nothing to put there. There is now: GR_GasVent, built by
            // Tools > Gulag Runners > Build the hit effects.
            //
            // Three of them, spread across the pool's width at its floor, because docs/33
            // wants the player to learn WHERE it comes out -- one jet in the middle is a
            // special effect, three along the room is a room filling up.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Effects/GR_GasVent.prefab");
            int vents = 0;
            if (prefab != null)
            {
                Bounds box = pool.Flooded;
                List<ParticleSystem> made = new List<ParticleSystem>();

                // The jets go on the FIGHT PLANE, not down the middle of the pool.
                //
                // Flooded is a MEASUREMENT volume: it spans the whole depth of the arena so
                // the simulation can tell whether a body on any slice is under the surface.
                // In Arena_Mine and Arena_Egypt it runs from z -3.4 to +5.2 and its centre
                // is at +0.9, while the fighters stand at -2.12. Putting the vents at that
                // centre buried them three metres back, inside and behind the lowest
                // storey's own geometry -- which is what "the gas appears behind the arena"
                // was. The pool knows how deep it reaches; only the fighters know where the
                // fight is.
                float jetZ = FightPlane(box.center.z);

                Transform old_ = pool.transform.Find("Vents");
                if (old_ != null) Undo.DestroyObjectImmediate(old_.gameObject);

                GameObject holder = new GameObject("Vents");
                Undo.RegisterCreatedObjectUndo(holder, "Announce the gas");
                holder.transform.SetParent(pool.transform, false);

                for (int i = 0; i < 3; i++)
                {
                    float t = (i + 1) / 4f;          // 1/4, 1/2, 3/4 of the way across
                    GameObject vent =
                        (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
                    Undo.RegisterCreatedObjectUndo(vent, "Announce the gas");
                    vent.name = $"Vent {i + 1}";
                    vent.transform.position = new Vector3(
                        Mathf.Lerp(box.min.x, box.max.x, t), box.min.y, jetZ);

                    ParticleSystem ps = vent.GetComponent<ParticleSystem>();
                    if (ps != null) made.Add(ps);
                }

                warn.vents = made.ToArray();
                vents = made.Count;
            }

            EditorUtility.SetDirty(warn);
            Selection.activeGameObject = pool.gameObject;

            Debug.Log($"Gas announcement: {lamps.Count} lamp(s) will dip, "
                      + (camera != null ? "the camera gets one shove, " : "NO camera found, ")
                      + (collars > 0 ? "and a collar pulses as it nears your feet. "
                                     : "and no collar was found, so the third beat is "
                                       + "silent — run Tools > Gulag Runners > Set up the "
                                       + "siren first, which is what puts one on a fighter. ")
                      + (vents == 0
                          ? "NO vents: Assets/_Game/Effects/GR_GasVent.prefab does not "
                            + "exist yet. Run Tools > Gulag Runners > Build the hit effects "
                            + "first, then this again — without it the burst leans on the "
                            + "pool's own source smoke and nothing says WHERE the gas "
                            + "comes out."
                          : $"{vents} vent(s) burst out of the floor."),
                      warn);
        }

        /// <summary>
        /// The depth the fighters actually stand on.
        ///
        /// Asked of the fighters first, because planeZ on a PlayerController is the number
        /// the simulation uses; the camera's copy is derived from it and can be stale if
        /// depthFromPlayer is on. The fallback is whatever was passed in, so a scene with
        /// neither still gets vents somewhere rather than none.
        /// </summary>
        static float FightPlane(float fallback)
        {
            PlayerController fighter = Object.FindFirstObjectByType<PlayerController>(
                FindObjectsInactive.Include);
            if (fighter != null) return fighter.planeZ;

            SideViewCamera rig = Object.FindFirstObjectByType<SideViewCamera>(
                FindObjectsInactive.Include);
            if (rig != null) return rig.planeZ;

            return fallback;
        }

    }
}
