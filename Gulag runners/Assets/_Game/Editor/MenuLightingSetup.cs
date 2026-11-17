using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Lights the menu scene, and re-scales its volumetric fog to the room it is in.
    ///
    /// THE FOG. The pack's defaults are built for a landscape: near fade 5 m, maximum
    /// distance 4500 m, a raymarch step of 105 m, noise features 800 m across. This room
    /// is 4.0 m wide and the arch is 3.7 m from the camera, so every one of those numbers
    /// was wrong by two or three orders of magnitude. The step alone meant the raymarcher
    /// took ONE sample across the whole scene, and the near fade meant the fog did not
    /// begin until past the gateway it had been added to veil. Here they are set from the
    /// scene's own measured bounds instead of typed.
    ///
    /// THE LIGHT. A menu scene has one job: show the character. So it gets the oldest
    /// lighting in the book, which is also the one the art prompt describes — a warm key
    /// close on one side, a cold fill from the other, and ambient dark enough that the two
    /// of them are what you see. The scene arrived with a single default directional light
    /// at full strength, which is flat white from above and the reason stone reads as
    /// cardboard.
    ///
    /// Everything it makes is a GameObject in the scene, visible and movable afterwards.
    /// Run it again and it updates what it made rather than making a second one.
    /// </summary>
    public static class MenuLightingSetup
    {
        const string Root = "Menu Lighting";
        const string KeyName = "Torch Key";
        const string FillName = "Gate Fill";

        [MenuItem("Tools/Gulag Runners/Setup/Light the menu scene")]
        public static void Setup()
        {
            Scene scene = SceneManager.GetActiveScene();
            Camera cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                Debug.LogWarning("No camera in this scene, so there is nothing to light for.");
                return;
            }

            if (!Room(out Bounds room))
            {
                Debug.LogWarning("Nothing with a renderer in this scene to measure.");
                return;
            }

            Transform holder = Holder();

            // The key sits just off the left wall at head height, a little in front of the
            // camera's subject: the torch in the art, and the only warm thing in the room.
            Vector3 key = new Vector3(room.min.x + room.size.x * 0.12f,
                                      room.min.y + 1.75f,
                                      cam.transform.position.z + 2.0f);
            Light torch = Lamp(holder, KeyName, key);
            torch.color = new Color(1f, 0.72f, 0.38f);
            torch.intensity = 2.6f;
            torch.range = Mathf.Max(4f, room.size.x * 1.4f);
            torch.shadows = LightShadows.Soft;
            torch.shadowStrength = 0.75f;
            torch.bounceIntensity = 0f;

            // And a cold one just beyond the arch, dim and wide. It is not there to light
            // anything: it is there to give the fog something to catch, so the gateway
            // reads as a way out rather than as a wall.
            Vector3 fill = new Vector3(room.center.x, room.min.y + 1.4f, room.max.z + 1.2f);
            Light gate = Lamp(holder, FillName, fill);
            gate.color = new Color(0.58f, 0.72f, 1f);
            gate.intensity = 1.5f;
            gate.range = Mathf.Max(6f, room.size.z * 1.5f);
            gate.shadows = LightShadows.None;
            gate.bounceIntensity = 0f;

            Sun();
            Ambient(cam);
            int fogged = Fog(room, cam);

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"Menu lighting: room measured {room.size.x:0.00} x {room.size.y:0.00} x " +
                $"{room.size.z:0.00} m, centre {room.center}.\n" +
                $"Key light at {key}, fill at {fill}. " +
                (fogged > 0
                    ? $"{fogged} fog field(s) rescaled to the room."
                    : "No volumetric fog component found, so none was rescaled."));
        }

        /// <summary>
        /// The room, from everything that is drawn in it.
        ///
        /// Measured rather than typed, because the one thing a tool must not do is carry a
        /// copy of the level's dimensions: the day somebody widens the cell, a typed number
        /// is wrong and says nothing about it.
        /// </summary>
        static bool Room(out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude,
                                                                      FindObjectsSortMode.None))
            {
                // The terrain is left OUT of the measurement -- it is forty times the
                // room and would put the key light in a field. It is not left out of the
                // scene: the fog needs it, and removing it removes the fog.
                if (r.GetComponentInParent<Terrain>() != null) continue;
                if (r is ParticleSystemRenderer) continue;

                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            return any;
        }

        static Transform Holder()
        {
            GameObject go = GameObject.Find(Root);
            if (go == null)
            {
                go = new GameObject(Root);
                Undo.RegisterCreatedObjectUndo(go, "Light the menu scene");
            }
            return go.transform;
        }

        static Light Lamp(Transform parent, string name, Vector3 at)
        {
            Transform had = parent.Find(name);
            GameObject go = had != null ? had.gameObject : null;

            if (go == null)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Light the menu scene");
                go.transform.SetParent(parent, false);
            }

            Undo.RecordObject(go.transform, "Light the menu scene");
            go.transform.position = at;

            Light light = go.GetComponent<Light>() ?? go.AddComponent<Light>();
            Undo.RecordObject(light, "Light the menu scene");
            light.type = LightType.Point;
            EditorUtility.SetDirty(light);
            return light;
        }

        /// <summary>The directional light stops being the sun and becomes moonlight.</summary>
        static void Sun()
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude,
                                                                FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;

                Undo.RecordObject(l, "Light the menu scene");
                l.color = new Color(0.56f, 0.68f, 0.95f);
                l.intensity = 0.32f;
                l.bounceIntensity = 0f;
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.6f;
                EditorUtility.SetDirty(l);
            }
        }

        /// <summary>
        /// Dark, flat and cool, and the camera clears to almost black.
        ///
        /// Skybox ambient in an interior is the single commonest reason a dungeon looks
        /// like a daylit model of a dungeon: it lifts every surface evenly, which is the
        /// opposite of what a torch does.
        /// </summary>
        static void Ambient(Camera cam)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.12f, 0.17f);
            RenderSettings.ambientIntensity = 1f;

            Undo.RecordObject(cam, "Light the menu scene");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.045f, 0.055f, 0.075f);
            EditorUtility.SetDirty(cam);
        }

        /// <summary>
        /// Every distance in the fog, from the room's own size.
        ///
        /// Set through SerializedObject by field NAME, with no reference to the pack's
        /// type. The fog is a third-party asset; naming its class here would mean this
        /// file stops compiling the day it is removed, and an editor script that breaks
        /// the project when an asset is deleted is worse than no editor script.
        /// </summary>
        static int Fog(Bounds room, Camera cam)
        {
            MonoBehaviour fog = null;
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                if (mb.GetType().Name.Contains("VolumetricFog")) { fog = mb; break; }
            }
            if (fog == null) return 0;

            // How far the camera is from the far wall: the one distance everything else
            // here is a fraction or a multiple of.
            float depth = Mathf.Max(2f, room.max.z - cam.transform.position.z);
            float reach = Mathf.Max(room.size.x, Mathf.Max(room.size.y, room.size.z));

            var set = new Dictionary<string, float>
            {
                // Begin just BEHIND THE FIGHTER, not behind the far wall. 0.85 of the
                // depth put the first haze past everything worth hazing: the shaft of
                // moonlight through the ceiling slot was still crisp air by the time it
                // reached the floor. Half the depth starts it a step past him, so he
                // stays sharp and everything behind him recedes.
                { "nearFadeDistance", depth * 0.5f },
                { "maximumFogDistance", Mathf.Max(20f, depth * 10f) },

                // One raymarch step was 105 m. The component clamps this at 2, so that is
                // as fine as it gets -- about five samples across a room this size.
                { "targetStepLength", Mathf.Max(2f, depth / 10f) },
                { "shadowDistance", Mathf.Max(20f, depth * 5f) },

                { "groundFogHeight", Mathf.Max(1.5f, room.size.y * 1.2f) },
                { "topSoftness", 0.35f },
                { "largeNoiseScale", Mathf.Max(3f, reach * 1.5f) },
                { "detailNoiseScale", Mathf.Max(1f, reach * 0.4f) },
                { "windSpeed", 0.25f },
                { "density", 0.3f },            // the component's own ceiling
                { "anisotropy", 0.65f },        // forward scatter, so the bars throw shafts
                { "sunScattering", 0.8f },
                { "ambientStrength", 0.55f },
                { "fogSelfShadowStrength", 1.2f },

                // NOT shrunk to the room. The volume is built from the TERRAIN's bounds
                // plus this padding, and the terrain in this scene sits several metres
                // away -- cut it down and the room falls outside its own fog.
                { "horizontalPadding", 40f },
                { "topPadding", 60f },
                { "bottomPadding", 20f },
            };

            var colours = new Dictionary<string, Color>
            {
                { "fogColor", new Color(0.52f, 0.60f, 0.70f) },
                { "ambientColor", new Color(0.22f, 0.27f, 0.36f) },
            };

            var flags = new Dictionary<string, bool>
            {
                // ON, always. UpdateTerrainBounds returns early when no terrain resolves,
                // and the volume is built from nothing else -- so with this off and the
                // terrain list empty there is no volume and therefore NO FOG AT ALL.
                // Turning it off once because a terrain looked like a leftover is what
                // made the fog vanish, and the terrain is not a leftover: it is the only
                // thing giving this effect a ground to sit on.
                { "findActiveTerrainsAutomatically", true },
                { "enableVolumetricShadows", true },
                { "useUnityRealtimeShadows", true },
            };

            if (Object.FindFirstObjectByType<Terrain>() == null)
                Debug.LogWarning(
                    "This scene has no Terrain. The volumetric fog builds its entire volume " +
                    "from terrain bounds, so it will render nothing at all until there is " +
                    "one — a single flat terrain under the room is enough.");

            SerializedObject so = new SerializedObject(fog);
            int done = 0;

            foreach (KeyValuePair<string, float> f in set)
            {
                SerializedProperty prop = so.FindProperty(f.Key);
                if (prop == null || prop.propertyType != SerializedPropertyType.Float) continue;
                prop.floatValue = f.Value;
                done++;
            }
            foreach (KeyValuePair<string, Color> c in colours)
            {
                SerializedProperty prop = so.FindProperty(c.Key);
                if (prop == null || prop.propertyType != SerializedPropertyType.Color) continue;
                prop.colorValue = c.Value;
                done++;
            }
            foreach (KeyValuePair<string, bool> b in flags)
            {
                SerializedProperty prop = so.FindProperty(b.Key);
                if (prop == null || prop.propertyType != SerializedPropertyType.Boolean) continue;
                prop.boolValue = b.Value;
                done++;
            }

            so.ApplyModifiedProperties();
            return done;
        }
    }
}
