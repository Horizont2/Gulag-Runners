using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Uses an imported effects pack for the slots it has something better for, and leaves
    /// the rest to the ones <see cref="HitEffectSetup"/> builds by hand.
    ///
    /// Not a second tool. One menu item has to leave one result, and a project with two
    /// ways to dress a fighter is a project where half the fighters are dressed the other
    /// way. So this is a pass inside the existing one: it runs after the hand-built set is
    /// wired, replaces what it can, and says what it replaced.
    ///
    /// Matched by NAME, against a ranked list of phrases per slot rather than against one
    /// exact asset path. A pack is somebody else's naming scheme and the next pack will be
    /// a different one; "the first prefab whose name contains all of these words" survives
    /// that, and falls through to the hand-built system when it finds nothing. Nothing here
    /// fails if no pack is installed at all.
    ///
    /// Two things have to be undone on every instance taken from a pack, and both are
    /// silent if missed:
    ///
    ///   Play On Awake   every system in the game is fired by PlayerEffects off simulation
    ///                   state. One that plays itself fires once at spawn, in the cage,
    ///                   before the round starts.
    ///   Clear Behavior  Cartoon FX defaults its effects to DESTROY themselves when they
    ///                   finish, which is right for a system you instantiate per hit and
    ///                   wrong for one held in a slot: it would play once, delete itself,
    ///                   and leave an empty reference that reports nothing for the rest of
    ///                   the match. Set by reflection, so this file compiles and runs with
    ///                   no pack installed and does not name a third-party type.
    /// </summary>
    public static class EffectPack
    {
        /// <summary>The height the packs are authored against: an ordinary human.</summary>
        const float AuthoredFor = 1.8f;

        static Dictionary<string, string> s_prefabs;

        /// <summary>
        /// Replaces what it can and returns the slots it filled, for the tool's report.
        /// </summary>
        public static List<string> Upgrade(PlayerController fighter, PlayerEffects fx,
                                           FighterDamageView wounds, Transform feet,
                                           Transform body, float height)
        {
            List<string> took = new List<string>();
            if (fx == null) return took;

            Transform home = feet != null ? feet.parent : fighter.transform;
            float scale = Mathf.Max(0.05f, height / AuthoredFor);

            // A swing is the one that matters most: it is the only cue that says the blade
            // went live, and under the commitment rule that moment is the whole read.
            Take(fx.swing, home, "swing", scale, took, "slash", "wind trail");
            Take(fx.hurt, body, "hurt", scale, took, "blood directional", "blood", "hit a");
            Take(fx.parried, body, "parried", scale, took, "flash", "sword hit plain");
            Take(fx.guardBroken, body, "clash", scale, took, "hit misc a", "sword hit plain");
            Take(fx.death, body, "death", scale, took, "souls escape", "skull head");

            Take(fx.stepQuiet, feet, "step quiet", scale, took, "hit misc f smoke");
            Take(fx.stepLoud, feet, "step loud", scale, took, "ground hit");
            Take(fx.landSoft, feet, "land soft", scale, took, "hit misc f smoke");
            Take(fx.landHard, feet, "land hard", scale, took, "ground hit");
            Take(fx.jump, feet, "jump", scale, took, "hit misc f smoke");
            Take(fx.dodge, feet, "dodge", scale, took, "wind trail", "hit misc f smoke");

            Take(fx.chestOpened, body, "chest opened", scale, took, "magic poof");
            Take(fx.pickUp, body, "pick up", scale, took, "magic poof");
            Take(fx.mantle, feet, "mantle", scale, took, "ground hit");
            Take(fx.ladderGrab, feet, "ladder grab", scale, took, "hit misc f smoke");

            // chestWorking is deliberately left alone: it is the one that must LOOP, and a
            // pack's one-shot in that slot would play once and leave the tell off for the
            // rest of the opening -- which is the opposite of what it is for.

            if (wounds != null)
            {
                ParticleSystem blood = Instance(body, "blood", scale,
                                                "blood directional", "blood");
                ParticleSystem guard = Instance(body, "block sparks", scale,
                                                "sword hit plain", "hit misc a");
                if (blood != null || guard != null)
                {
                    Undo.RecordObject(wounds, "Wire effects pack");
                    if (blood != null) { wounds.bloodOnHit = blood; took.Add("blood"); }
                    if (guard != null) { wounds.sparksOnBlock = guard; took.Add("block sparks"); }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(wounds);
                }
            }

            if (took.Count > 0) PrefabUtility.RecordPrefabInstancePropertyModifications(fx);
            return took;
        }

        static void Take(ActionEffect slot, Transform parent, string name, float scale,
                         List<string> took, params string[] ranked)
        {
            ParticleSystem made = Instance(parent, name, scale, ranked);
            if (made == null) return;

            // The hand-built one it replaces goes, rather than lingering switched off where
            // somebody will later wonder which of the two is live.
            if (slot.particles != null) Undo.DestroyObjectImmediate(slot.particles.gameObject);
            slot.particles = made;
            took.Add(name);
        }

        static ParticleSystem Instance(Transform parent, string name, float scale,
                                       params string[] ranked)
        {
            GameObject asset = Asset(ranked);
            if (asset == null || parent == null) return null;

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            if (go == null) return null;

            Undo.RegisterCreatedObjectUndo(go, "Wire effects pack");
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * scale;

            ParticleSystem[] systems = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in systems)
            {
                ParticleSystem.MainModule main = ps.main;
                main.playOnAwake = false;
                // Hierarchy, not Local: the fighter itself is scaled, and a Local system
                // ignores every scale above its own -- which is a spark authored for a
                // 1.8 m man drawn at full size on a 0.91 m one.
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            Keep(go);
            return systems.Length > 0 ? systems[0] : null;
        }

        /// <summary>
        /// Stops a pack effect deleting itself when it finishes.
        ///
        /// By reflection on purpose. The field belongs to the pack, so naming its type here
        /// would make this file -- and the tool it is part of -- fail to compile for anyone
        /// who has not imported that pack. A tool that cannot run without an optional asset
        /// is not an optional asset.
        /// </summary>
        static void Keep(GameObject go)
        {
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                FieldInfo field = mb.GetType().GetField(
                    "clearBehavior", BindingFlags.Public | BindingFlags.Instance);
                if (field == null || !field.FieldType.IsEnum) continue;

                // "None": finish and stay. Disable would switch the object off, and a
                // switched-off system never answers Play() again.
                foreach (string want in new[] { "None", "none" })
                    if (Enum.IsDefined(field.FieldType, want))
                    {
                        field.SetValue(mb, Enum.Parse(field.FieldType, want));
                        EditorUtility.SetDirty(mb);
                        break;
                    }
            }
        }

        /// <summary>The best prefab for a slot: the first phrase that matches anything.</summary>
        static GameObject Asset(params string[] ranked)
        {
            if (s_prefabs == null)
            {
                s_prefabs = new Dictionary<string, string>();
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    // Our own prefabs are not an effects pack, and a fighter instantiated
                    // into a fighter is a loop nobody enjoys finding.
                    if (path.StartsWith("Assets/_Game/") || path.StartsWith("Assets/Prefabs/"))
                        continue;
                    string key = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (!s_prefabs.ContainsKey(key)) s_prefabs[key] = path;
                }
            }

            foreach (string phrase in ranked)
            {
                string[] words = phrase.ToLowerInvariant().Split(' ');
                string hit = s_prefabs.Keys
                    .Where(k => words.All(w => k.Contains(w)))
                    .OrderBy(k => k.Length)          // the plainest name that matches
                    .FirstOrDefault();
                if (hit != null) return AssetDatabase.LoadAssetAtPath<GameObject>(s_prefabs[hit]);
            }
            return null;
        }

        /// <summary>Forgets the asset list, so a freshly imported pack is seen.</summary>
        public static void Forget() => s_prefabs = null;
    }
}
