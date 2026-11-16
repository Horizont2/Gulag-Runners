using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the impact effects, and wires them to the fighters.
    ///
    /// The three slots that matter were EMPTY. <see cref="FighterDamageView"/> has had
    /// `bloodOnHit`, `sparksOnBlock` and `deathBurst` since it was written, and in every
    /// arena all three were null -- so landing a blow produced a material flash and nothing
    /// else, a block produced a flash, and dying produced a flash. The pack the project owns
    /// (Cartoon FX Remaster, 65 prefabs) had three of them used.
    ///
    /// These are authored rather than taken from the pack for one reason: a bought effect
    /// does not know the palette. docs/35 gives the game one accent per biome and docs/00
    /// asks for flat colour and hard edges; a pack's impact arrives with its own hue, its
    /// own softness and usually its own bloom, and the result reads as somebody else's game
    /// happening on top of this one. Everything below takes its colour from
    /// <see cref="UiPalette"/>, so changing the palette changes the hits with it.
    ///
    /// The shape of the effect was designed on a filmstrip before any of it was built --
    /// `docs/refs/vfx/preview.html` renders the same numbers at 0 to 300 ms. That is not a
    /// nicety: the first version had a ring bigger than the fighter, sparks radiating in a
    /// full starburst instead of spraying along the blade, and a 420 ms tail on a sword
    /// blow. Three rounds of looking at it made it decent. The numbers in this file are the
    /// third version.
    ///
    /// Five layers, each a job:
    ///
    ///   SLASH   one hard arc at contact, 75 ms. A stylised hit is a SHAPE first.
    ///   CORE    the white-hot centre, 70 ms. Gone fastest of anything here.
    ///   RING    a thin shell, starting a beat late so it reads as a consequence. Says force.
    ///   SPARKS  nine, in a CONE along the blade. Not a starburst -- a starburst has no
    ///           direction and a hit is the most directional thing in the game.
    ///   BITS    four, falling. Without them the effect looks cut off rather than finished.
    ///
    /// Everything is unlit and additive (docs/06), one draw call per layer, and nothing here
    /// is lit or shadowed.
    /// </summary>
    public static class HitEffectBuilder
    {
        const string EffectsDir = "Assets/_Game/Effects";
        const string TexturesDir = EffectsDir + "/Textures";
        const string MaterialsDir = EffectsDir + "/Materials";

        // The fighter is about 1.8 m. Every size below is in metres and was read off the
        // preview, where the body block was 1.8 m tall and the effect was scaled to it.
        const float Px = 1.35f;

        [MenuItem("Tools/Gulag Runners/Build the hit effects")]
        public static void Run()
        {
            if (!Directory.Exists(TexturesDir))
            {
                Debug.LogError($"No {TexturesDir}. Run `cd docs/refs && python3 "
                               + "make_vfx_textures.py` first — the textures are drawn in "
                               + "code and are not in git as binaries nobody can regenerate.");
                return;
            }

            Directory.CreateDirectory(MaterialsDir);

            // No block effect. docs/31 took the guard out and made the weapon the
            // defence; a blocked hit is not an event the fight raises any more.
            GameObject hit = Build("GR_Hit_Flesh", UiPalette.Attack, 1f);
            GameObject armour = Build("GR_Hit_Armour", new Color(1f, 0.86f, 0.55f), 0.85f);
            GameObject death = Build("GR_Death", UiPalette.Attack, 1.7f);

            int wired = Wire(hit, death);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built 3 impact effects in {EffectsDir} and wired {wired} fighter(s).\n"
                      + "Flesh, armour and death are the same five layers in "
                      + "different colours and sizes — one recipe, so they cannot drift "
                      + "apart, and the armour hit is pale and metallic because a player "
                      + "has to be able to tell at a glance that the other man found a "
                      + "breastplate.\n\n"
                      + "Nothing is lit, nothing writes depth, and each layer is one draw "
                      + "call.");
        }

        // ----------------------------------------------------------------- the effect

        /// <summary>
        /// One impact, as a prefab: an empty parent with five particle systems under it.
        ///
        /// A parent rather than one system with five sub-emitters, because each layer wants
        /// its own material and its own render mode -- the sparks are stretched billboards
        /// and the ring is not -- and a sub-emitter cannot have either.
        /// </summary>
        static GameObject Build(string name, Color accent, float scale)
        {
            GameObject root = new GameObject(name);

            // A conductor on the root: an emitter that draws nothing and emits nothing,
            // whose only job is to be the one reference somebody holds. Play() on it plays
            // its children, so FighterDamageView needs one field and not five, and the
            // effect can gain or lose a layer without anything outside being rewired.
            ParticleSystem conductor = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule lead = conductor.main;
            lead.duration = 0.5f;
            lead.loop = false;
            lead.playOnAwake = false;
            lead.maxParticles = 1;
            ParticleSystem.EmissionModule silent = conductor.emission;
            silent.enabled = false;
            conductor.GetComponent<ParticleSystemRenderer>().enabled = false;

            Slash(root, accent, scale);
            Core(root, scale);
            Ring(root, scale);
            Sparks(root, accent, scale);
            Bits(root, accent, scale);

            string path = $"{EffectsDir}/{name}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>
        /// The arc of the blade. One particle, one job, and the only layer with a rotation
        /// that means anything.
        /// </summary>
        static void Slash(GameObject parent, Color accent, float scale)
        {
            ParticleSystem ps = Layer(parent, "Slash", "vfx_slash", 1);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.075f;
            main.startSize = 1.00f * Px * scale;
            main.startSpeed = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            main.startColor = Color.white;

            // White at the instant of contact, the accent as it goes. Iron is white-hot for
            // a frame and then it is just the colour of the game.
            Over(ps, Ramp(Color.white, accent, 0.35f), Fade(0.75f));
            Size(ps, Grow(1.0f, 1.48f, 0.38f));
        }

        /// <summary>The hot centre. Smallest, brightest, shortest.</summary>
        static void Core(GameObject parent, float scale)
        {
            ParticleSystem ps = Layer(parent, "Core", "vfx_core", 1);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.07f;
            main.startSize = 0.21f * Px * scale;
            main.startSpeed = 0f;
            main.startColor = new Color(1f, 0.925f, 0.84f);

            Over(ps, Ramp(new Color(1f, 0.95f, 0.88f), new Color(1f, 0.85f, 0.7f), 0.5f),
                 Fade(1.5f));
            Size(ps, Grow(1.0f, 2.3f, 0.5f));
        }

        /// <summary>
        /// The shell. Late by a frame, small, thin, and gone before the sparks are.
        ///
        /// Small on purpose. The first version reached 2.3 times this and by 150 ms it was a
        /// circle wider than the fighter, hanging in the air: it read as a spell. A ring
        /// that says FORCE has to be gone before you have finished noticing it.
        /// </summary>
        static void Ring(GameObject parent, float scale)
        {
            ParticleSystem ps = Layer(parent, "Ring", "vfx_ring", 1);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.10f;
            main.startSize = 0.20f * Px * scale;
            main.startSpeed = 0f;
            main.startDelay = 0.012f;
            main.startColor = new Color(1f, 0.85f, 0.63f, 0.55f);

            Size(ps, Grow(1.0f, 5.5f, 0.5f));
            Over(ps, Ramp(new Color(1f, 0.85f, 0.63f), new Color(1f, 0.75f, 0.5f), 0.5f),
                 Fade(1.4f));
        }

        /// <summary>
        /// Nine sparks in a cone along the blade.
        ///
        /// A cone and not a sphere. A starburst is what a particle system does when nobody
        /// has told it where the blow came from, and it reads as an explosion rather than as
        /// a strike -- it is the single clearest sign of an effect that was not authored.
        ///
        /// Stretched by velocity, so each one points where it is going, and damped, so they
        /// leap out and settle rather than coasting at one speed. Nine, not ninety: a few
        /// readable streaks beat a cloud at the size a phone draws this.
        /// </summary>
        static void Sparks(GameObject parent, Color accent, float scale)
        {
            ParticleSystem ps = Layer(parent, "Sparks", "vfx_spark", 9);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f * Px * scale,
                                                            0.13f * Px * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 9.5f);
            main.startColor = new Color(1f, 0.75f, 0.33f);
            main.gravityModifier = 0.35f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;                 // +-30 degrees: a spray, still a direction
            shape.radius = 0.03f;

            // Out fast, then settling. This is what makes them read as thrown rather than
            // as fired.
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.14f;
            drag.limit = new ParticleSystem.MinMaxCurve(1.2f);

            Over(ps, Ramp(new Color(1f, 0.75f, 0.33f), accent, 0.55f), Fade(0.7f));
            Size(ps, Shrink(1.0f, 0.25f));

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.055f;
            r.lengthScale = 2.6f;
        }

        /// <summary>
        /// Four pieces that fall. The follow-through.
        ///
        /// Cheap and almost unnoticed, and the effect looks amputated without them: every
        /// other layer is gone by 110 ms, and an impact that stops dead reads as a dropped
        /// frame rather than as an ending.
        /// </summary>
        static void Bits(GameObject parent, Color accent, float scale)
        {
            ParticleSystem ps = Layer(parent, "Bits", "vfx_spark", 4);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.26f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f * Px * scale,
                                                            0.09f * Px * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.0f);
            main.startColor = accent;
            main.gravityModifier = 1.6f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 52f;
            shape.radius = 0.05f;

            Over(ps, Ramp(accent, accent * 0.7f, 0.6f), Fade(0.9f));
            Size(ps, Shrink(1.0f, 0.55f));

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 2.0f;
        }

        // ----------------------------------------------------------------- the plumbing

        /// <summary>
        /// One particle system, set up the way every layer here wants it: a single burst,
        /// not looping, not playing on its own, in world space, unlit and additive.
        /// </summary>
        static ParticleSystem Layer(GameObject parent, string name, string texture, int count)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Max(count, 16);

            // World space, so a fighter who is knocked back does not drag his own impact
            // along with him. The hit happened where it happened.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.material = Material(texture);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = -5f;              // in front of the fighter it is landing on

            return ps;
        }

        /// <summary>
        /// An additive unlit material for one texture, made once and reused.
        ///
        /// The blend states are set explicitly rather than through the shader's inspector
        /// keywords. Those keywords are bookkeeping the GUI does, and a material built from
        /// a script that only sets _Surface and _Blend comes out opaque often enough that
        /// it is not worth finding out which Unity version does which.
        /// </summary>
        static UnityEngine.Material Material(string texture)
        {
            string path = $"{MaterialsDir}/{texture}_Additive.mat";
            UnityEngine.Material existing =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            UnityEngine.Material mat = new UnityEngine.Material(shader);

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{TexturesDir}/{texture}.png");
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }

            mat.SetOverrideTag("RenderType", "Transparent");
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void Over(ParticleSystem ps, Gradient gradient, AnimationCurve alpha)
        {
            // The alpha goes on the gradient's own alpha keys: colourOverLifetime takes one
            // gradient, and a separate curve would need a second module that does not exist.
            GradientAlphaKey[] keys = new GradientAlphaKey[alpha.keys.Length];
            for (int i = 0; i < alpha.keys.Length; i++)
                keys[i] = new GradientAlphaKey(alpha.keys[i].value, alpha.keys[i].time);
            gradient.SetKeys(gradient.colorKeys, keys);

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        static void Size(ParticleSystem ps, AnimationCurve curve)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        static Gradient Ramp(Color from, Color to, float at)
        {
            Gradient g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(from, 0f),
                    new GradientColorKey(to, Mathf.Clamp01(at)),
                    new GradientColorKey(to, 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>Alpha falling off with a power, which is what `pow(1-k, p)` was in the preview.</summary>
        static AnimationCurve Fade(float power)
        {
            Keyframe[] keys = new Keyframe[6];
            for (int i = 0; i < keys.Length; i++)
            {
                float t = i / (float)(keys.Length - 1);
                keys[i] = new Keyframe(t, Mathf.Pow(1f - t, power));
            }
            return Smooth(keys);
        }

        static AnimationCurve Grow(float from, float to, float power)
        {
            Keyframe[] keys = new Keyframe[6];
            for (int i = 0; i < keys.Length; i++)
            {
                float t = i / (float)(keys.Length - 1);
                keys[i] = new Keyframe(t, Mathf.Lerp(from, to, Mathf.Pow(t, power)));
            }
            return Smooth(keys);
        }

        static AnimationCurve Shrink(float from, float to) => Grow(from, to, 1f);

        static AnimationCurve Smooth(Keyframe[] keys)
        {
            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        // ----------------------------------------------------------------- wiring

        /// <summary>
        /// Hangs an instance of each effect on every fighter in the scene and fills the
        /// three slots that have been empty since FighterDamageView was written.
        ///
        /// On the BODY transform, which PlayerEffects already defines as "where a hit lands
        /// on this body — roughly the chest". That is the same point the sound is posted
        /// from, so the flash, the particles and the noise all agree about where the player
        /// was struck.
        /// </summary>
        static int Wire(GameObject hit, GameObject death)
        {
            int wired = 0;

            foreach (FighterDamageView view in Object.FindObjectsByType<FighterDamageView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (view == null) continue;

                PlayerEffects effects = view.GetComponentInParent<PlayerEffects>();
                Transform at = effects != null && effects.body != null
                    ? effects.body : view.transform;

                Undo.RecordObject(view, "Build the hit effects");
                view.bloodOnHit = Hang(hit, at, "Hit");
                view.deathBurst = Hang(death, at, "Death");
                EditorUtility.SetDirty(view);
                wired++;
            }

            return wired;
        }

        /// <summary>
        /// One instance under the fighter, replacing any from a previous run so that running
        /// the tool twice does not leave a pile of dead copies on every body.
        /// </summary>
        static ParticleSystem Hang(GameObject prefab, Transform at, string name)
        {
            Transform old = at.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            GameObject made = (GameObject)PrefabUtility.InstantiatePrefab(prefab, at);
            made.name = name;
            made.transform.localPosition = Vector3.zero;
            made.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(made, "Build the hit effects");

            // The conductor, built onto the prefab's root. It is what gets handed to
            // FighterDamageView, and Play() on it starts all five layers.
            return made.GetComponent<ParticleSystem>();
        }
    }
}
