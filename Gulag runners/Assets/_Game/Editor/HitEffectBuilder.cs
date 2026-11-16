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
            GameObject parry = Parry();
            GameObject clash = Clash();
            GameObject pickup = Pickup();
            GameObject scuff = Dust("GR_Dust_Scuff", 0.55f, 5, 3);
            Vent();     // saved as a prefab; PoisonWarningSetup hangs it on the pool

            int wired = Wire(hit, death, parry, clash, pickup, scuff);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built 8 effects in {EffectsDir} and wired {wired} fighter(s).\n"
                      + "Flesh, armour and death are the same five layers in "
                      + "different colours and sizes — one recipe, so they cannot drift "
                      + "apart, and the armour hit is pale and metallic because a player "
                      + "has to be able to tell at a glance that the other man found a "
                      + "breastplate.\n\n"
                      + "Nothing is lit, nothing writes depth, and each layer is one draw "
                      + "call.");
        }

        /// <summary>
        /// The parry: the best moment in the game, and the only one built differently.
        ///
        /// docs/02 makes the parry the skill ceiling and the camera already agrees -- 0.8
        /// trauma against 0.22 for landing an ordinary blow, the biggest shake in the list.
        /// This is the picture that goes with it.
        ///
        /// Three things make it not a hit in another colour:
        ///
        ///   NO SLASH. Nothing was cut. The arc is the signature of a blade going THROUGH
        ///   something, and a parry is the opposite event.
        ///
        ///   TWO FANS. Sparks go the way the blade was travelling AND back the other way,
        ///   because two weapons refused each other. One fan would read as an ordinary hit
        ///   that happened to be yellow, which is exactly what this must not look like.
        ///
        ///   THE CORE HOLDS. Every other impact here is gone inside 70 ms. This one sits
        ///   for 115, because the hit stop behind it is the longest in the game and the
        ///   point of that pause is that everybody gets to see what just happened.
        ///
        /// Gold, not red, for the same reason: no damage was done. Red would say somebody
        /// was hurt, and the whole reward of a parry is that nobody was.
        /// </summary>
        static GameObject Parry()
        {
            Color gold = new Color(1f, 0.84f, 0.47f);
            GameObject root = new GameObject("GR_Parry");

            ParticleSystem conductor = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule lead = conductor.main;
            lead.duration = 0.5f; lead.loop = false; lead.playOnAwake = false;
            lead.maxParticles = 1;
            ParticleSystem.EmissionModule silent = conductor.emission;
            silent.enabled = false;
            conductor.GetComponent<ParticleSystemRenderer>().enabled = false;

            // The core, held.
            ParticleSystem core = Layer(root, "Core", "vfx_core", 1);
            ParticleSystem.MainModule cm = core.main;
            cm.startLifetime = 0.115f;
            cm.startSize = 0.21f * Px;
            cm.startSpeed = 0f;
            cm.startColor = new Color(1f, 0.99f, 0.93f);
            Over(core, Ramp(new Color(1f, 0.99f, 0.93f), gold, 0.6f), Fade(0.9f));
            Size(core, Grow(1.0f, 3.5f, 0.35f));

            // The shell: faster and harder than a hit's, because two edges stopped dead.
            ParticleSystem ring = Layer(root, "Ring", "vfx_ring", 1);
            ParticleSystem.MainModule rm = ring.main;
            rm.startLifetime = 0.085f;
            rm.startSize = 0.18f * Px;
            rm.startSpeed = 0f;
            rm.startDelay = 0.006f;
            rm.startColor = new Color(1f, 0.93f, 0.77f, 0.9f);
            Size(ring, Grow(1.0f, 8f, 0.42f));
            Over(ring, Ramp(new Color(1f, 0.93f, 0.77f), gold, 0.5f), Fade(1.2f));

            // Two fans, one each way. Seven sparks a side: enough to read as a spray, few
            // enough that each one is a streak rather than a cloud.
            Fan(root, "Sparks Out", 0f, gold);
            Fan(root, "Sparks Back", 180f, gold);

            string path = $"{EffectsDir}/GR_Parry.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>One side of the parry's spray, turned to face where it goes.</summary>
        static void Fan(GameObject parent, string name, float yaw, Color gold)
        {
            ParticleSystem ps = Layer(parent, name, "vfx_spark", 7);
            ps.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.19f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f * Px, 0.12f * Px);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 12f);
            main.startColor = new Color(1f, 0.96f, 0.84f);
            main.gravityModifier = 0.5f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 24f;
            shape.radius = 0.03f;

            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.12f;
            drag.limit = new ParticleSystem.MinMaxCurve(1.4f);

            Over(ps, Ramp(new Color(1f, 0.96f, 0.84f), gold, 0.5f), Fade(0.7f));
            Size(ps, Shrink(1.0f, 0.2f));

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 2.8f;
        }

        /// <summary>
        /// Dust. The most Worms-like thing in this file and the cheapest.
        ///
        /// In that game everything puffs: a landing, a scuff, a thing dropped on the floor.
        /// It is not an effect anybody notices, which is the point -- what gets noticed is
        /// its absence, as a character who moves through the world without disturbing any
        /// of it. A fighter hauling himself over a ledge in silence and stillness reads as
        /// a sprite sliding on a track.
        ///
        /// Not additive, unlike everything else here. Dust OCCLUDES: it is air with
        /// something in it, and an additive puff glows like steam lit from inside.
        ///
        /// The spread is continuous rather than alternating sides. A first version pushed
        /// every puff hard left or hard right and left a hole in the middle where the
        /// fighter stands, which read as two separate puffs instead of one cloud opening
        /// out from under him.
        /// </summary>
        static GameObject Dust(string name, float scale, int puffs, int grit)
        {
            GameObject root = Conductor(name);

            ParticleSystem cloud = Layer(root, "Cloud", "vfx_puff", puffs);
            ParticleSystem.MainModule main = cloud.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.34f, 0.60f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.26f * Px * scale,
                                                            0.56f * Px * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * scale, 2.2f * scale);
            main.startColor = new Color(0.77f, 0.70f, 0.60f, 0.62f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-3.14f, 3.14f);
            main.gravityModifier = -0.04f;              // dust rises, barely

            ParticleSystem.ShapeModule shape = cloud.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 82f;                          // wide and flat: it runs along the ground
            shape.radius = 0.12f * scale;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.LimitVelocityOverLifetimeModule drag =
                cloud.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.22f;
            drag.limit = new ParticleSystem.MinMaxCurve(0.6f);

            ParticleSystem.RotationOverLifetimeModule spin = cloud.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-1.1f, 1.1f);

            Over(cloud, Ramp(new Color(0.77f, 0.70f, 0.60f),
                             new Color(0.66f, 0.60f, 0.52f), 0.6f), Rise(1.25f));
            Size(cloud, Grow(1.0f, 1.9f, 0.45f));
            Soft(cloud);

            if (grit > 0)
            {
                // A few hard bits. This is what says WEIGHT rather than air, and five of
                // them do it -- the dust is the picture, the grit is the punctuation.
                ParticleSystem bits = Layer(root, "Grit", "vfx_spark", grit);
                ParticleSystem.MainModule gm = bits.main;
                gm.startLifetime = new ParticleSystem.MinMaxCurve(0.17f, 0.29f);
                gm.startSize = new ParticleSystem.MinMaxCurve(0.04f * Px * scale,
                                                              0.08f * Px * scale);
                gm.startSpeed = new ParticleSystem.MinMaxCurve(2.4f * scale, 4.6f * scale);
                gm.startColor = new Color(0.84f, 0.77f, 0.64f);
                gm.gravityModifier = 1.5f;

                ParticleSystem.ShapeModule gs = bits.shape;
                gs.enabled = true;
                gs.shapeType = ParticleSystemShapeType.Cone;
                gs.angle = 62f;
                gs.radius = 0.06f * scale;
                gs.rotation = new Vector3(-90f, 0f, 0f);

                Over(bits, Ramp(new Color(0.84f, 0.77f, 0.64f),
                                new Color(0.62f, 0.56f, 0.46f), 0.6f), Fade(0.8f));
                Size(bits, Shrink(1.0f, 0.4f));

                ParticleSystemRenderer r = bits.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.03f;
                r.lengthScale = 2.0f;
            }

            return Save(root, name);
        }

        /// <summary>
        /// A clash: your swing was turned aside. The parry seen from the losing end.
        ///
        /// Deliberately the parry's sparks with nothing else -- no held core, no shell. The
        /// man who parried gets the moment; the man who was parried gets told, and told in
        /// the same colour so he knows WHAT happened to him rather than just that something
        /// did.
        /// </summary>
        static GameObject Clash()
        {
            Color gold = new Color(1f, 0.84f, 0.47f);
            GameObject root = Conductor("GR_Clash");
            Fan(root, "Sparks", 0f, gold);
            Fan(root, "Sparks Back", 180f, gold);
            return Save(root, "GR_Clash");
        }

        /// <summary>
        /// Something picked up. docs/03 makes the loot the payoff for the time and the noise
        /// a chest costs, and a payoff you cannot see is a payoff nobody feels.
        ///
        /// Upward, which nothing else here is. Every impact in this file throws outward from
        /// a point; this gathers and lifts, because that is the shape of a thing being
        /// taken rather than a thing being done to somebody.
        /// </summary>
        static GameObject Pickup()
        {
            Color gold = UiPalette.Amber;
            GameObject root = Conductor("GR_Pickup");

            ParticleSystem core = Layer(root, "Flash", "vfx_core", 1);
            ParticleSystem.MainModule cm = core.main;
            cm.startLifetime = 0.16f;
            cm.startSize = 0.14f * Px;
            cm.startSpeed = 0f;
            cm.startColor = new Color(1f, 0.95f, 0.82f);
            Over(core, Ramp(new Color(1f, 0.95f, 0.82f), gold, 0.5f), Fade(1.1f));
            Size(core, Grow(1.0f, 2.2f, 0.4f));

            ParticleSystem motes = Layer(root, "Motes", "vfx_core", 6);
            ParticleSystem.MainModule mm = motes.main;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(0.26f, 0.44f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.03f * Px, 0.06f * Px);
            mm.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.9f);
            mm.startColor = gold;
            mm.gravityModifier = -0.22f;                // they LIFT

            ParticleSystem.ShapeModule shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 26f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);  // up, not out

            Over(motes, Ramp(gold, new Color(1f, 0.74f, 0.33f), 0.5f), Fade(0.9f));
            Size(motes, Shrink(1.0f, 0.25f));

            return Save(root, "GR_Pickup");
        }

        /// <summary>The silent emitter on a prefab's root; Play() on it plays the layers.</summary>
        static GameObject Conductor(string name)
        {
            GameObject root = new GameObject(name);
            ParticleSystem lead = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = lead.main;
            main.duration = 0.8f;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 1;
            ParticleSystem.EmissionModule silent = lead.emission;
            silent.enabled = false;
            lead.GetComponent<ParticleSystemRenderer>().enabled = false;
            return root;
        }

        static GameObject Save(GameObject root, string name)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, $"{EffectsDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>Alpha that comes UP and then goes, for something that billows.</summary>
        static AnimationCurve Rise(float power)
        {
            Keyframe[] keys = new Keyframe[7];
            for (int i = 0; i < keys.Length; i++)
            {
                float t = i / (float)(keys.Length - 1);
                float up = Mathf.Min(1f, t / 0.18f);
                keys[i] = new Keyframe(t, up * Mathf.Pow(1f - t, power));
            }
            return Smooth(keys);
        }

        /// <summary>Alpha-blended instead of additive. Dust occludes; it does not glow.</summary>
        static void Soft(ParticleSystem ps)
        {
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            UnityEngine.Material mat = new UnityEngine.Material(r.sharedMaterial);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            string path = $"{MaterialsDir}/vfx_puff_Soft.mat";
            UnityEngine.Material existing =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mat, path);
                existing = mat;
            }
            r.material = existing;
        }

        /// <summary>
        /// One vent: the gas arriving, from under the floor.
        ///
        /// docs/33 is explicit that it ARRIVES and does not fade in -- "the green arrives at
        /// the bottom of the arena over about a third of a second, not faded in over three,
        /// which reads as a bug rather than a release". So this is a valve letting go: a
        /// jet out of the floor, a thin push of air along it, and then the gas losing its
        /// shove and spreading into a cloud.
        ///
        /// Three things it is NOT, each of which the filmstrip caught:
        ///
        ///   Not a lamp. The first version flashed a round blob at each mouth and it read
        ///   as a glowing egg sitting on the ground. A jet leaving the floor is tall, thin,
        ///   and grows upward.
        ///
        ///   Not a puddle. The shell along the floor was a fat ellipse and read as light
        ///   lying on the stone rather than as air being pushed sideways. Thin and quick.
        ///
        ///   Not smoke. It is PoisonPool's own green at the top of its climb, so the burst
        ///   and the flood that follows are the same substance. A grey puff followed by a
        ///   green pool is two events.
        /// </summary>
        static GameObject Vent()
        {
            GameObject root = Conductor("GR_GasVent");

            // The jet: nine puffs up a narrow cone, losing their push as they go.
            ParticleSystem jet = Layer(root, "Jet", "vfx_puff", 9);
            ParticleSystem.MainModule jm = jet.main;
            jm.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.58f);
            jm.startSize = new ParticleSystem.MinMaxCurve(0.16f * Px, 0.34f * Px);
            jm.startSpeed = new ParticleSystem.MinMaxCurve(3.6f, 6.2f);
            jm.startColor = new Color(0.72f, 0.96f, 0.68f, 0.5f);
            jm.startRotation = new ParticleSystem.MinMaxCurve(-3.14f, 3.14f);
            jm.gravityModifier = -0.05f;

            ParticleSystem.ShapeModule js = jet.shape;
            js.enabled = true;
            js.shapeType = ParticleSystemShapeType.Cone;
            js.angle = 14f;                      // narrow going up, it spreads by drag
            js.radius = 0.08f;
            js.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.LimitVelocityOverLifetimeModule drag =
                jet.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.3f;
            drag.limit = new ParticleSystem.MinMaxCurve(0.9f);

            ParticleSystem.RotationOverLifetimeModule spin = jet.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

            Over(jet, Ramp(new Color(0.88f, 1f, 0.84f), new Color(0.47f, 0.86f, 0.45f), 0.45f),
                 Rise(1.2f));
            Size(jet, Grow(1.0f, 2.6f, 0.5f));
            Soft(jet);

            // The mouth: one hard vertical flash as the valve opens.
            ParticleSystem mouth = Layer(root, "Mouth", "vfx_core", 1);
            ParticleSystem.MainModule mm = mouth.main;
            mm.startLifetime = 0.075f;
            mm.startSize = 0.2f * Px;
            mm.startSpeed = 0f;
            mm.startColor = new Color(0.80f, 1f, 0.75f);
            Over(mouth, Ramp(new Color(0.88f, 1f, 0.84f), new Color(0.6f, 0.95f, 0.55f), 0.5f),
                 Fade(1.5f));
            Size(mouth, Grow(1.0f, 3.4f, 0.45f));
            ParticleSystemRenderer mr = mouth.GetComponent<ParticleSystemRenderer>();
            mr.renderMode = ParticleSystemRenderMode.Stretch;
            mr.lengthScale = 4.5f;               // tall and thin: a jet, not a lamp
            mr.velocityScale = 0f;

            // The push along the floor.
            ParticleSystem shell = Layer(root, "Shell", "vfx_ring", 1);
            ParticleSystem.MainModule sm = shell.main;
            sm.startLifetime = 0.13f;
            sm.startSize = 0.3f * Px;
            sm.startSpeed = 0f;
            sm.startRotation = Mathf.Deg2Rad * 90f;
            sm.startColor = new Color(0.67f, 0.94f, 0.63f, 0.3f);
            Size(shell, Grow(1.0f, 7f, 0.45f));
            Over(shell, Ramp(new Color(0.67f, 0.94f, 0.63f),
                             new Color(0.5f, 0.85f, 0.48f), 0.5f), Fade(1.8f));

            return Save(root, "GR_GasVent");
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
        static int Wire(GameObject hit, GameObject death, GameObject parry,
                        GameObject clash, GameObject pickup, GameObject scuff)
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

                // The parry goes on PlayerEffects, not here. FighterDamageView answers
                // DAMAGE, and a parry does none -- that is its whole point. PlayerEffects
                // already has a `parried` slot waiting, with the loudest noise level in
                // its table, and nothing has ever filled it.
                if (effects != null)
                {
                    Undo.RecordObject(effects, "Build the hit effects");

                    effects.parried.particles = Hang(parry, at, "Parry");
                    if (effects.parried.at == null) effects.parried.at = at;

                    // Your swing was turned aside: the parry seen from the losing end.
                    effects.clashed.particles = Hang(clash, at, "Clash");
                    if (effects.clashed.at == null) effects.clashed.at = at;

                    // The payoff. docs/03 spends the whole scavenge phase earning this.
                    effects.pickUp.particles = Hang(pickup, at, "Pickup");
                    if (effects.pickUp.at == null) effects.pickUp.at = at;

                    // Dust, at the FEET, for the three things that scuff the floor. One
                    // prefab each rather than one shared: a ParticleSystem can only be
                    // playing once, and a fighter who grabs a ladder while the dust of his
                    // landing is still in the air would cut it off mid-cloud.
                    Transform feet = effects.feet != null ? effects.feet : at;
                    effects.mantle.particles = Hang(scuff, feet, "Dust Mantle");
                    if (effects.mantle.at == null) effects.mantle.at = feet;

                    effects.ladderGrab.particles = Hang(scuff, feet, "Dust Ladder");
                    if (effects.ladderGrab.at == null) effects.ladderGrab.at = feet;

                    effects.drop.particles = Hang(scuff, feet, "Dust Drop");
                    if (effects.drop.at == null) effects.drop.at = feet;

                    // `swing` is left empty on purpose: PlayerEffects already drives a
                    // TrailRenderer off the blade, and a puff of air behind a weapon that
                    // is already drawing its own arc is two answers to one question.

                    EditorUtility.SetDirty(effects);
                }

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
