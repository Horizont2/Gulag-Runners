using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts something in the air of an arena that has nothing in it.
    ///
    /// THE FIRST THING THIS DOES IS LOOK. Three of the four arenas were already dressed and
    /// it was not obvious: the effects live in prefab instances, so the components are in
    /// the prefab assets and a search of the scene files finds nothing at all. Arena_Forest
    /// has Rain Particles, Arena_Nordic fortress has Snow Particles, Arena_Mine has three
    /// of Polyart's P_Dust_Gray_Wind spaced ten metres apart -- and all three are properly
    /// placed, not dropped at their demo transforms. The snow emits from 7.39 m, above a
    /// ceiling of 6.2, out of a box 40 m wide across an arena 32 wide. Somebody sized that.
    ///
    /// So this refuses to add a second weather system on top of a first. The test is not a
    /// list of prefab names, which would miss the next one somebody adds; it is what makes
    /// a thing ambient in the first place -- a particle system that loops and starts itself.
    /// Every effect this project builds for hits, dust and gas has playOnAwake off, because
    /// each is fired by an event, so the two kinds cannot be confused.
    ///
    /// ARENA_EGYPT HAS AIR NOW TOO, put in the scene rather than built here: three of the
    /// same P_Dust_Gray_Wind emitters the mine uses, at x -10, 0 and 10 under an
    /// Atmosphere root, on the project's own Particle Soft material. Each is a 10 x 3 x
    /// 10 m box against borders at x +-15.1, so the three of them cover the arena.
    ///
    /// Grey dust and not yellow sand, which is the one thing worth saying about the
    /// choice: lit sand at night is not the colour of sand, it is the moon picking out
    /// grains, and the Moods entry below says the same thing in its own tint. Reusing the
    /// emitter the mine already ships costs no texture, no material and no new prefab.
    ///
    /// So this tool now has nothing left to build in any of the five, and running it says
    /// so, per scene, naming what it found. That is the useful answer rather than a
    /// no-op: the question it answers is whether an arena's air is empty, and the Moods
    /// table below stays as the shape of an answer for the next arena somebody adds.
    /// </summary>
    public static class ArenaAtmosphereSetup
    {
        const string HolderName = "Atmosphere";
        const string MaterialsDir = "Assets/_Game/Effects/Materials";
        const string TexturesDir = "Assets/_Game/Effects/Textures";

        /// <summary>What hangs in the air of one arena.</summary>
        struct Air
        {
            public string name;
            public Color tint;
            public float rate;          // particles per second across the whole arena
            public float life;          // seconds, so rate * life is the count alive
            public float small, large;  // metres
            public Vector2 drift;       // steady wind
            public Vector2 scatter;     // how much each mote wanders off it
            public float depth;         // metres of Z the box covers, around the fight plane
        }

        // Only arenas whose air is empty. Adding one here is not enough on its own -- if the
        // scene turns out to have weather already, the tool still stands down.
        static readonly Dictionary<string, Air> Moods = new Dictionary<string, Air>
        {
            {
                "Arena_Egypt", new Air
                {
                    name = "Sand on the wind",
                    // Warm and pale, and very faint. Lit sand at night is not yellow, it is
                    // the moon picking out grains, so this sits a little warmer than the
                    // scene's blue ambient and nowhere near full brightness.
                    tint = new Color(0.78f, 0.69f, 0.52f, 0.5f),
                    rate = 30f,
                    life = 8f,
                    small = 0.015f,
                    large = 0.05f,
                    drift = new Vector2(-0.55f, -0.07f),
                    scatter = new Vector2(0.35f, 0.12f),
                    depth = 5f,
                }
            },
        };

        [MenuItem("Tools/Gulag Runners/Build the arena atmosphere")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            ParticleSystem already = ExistingAmbient();
            if (already != null)
            {
                Debug.Log($"{scene.name} already has something in the air — "
                          + $"\"{Name(already.transform)}\" loops and starts itself. Left "
                          + "alone: a second weather system over the first is two sets of "
                          + "motes at two speeds, which reads as a bug, not as more air.");
                return;
            }

            if (!Moods.TryGetValue(scene.name, out Air air))
            {
                Debug.LogWarning($"{scene.name} has empty air and no mood defined for it. "
                                 + "Add one to ArenaAtmosphereSetup.Moods, or drop a weather "
                                 + "prefab in by hand — this tool only knows about: "
                                 + string.Join(", ", Moods.Keys));
                return;
            }

            SideViewCamera rig = Object.FindFirstObjectByType<SideViewCamera>(
                FindObjectsInactive.Include);
            if (rig == null)
            {
                Debug.LogWarning($"{scene.name} has no SideViewCamera, so there is no way to "
                                 + "tell how big the arena is.");
                return;
            }

            Vector2 min = rig.bordersMin, max = rig.bordersMax;
            float planeZ = rig.planeZ;

            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == HolderName) Undo.DestroyObjectImmediate(root);

            GameObject holder = new GameObject(HolderName);
            Undo.RegisterCreatedObjectUndo(holder, "Build the arena atmosphere");

            GameObject go = new GameObject(air.name);
            go.transform.SetParent(holder.transform, false);
            go.transform.position = new Vector3((min.x + max.x) * 0.5f,
                                                (min.y + max.y) * 0.5f, planeZ);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;            // or the arena opens with clean air and fills up
            main.duration = air.life;
            main.startLifetime = air.life;
            main.startSize = new ParticleSystem.MinMaxCurve(air.small, air.large);
            main.startColor = air.tint;
            main.startSpeed = 0f;           // the drift below does all of it
            main.gravityModifier = 0f;
            main.maxParticles = Mathf.CeilToInt(air.rate * air.life * 1.3f);

            // World space. The box is nailed to the arena, so motes stay where the wind put
            // them instead of being towed about by a transform.
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = air.rate;

            // Sized from the arena, not from a constant: whatever the borders say is what
            // the air has to fill, and the borders are what the camera is clamped to.
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(max.x - min.x, max.y - min.y, air.depth);

            ParticleSystem.VelocityOverLifetimeModule flow = ps.velocityOverLifetime;
            flow.enabled = true;
            flow.space = ParticleSystemSimulationSpace.World;
            flow.x = Spread(air.drift.x, air.scatter.x);
            flow.y = Spread(air.drift.y, air.scatter.y);
            flow.z = Spread(0f, air.scatter.y * 0.5f);

            // In and out, so nothing ever pops into being or vanishes mid-air. A mote that
            // appears is the one thing that gives a particle system away.
            ParticleSystem.ColorOverLifetimeModule over = ps.colorOverLifetime;
            over.enabled = true;
            over.color = new ParticleSystem.MinMaxGradient(Breath());

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.material = MoteMaterial();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;

            EditorSceneManager.MarkSceneDirty(scene);

            int alive = Mathf.RoundToInt(air.rate * air.life);
            Debug.Log($"{air.name} built in {scene.name}: about {alive} motes alive over "
                      + $"{max.x - min.x:0.0} x {max.y - min.y:0.0} m, each {air.small * 100:0}"
                      + $"-{air.large * 100:0} cm. The camera sees roughly a third of the "
                      + "arena at a time, so a third of those are on screen.");
        }

        static string Name(Transform t) =>
            t.parent != null ? $"{t.parent.name}/{t.name}" : t.name;

        /// <summary>
        /// Anything in this scene that is already weather.
        ///
        /// Looping AND self-starting is the whole definition, and it is the right one here
        /// because of how this project builds its other effects: every impact, puff, scuff
        /// and gas vent has playOnAwake off and is fired from code when something happens.
        /// A system that starts on its own and never stops is, by construction, the air.
        /// </summary>
        static ParticleSystem ExistingAmbient()
        {
            foreach (ParticleSystem ps in
                     Object.FindObjectsByType<ParticleSystem>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ps.transform.root.name == HolderName) continue;   // something we built
                ParticleSystem.MainModule main = ps.main;
                if (main.loop && main.playOnAwake) return ps;
            }
            return null;
        }

        static ParticleSystem.MinMaxCurve Spread(float middle, float either) =>
            new ParticleSystem.MinMaxCurve(middle - either, middle + either);

        /// <summary>Alpha up over the first fifth of a life and down over the last third.</summary>
        static Gradient Breath()
        {
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.68f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        /// <summary>
        /// A mote: the soft disc the impact effects already use, blended ADDITIVELY.
        ///
        /// Additive rather than alpha because this is dust catching what light there is.
        /// Alpha-blended pale dust over a night arena reads as grey specks painted on the
        /// screen; additive makes them brighten only what they are in front of, so they
        /// disappear against the lit parts and show against the dark, which is what motes
        /// in air actually do.
        /// </summary>
        static Material MoteMaterial()
        {
            string path = $"{MaterialsDir}/GR_Air_Mote.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            mat = new Material(shader);

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{TexturesDir}/vfx_core.png");
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }

            // URP re-derives _SrcBlend and _DstBlend from _Blend when the material is
            // validated, so setting those by hand is overwritten. 2 is Additive; 1 is
            // Premultiply, which is what an earlier pass here set while meaning additive,
            // and the effect simply did not appear.
            mat.SetOverrideTag("RenderType", "Transparent");
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
