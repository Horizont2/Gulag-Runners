using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the hit effects and wires them to the fighters.
    ///
    /// Written as a tool rather than authored by hand because the numbers that make an impact
    /// read are numbers, not taste: how long a spark lives, how fast it leaves, how big it is
    /// against a body. That last one is the whole reason this cannot be copied out of a
    /// tutorial — this fighter is 0.91 m tall, so a spark authored for a 1.8 m human is twice
    /// the size it should be and a dust puff is a fog bank. Everything below is sized against
    /// the body, so it stays right if the body changes.
    ///
    /// What makes a blow feel expensive is not any one of these. It is four things landing on
    /// the same frame: the bodies stop (hit stop, in the simulation), the room moves (camera
    /// trauma), the victim flashes (FighterDamageView), and something leaves the point of
    /// contact (these). Three of the four were already there and had nothing to fire.
    ///
    /// Re-running it tunes the systems it made before rather than making more.
    /// </summary>
    public static class HitEffectSetup
    {
        const string Folder = "Assets/_Game/Effects";
        const string MaterialPath = Folder + "/Particle Soft.mat";
        const string TexturePath = Folder + "/Particle Soft.png";

        [MenuItem("Tools/Gulag Runners/Build hit effects")]
        public static void Build()
        {
            PlayerController[] fighters =
                Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,
                                                           FindObjectsSortMode.None);
            if (fighters.Length == 0)
            {
                Debug.LogWarning("No PlayerController in this scene.");
                return;
            }

            Material material = Material();
            System.Array.Sort(fighters, (a, b) => a.name.CompareTo(b.name));

            foreach (PlayerController fighter in fighters) Dress(fighter, material);

            UnityEditor.SceneManagement.EditorSceneManager
                       .MarkSceneDirty(fighters[0].gameObject.scene);
        }

        static void Dress(PlayerController fighter, Material material)
        {
            // Against the body, so every figure below is a share of a fighter rather than a
            // number in metres that goes wrong the day the fighter changes size.
            float h = Mathf.Max(0.2f, fighter.tuning.bodyHeight);

            Transform root = Child(fighter.transform, "Effects");
            Transform feet = Child(root, "At Feet");
            Transform body = Child(root, "At Body");
            feet.localPosition = new Vector3(0f, -fighter.visualYOffset, 0f);
            body.localPosition = new Vector3(0f, -fighter.visualYOffset + h * 0.55f, 0f);

            List<string> made = new List<string>();

            // ---- the moment of contact. Sparks read the metal, blood reads the body, and
            // both are needed: sparks alone look like a machine, blood alone looks soft.
            ParticleSystem sparks = System_(body, "Hit Sparks", material, made);
            Burst(sparks, 18, 0.16f, 0.30f, 3.2f, 6.4f, h * 0.030f, h * 0.004f, 1.2f,
                  new Color(1f, 0.95f, 0.75f), new Color(1f, 0.45f, 0.10f), 0.06f);

            ParticleSystem blood = System_(body, "Hit Blood", material, made);
            Burst(blood, 10, 0.32f, 0.48f, 0.8f, 2.2f, h * 0.070f, h * 0.025f, 0.7f,
                  new Color(0.55f, 0.07f, 0.06f), new Color(0.22f, 0.03f, 0.03f), h * 0.05f);

            // ---- the guard. Cooler, tighter, smaller: docs/02 wants a block to be legible
            // as LESS than a hit from across the arena.
            ParticleSystem guard = System_(body, "Block Sparks", material, made);
            Burst(guard, 10, 0.10f, 0.18f, 2.0f, 4.0f, h * 0.022f, h * 0.004f, 0.9f,
                  new Color(0.90f, 0.95f, 1f), new Color(0.55f, 0.70f, 1f), 0.04f);

            // ---- the parry. The biggest thing in the list, because it is the one moment the
            // game is allowed to stop and be looked at.
            ParticleSystem parry = System_(body, "Parry Burst", material, made);
            Burst(parry, 28, 0.22f, 0.40f, 4.0f, 8.5f, h * 0.040f, h * 0.006f, 0.5f,
                  new Color(1f, 0.96f, 0.70f), new Color(1f, 0.72f, 0.18f), 0.05f);

            // ---- the feet. docs/01 makes noise the only way to find the other player, so a
            // loud step has to LOOK loud as well as sound it.
            ParticleSystem quiet = System_(feet, "Step Dust Quiet", material, made);
            Burst(quiet, 3, 0.25f, 0.40f, 0.25f, 0.6f, h * 0.045f, h * 0.13f, 0.1f,
                  new Color(0.72f, 0.69f, 0.62f, 0.22f), new Color(0.72f, 0.69f, 0.62f, 0f),
                  h * 0.04f);

            ParticleSystem loud = System_(feet, "Step Dust Loud", material, made);
            Burst(loud, 8, 0.30f, 0.50f, 0.5f, 1.3f, h * 0.060f, h * 0.18f, 0.1f,
                  new Color(0.76f, 0.72f, 0.64f, 0.40f), new Color(0.76f, 0.72f, 0.64f, 0f),
                  h * 0.06f);

            ParticleSystem landSoft = System_(feet, "Land Dust Soft", material, made);
            Burst(landSoft, 8, 0.30f, 0.45f, 0.8f, 1.6f, h * 0.060f, h * 0.20f, 0.1f,
                  new Color(0.76f, 0.72f, 0.64f, 0.35f), new Color(0.76f, 0.72f, 0.64f, 0f),
                  h * 0.07f);

            ParticleSystem landHard = System_(feet, "Land Dust Hard", material, made);
            Burst(landHard, 18, 0.35f, 0.60f, 1.6f, 3.4f, h * 0.080f, h * 0.30f, 0.15f,
                  new Color(0.80f, 0.75f, 0.66f, 0.55f), new Color(0.80f, 0.75f, 0.66f, 0f),
                  h * 0.09f);

            ParticleSystem jumpPuff = System_(feet, "Jump Puff", material, made);
            Burst(jumpPuff, 6, 0.22f, 0.34f, 0.7f, 1.5f, h * 0.050f, h * 0.16f, 0.1f,
                  new Color(0.76f, 0.72f, 0.64f, 0.3f), new Color(0.76f, 0.72f, 0.64f, 0f),
                  h * 0.06f);

            ParticleSystem dodgePuff = System_(feet, "Dodge Puff", material, made);
            Burst(dodgePuff, 12, 0.25f, 0.40f, 1.4f, 3.0f, h * 0.055f, h * 0.20f, 0.1f,
                  new Color(0.82f, 0.80f, 0.74f, 0.4f), new Color(0.82f, 0.80f, 0.74f, 0f),
                  h * 0.07f);

            ParticleSystem died = System_(body, "Death Burst", material, made);
            Burst(died, 26, 0.45f, 0.80f, 1.2f, 3.2f, h * 0.090f, h * 0.030f, 0.9f,
                  new Color(0.45f, 0.06f, 0.05f), new Color(0.12f, 0.02f, 0.02f, 0f), h * 0.06f);

            // ---- the chest. Looping, because the point of this one is that it lasts as long
            // as the work does: it is the tell that gives the opener away.
            ParticleSystem working = System_(body, "Chest Working", material, made);
            Loop(working, 26f, 0.25f, 0.45f, 0.6f, 1.5f, h * 0.025f, h * 0.004f,
                 new Color(1f, 0.88f, 0.55f), new Color(1f, 0.52f, 0.12f, 0f), h * 0.05f);

            ParticleSystem opened = System_(body, "Chest Opened", material, made);
            Burst(opened, 16, 0.30f, 0.55f, 1.2f, 3.0f, h * 0.030f, h * 0.006f, 0.6f,
                  new Color(1f, 0.93f, 0.68f), new Color(1f, 0.70f, 0.20f, 0f), h * 0.06f);

            // ---- the trail. Cheapest thing here and the one that reads from furthest away.
            TrailRenderer trail = Trail(fighter, material, h);

            Wire(fighter, sparks, blood, guard, parry, quiet, loud, landSoft, landHard,
                 jumpPuff, dodgePuff, died, working, opened, trail, feet, body);

            Debug.Log($"{fighter.name}: {made.Count} effect(s) built under \"Effects\" and " +
                      $"wired.\n  " + string.Join(", ", made) + "\n" +
                      "Everything is sized against this fighter's body height " +
                      $"({h:0.00} m), so it stays right if the body changes. The material is " +
                      "alpha blended; for the sparks and the parry, switching it to additive " +
                      "in the material's Surface Options is the one change that buys the most.",
                      fighter);
        }

        /// <summary>Hands every system and transform to the two views that fire them.</summary>
        static void Wire(PlayerController fighter,
                         ParticleSystem sparks, ParticleSystem blood, ParticleSystem guard,
                         ParticleSystem parry, ParticleSystem quiet, ParticleSystem loud,
                         ParticleSystem landSoft, ParticleSystem landHard,
                         ParticleSystem jumpPuff, ParticleSystem dodgePuff,
                         ParticleSystem died, ParticleSystem working, ParticleSystem opened,
                         TrailRenderer trail, Transform feet, Transform body)
        {
            PlayerEffects fx = fighter.GetComponent<PlayerEffects>();
            if (fx == null) fx = Undo.AddComponent<PlayerEffects>(fighter.gameObject);

            Undo.RecordObject(fx, "Build hit effects");
            fx.player = fighter;
            fx.feet = feet;
            fx.body = body;
            fx.swingTrail = trail;
            if (trail != null) fx.weaponTip = trail.transform;

            fx.stepQuiet.particles = quiet;
            fx.stepLoud.particles = loud;
            fx.landSoft.particles = landSoft;
            fx.landHard.particles = landHard;
            fx.jump.particles = jumpPuff;
            fx.dodge.particles = dodgePuff;
            fx.hurt.particles = sparks;
            fx.parried.particles = parry;
            fx.death.particles = died;

            // blocked is deliberately NOT wired here. Both views answer a block, and both
            // firing the same system would double it — two bursts on one frame, which reads
            // as a bigger event than being hit. The damage view keeps it, because it scales
            // the burst by the chip that got through and this component cannot.
            fx.chestWorking.particles = working;
            fx.chestOpened.particles = opened;
            PrefabUtility.RecordPrefabInstancePropertyModifications(fx);

            // The blood goes on the damage view instead, which sizes it by the damage that
            // got through — a thing PlayerEffects has no business knowing.
            FighterDamageView wounds = fighter.GetComponentInChildren<FighterDamageView>(true);
            if (wounds == null) return;

            // Only the two that scale with the damage. deathBurst stays empty for the same
            // reason blocked does above: PlayerEffects already fires it, and a death that
            // bursts twice is a death that looks like a bug.
            Undo.RecordObject(wounds, "Build hit effects");
            wounds.bloodOnHit = blood;
            wounds.sparksOnBlock = guard;
            PrefabUtility.RecordPrefabInstancePropertyModifications(wounds);
        }

        // ------------------------------------------------------------------ the systems

        static ParticleSystem System_(Transform parent, string name, Material material,
                                      List<string> made)
        {
            Transform found = parent.Find(name);
            GameObject go;

            if (found != null) go = found.gameObject;
            else
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Build hit effects");
                go.transform.SetParent(parent, false);
            }

            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = go.AddComponent<ParticleSystem>();

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            if (r != null)
            {
                r.sharedMaterial = material;
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.alignment = ParticleSystemRenderSpace.View;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sortMode = ParticleSystemSortMode.None;
            }

            made.Add(name);
            return ps;
        }

        /// <summary>Common shape: nothing plays by itself, nothing loops, nothing collides.</summary>
        static void Base(ParticleSystem ps, float lifeMin, float lifeMax,
                         float speedMin, float speedMax, float sizeStart, float sizeEnd,
                         float gravity, Color from, Color to, float radius, bool looping)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = looping;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = sizeStart;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 200;
            main.duration = Mathf.Max(0.3f, lifeMax);

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.001f, radius);

            // Size down to nothing over the life: a particle that vanishes at full size
            // reads as a dropped frame.
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            float ratio = sizeStart > 0.0001f ? Mathf.Clamp01(sizeEnd / sizeStart) : 0f;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, ratio)));

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[]
                {
                    new GradientAlphaKey(from.a, 0f),
                    new GradientAlphaKey(from.a * 0.85f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystem.CollisionModule none = ps.collision;
            none.enabled = false;
            ParticleSystem.LightsModule lights = ps.lights;
            lights.enabled = false;
        }

        static void Burst(ParticleSystem ps, int count, float lifeMin, float lifeMax,
                          float speedMin, float speedMax, float sizeStart, float sizeEnd,
                          float gravity, Color from, Color to, float radius)
        {
            Base(ps, lifeMin, lifeMax, speedMin, speedMax, sizeStart, sizeEnd, gravity,
                 from, to, radius, false);

            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = true;
            e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        static void Loop(ParticleSystem ps, float rate, float lifeMin, float lifeMax,
                         float speedMin, float speedMax, float sizeStart, float sizeEnd,
                         Color from, Color to, float radius)
        {
            Base(ps, lifeMin, lifeMax, speedMin, speedMax, sizeStart, sizeEnd, 0.2f,
                 from, to, radius, true);

            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = true;
            e.SetBursts(new ParticleSystem.Burst[0]);
            e.rateOverTime = rate;
        }

        /// <summary>The swing trail, on the weapon socket so it follows whatever is held.</summary>
        static TrailRenderer Trail(PlayerController fighter, Material material, float h)
        {
            WeaponSocket socket = fighter.GetComponentInChildren<WeaponSocket>(true);
            Transform parent = socket != null ? socket.transform : fighter.transform;

            Transform found = parent.Find("Swing Trail");
            GameObject go;
            if (found != null) go = found.gameObject;
            else
            {
                go = new GameObject("Swing Trail");
                Undo.RegisterCreatedObjectUndo(go, "Build hit effects");
                go.transform.SetParent(parent, false);
            }

            // Out at the end of the weapon rather than in the fist: the arc the eye follows is
            // the one the blade draws.
            go.transform.localPosition = new Vector3(0f, 0f, h * 0.9f);

            TrailRenderer t = go.GetComponent<TrailRenderer>();
            if (t == null) t = go.AddComponent<TrailRenderer>();

            t.sharedMaterial = material;
            t.time = 0.14f;
            t.widthCurve = new AnimationCurve(new Keyframe(0f, h * 0.10f),
                                              new Keyframe(1f, 0f));
            t.minVertexDistance = h * 0.02f;
            t.autodestruct = false;
            t.emitting = false;
            t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t.receiveShadows = false;

            Gradient g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.97f, 0.88f), 0f),
                    new GradientColorKey(new Color(0.75f, 0.80f, 0.95f), 1f)
                },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            t.colorGradient = g;

            return t;
        }

        // ------------------------------------------------------------------ the material

        /// <summary>
        /// One URP particle material and a soft dot to go on it.
        ///
        /// Generated rather than imported because a square particle is the difference between
        /// "effects" and "coloured squares", and the project has no particle texture. A radial
        /// falloff is eight lines of arithmetic; finding one that is licensed is a shopping
        /// trip.
        /// </summary>
        static Material Material()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Game", "Effects");

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (tex == null)
            {
                const int n = 64;
                Texture2D made = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // Smooth to zero at the rim, and squared so the core stays bright: a
                    // linear falloff reads as a ring rather than as a point of light.
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    made.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                made.Apply();

                File.WriteAllBytes(TexturePath, made.EncodeToPNG());
                Object.DestroyImmediate(made);
                AssetDatabase.ImportAsset(TexturePath);

                TextureImporter importer =
                    AssetImporter.GetAtPath(TexturePath) as TextureImporter;
                if (importer != null)
                {
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                {
                    Debug.LogWarning("No URP particle shader found, so the effects will use " +
                                     "whatever Unity gives them — probably magenta. Assign a " +
                                     "particle material to Assets/_Game/Effects by hand.");
                    shader = Shader.Find("Sprites/Default");
                }

                material = new Material(shader) { name = "Particle Soft" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            if (tex != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);
            }
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);  // transparent
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            return material;
        }

        static Transform Child(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null) return found;

            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Build hit effects");
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
