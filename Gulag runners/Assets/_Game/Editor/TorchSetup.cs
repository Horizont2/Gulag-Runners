using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Lights the torches, so they stop being sticks with a cone on top.
    ///
    /// WHAT WAS ACTUALLY WRONG. The Quaternius torch is not an unlit stick: Torch.fbx
    /// carries two materials, "DarkMetal" and "Fire", and the second one is a 24-polygon
    /// flame cone 0.21 m tall sitting in the cup. So the fire was already there. What was
    /// missing was everything that makes fire read as fire -- the cone does not glow, it
    /// does not move, and it throws no light on the wall behind it. Six of those in a room
    /// is six props, which is exactly what they looked like.
    ///
    /// So this adds the three missing things, in that order of importance:
    ///   1. the cone becomes EMISSIVE, through a material of ours in the Fire slot. The
    ///      slot is found by name, never by index -- the index happens to be 1 here, and
    ///      an index typed into a tool is a bug waiting for the day a model is re-exported.
    ///   2. a particle flame sits on the cone, so something moves.
    ///   3. a flickering Light, on the few torches that earn one.
    ///
    /// WHY NOT ALL SIX GET A LIGHT. Both render pipeline assets cap additional lights at
    /// FOUR per object (m_AdditionalLightsPerObjectLimit). Six torch lights plus the gate
    /// fill is seven, and over the cap URP drops the surplus per object per frame, so
    /// lights blink in and out as things move. Three plus the gate fill is four exactly,
    /// which is the most that can be lit with nothing ever dropped. Which three is
    /// measured, not chosen: in frame beats out of frame, then nearer the fighter beats
    /// further. On the menu camera that picks the two torches by the arch -- the only two
    /// a phone ever sees -- plus the one standing level with the fighter, which is the
    /// only one of the six in FRONT of him and therefore the only one that can be a key
    /// light rather than a rim.
    ///
    /// Everything it makes is a GameObject in the scene with ordinary components on it.
    /// Run it again and it updates what it made rather than making a second set.
    /// </summary>
    public static class TorchSetup
    {
        const string HolderName = "Torch Fire";
        const string FlamePath =
            "Assets/Polyart/PolyartStudio/SharedResources/Effects/PF_FireSmall.prefab";
        const string MaterialPath = "Assets/_Game/Art/Materials/Torch_Fire.mat";

        // The stand-in lamp from before the torches could light themselves. It sat in mid
        // air a metre off the nearest torch, which is presumably why it ended up switched
        // off; now there is a real flame to hang the light on, it is a duplicate.
        const string StandIn = "Torch Key";

        // The flame prefab is authored at about a metre. A torch's own cone is 0.21 m, so
        // the particles want to be about that -- bigger and they swallow the torch.
        const float FlameScale = 0.22f;

        const int Lit = 3;

        [MenuItem("Tools/Gulag Runners/Setup/Light the torches")]
        public static void Setup()
        {
            Scene scene = SceneManager.GetActiveScene();
            Camera cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                Debug.LogWarning("No camera in this scene, so there is no shot to light for.");
                return;
            }

            List<Torch> torches = Find();
            if (torches.Count == 0)
            {
                Debug.LogWarning(
                    "Nothing in this scene has a material named like fire, so there is no " +
                    "torch to light. This looks for the material, not for the word Torch in " +
                    "a name, so a renamed torch is still found and a lamppost is not.");
                return;
            }

            Vector3 subject = Subject(cam);
            Rank(torches, cam, subject);

            Material glow = FireMaterial();
            GameObject flame = AssetDatabase.LoadAssetAtPath<GameObject>(FlamePath);
            if (flame == null)
                Debug.LogWarning($"No flame prefab at {FlamePath}; the torches will glow and " +
                                 "light, but nothing will move.");

            // The key is the first of the lit ones standing in FRONT of the fighter. If
            // none of them is, nothing gets promoted and the room is rim-lit -- which is
            // worth seeing in the log rather than papering over by promoting a torch
            // behind him and wondering why the face stays dark.
            int keyAt = -1;
            for (int i = 0; i < torches.Count && i < Lit; i++)
                if (torches[i].InFront) { keyAt = i; break; }

            var said = new List<string>();
            for (int i = 0; i < torches.Count; i++)
            {
                Torch t = torches[i];
                Emissive(t, glow);

                Transform holder = Holder(t);
                Flame(holder, flame);

                bool lamp = i < Lit;
                Lamp(holder, lamp, i == keyAt);

                said.Add($"{t.Name}: anchor {holder.position}, " +
                         (lamp ? (i == keyAt ? "KEY light" : "light") : "fire only") +
                         (t.InFrame ? ", in frame" : "") +
                         $", {t.ToSubject:0.00} m from the fighter");
            }

            if (keyAt < 0)
                Debug.LogWarning(
                    "Every lit torch stands behind the fighter, so he is rim-lit and his " +
                    "face is in shadow. Move one of them past him, towards the camera.");

            int removed = RemoveStandIn();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"Torches: {torches.Count} found, {Mathf.Min(Lit, torches.Count)} lit.\n" +
                string.Join("\n", said) +
                (removed > 0
                    ? $"\nRemoved the {StandIn} stand-in lamp; the torches carry that light now."
                    : ""));
        }

        // ---------------------------------------------------------------- finding them

        class Torch
        {
            public Renderer Renderer;
            public Transform Root;
            public int Slot = -1;        // the material slot the flame cone is drawn with
            public Bounds Cone;          // that submesh, in the renderer's local space
            public string Name;
            public bool InFrame;
            public bool InFront;
            public float ToSubject;
        }

        /// <summary>
        /// Every renderer in the scene drawing a material whose name says fire.
        ///
        /// Asking the geometry what it is beats asking the hierarchy what it is called:
        /// the torches in this scene are named Torch, Torch (1) ... Torch (5), and the day
        /// one is renamed "Brazier" a name test silently skips it.
        /// </summary>
        static List<Torch> Find()
        {
            var found = new List<Torch>();

            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude,
                                                                      FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer) continue;

                Material[] mats = r.sharedMaterials;
                int slot = -1;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name.ToLowerInvariant();
                    if (n.Contains("fire") || n.Contains("flame")) { slot = i; break; }
                }
                if (slot < 0) continue;

                var t = new Torch
                {
                    Renderer = r,
                    Root = Root(r.transform),
                    Slot = slot,
                    Cone = Cone(r, slot),
                };
                t.Name = t.Root.name;
                found.Add(t);
            }

            return found;
        }

        /// <summary>The prefab instance the renderer belongs to, or the renderer itself.</summary>
        static Transform Root(Transform t)
        {
            GameObject outer = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
            return outer != null ? outer.transform : t;
        }

        /// <summary>
        /// Where the flame cone is, in the renderer's own space.
        ///
        /// Through GetSubMesh, which reads the descriptor the importer wrote rather than
        /// the vertex buffer -- Torch.fbx is imported with Read/Write off, so anything
        /// that touches mesh.vertices throws. If the descriptor has no bounds the whole
        /// mesh's bounds stand in, which puts the flame half way down the shaft, hence the
        /// warning: better a visibly wrong anchor and a line in the console than a silently
        /// wrong one.
        /// </summary>
        static Bounds Cone(Renderer r, int slot)
        {
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer skin) mesh = skin.sharedMesh;
            else
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
            }
            if (mesh == null) return new Bounds(Vector3.zero, Vector3.zero);

            if (slot < mesh.subMeshCount)
            {
                Bounds b = mesh.GetSubMesh(slot).bounds;
                if (b.size.sqrMagnitude > 1e-12f) return b;
            }

            Debug.LogWarning(
                $"{r.name}: the fire submesh reports no bounds, so the flame is being hung " +
                "on the middle of the whole model instead. Nudge it by hand.");
            return mesh.bounds;
        }

        // ---------------------------------------------------------------- ranking them

        /// <summary>What the room is lit FOR: the fighter if there is one, else mid-shot.</summary>
        static Vector3 Subject(Camera cam)
        {
            var fighter = Object.FindFirstObjectByType<MenuFighter>();
            if (fighter != null)
            {
                Renderer r = fighter.GetComponentInChildren<Renderer>();
                if (r != null) return r.bounds.center;
                return fighter.transform.position + Vector3.up;
            }
            return cam.transform.position + cam.transform.forward * 2f + Vector3.up * 0.2f;
        }

        static void Rank(List<Torch> torches, Camera cam, Vector3 subject)
        {
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);
            Vector3 forward = cam.transform.forward;

            foreach (Torch t in torches)
            {
                Vector3 at = t.Renderer.transform.TransformPoint(t.Cone.center);
                t.InFrame = GeometryUtility.TestPlanesAABB(frustum, new Bounds(at, Vector3.one * 0.3f));
                t.ToSubject = Vector3.Distance(at, subject);

                // In front OF THE FIGHTER, from where the camera stands. A torch behind him
                // rims his shoulders; only one in front can light his face, and a menu that
                // does not light the character's face has failed at its one job.
                t.InFront = Vector3.Dot(at - subject, forward) < 0f;
            }

            torches.Sort((a, b) =>
            {
                if (a.InFrame != b.InFrame) return a.InFrame ? -1 : 1;
                return a.ToSubject.CompareTo(b.ToSubject);
            });
        }

        // ---------------------------------------------------------------- building it

        /// <summary>
        /// A child that cancels its parent's scale, so everything under it is in metres.
        ///
        /// The torches are model instances scaled by FORTY (the fbx is in centimetres and
        /// was placed by eye), so one unit under a torch is forty metres. Undo that once,
        /// here, and the flame, the light and the light's drift are all plain metres.
        /// </summary>
        static Transform Holder(Torch t)
        {
            Transform had = t.Root.Find(HolderName);
            GameObject go = had != null ? had.gameObject : null;

            if (go == null)
            {
                go = new GameObject(HolderName);
                Undo.RegisterCreatedObjectUndo(go, "Light the torches");
                go.transform.SetParent(t.Root, false);
            }

            Undo.RecordObject(go.transform, "Light the torches");

            Vector3 scale = t.Root.lossyScale;
            go.transform.localScale = new Vector3(Safe(scale.x), Safe(scale.y), Safe(scale.z));

            // Straight up, whatever the torch is doing. These lean ten degrees out of the
            // wall, and a flame that leans with its holder looks like it is being blown
            // sideways for ever.
            go.transform.rotation = Quaternion.identity;
            go.transform.position = t.Renderer.transform.TransformPoint(t.Cone.center);

            EditorUtility.SetDirty(go);
            return go.transform;
        }

        static float Safe(float lossy) =>
            Mathf.Abs(lossy) > 1e-4f ? 1f / lossy : 1f;

        static void Flame(Transform holder, GameObject prefab)
        {
            if (prefab == null) return;

            Transform had = holder.Find(prefab.name);
            GameObject go = had != null ? had.gameObject : null;

            if (go == null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
                Undo.RegisterCreatedObjectUndo(go, "Light the torches");
            }

            Undo.RecordObject(go.transform, "Light the torches");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * FlameScale;

            // The pack ships these on Local scaling, which means each of the four systems
            // reads its OWN transform and ignores the parent -- so scaling the prefab root
            // moves the layers apart without shrinking any of them. On Hierarchy the one
            // scale above drives all four, which is what a single knob has to mean.
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                Undo.RecordObject(ps, "Light the torches");
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                EditorUtility.SetDirty(ps);
            }

            EditorUtility.SetDirty(go);
        }

        static void Lamp(Transform holder, bool wanted, bool key)
        {
            Light light = holder.GetComponent<Light>();
            TorchLight flicker = holder.GetComponent<TorchLight>();

            if (!wanted)
            {
                if (flicker != null) Undo.DestroyObjectImmediate(flicker);
                if (light != null) Undo.DestroyObjectImmediate(light);
                return;
            }

            if (light == null) light = Undo.AddComponent<Light>(holder.gameObject);
            Undo.RecordObject(light, "Light the torches");

            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.38f);
            light.bounceIntensity = 0f;

            // The key is the one that models the face, so it is the brighter of the two
            // settings and the only one that casts -- one shadow caster in a menu is a
            // deliberate budget, and the mobile pipeline asset has additional-light shadows
            // switched off anyway, so on a phone this reads as the editor's preview only.
            light.intensity = key ? 2.6f : 1.6f;
            light.range = key ? 5.5f : 4.5f;
            light.shadows = key ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.75f;

            if (flicker == null)
                flicker = Undo.AddComponent<TorchLight>(holder.gameObject);
            Undo.RecordObject(flicker, "Light the torches");
            flicker.baseIntensity = light.intensity;

            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(flicker);
        }

        // ---------------------------------------------------------------- the glow

        /// <summary>
        /// The flame cone's material, swapped for one of ours with emission on.
        ///
        /// It has to be OURS. The pack's Fire material is generated by the importer and
        /// lives inside Torch.fbx, where nothing can edit it; and emission cannot be forced
        /// on from outside a material at all, because the _EMISSION keyword is compiled
        /// into the material and a MaterialPropertyBlock cannot enable a keyword. That was
        /// learnt the hard way on the loot glow, which rendered nothing for a commit.
        /// </summary>
        static Material FireMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                                ?? Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogWarning("No URP shader found, so the flame cone is left as it is.");
                    return null;
                }

                string dir = System.IO.Path.GetDirectoryName(MaterialPath);
                if (!AssetDatabase.IsValidFolder(dir))
                    System.IO.Directory.CreateDirectory(dir);

                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            // Over 1 on purpose. Bloom is what makes a flame look hot, and bloom only has
            // something to work with where the colour leaves the 0-1 range; the menu volume
            // has its threshold at 0.9.
            Color hot = new Color(2.4f, 1.05f, 0.30f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hot);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", hot);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", hot);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        static void Emissive(Torch t, Material glow)
        {
            if (glow == null || t.Slot < 0) return;

            Material[] mats = t.Renderer.sharedMaterials;
            if (mats[t.Slot] == glow) return;

            Undo.RecordObject(t.Renderer, "Light the torches");
            mats[t.Slot] = glow;
            t.Renderer.sharedMaterials = mats;
            EditorUtility.SetDirty(t.Renderer);
        }

        // ---------------------------------------------------------------- tidying up

        static int RemoveStandIn()
        {
            int gone = 0;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                                                                FindObjectsSortMode.None))
            {
                if (l.gameObject.name != StandIn) continue;
                if (l.GetComponent<TorchLight>() != null) continue;
                Undo.DestroyObjectImmediate(l.gameObject);
                gone++;
            }
            return gone;
        }
    }
}
