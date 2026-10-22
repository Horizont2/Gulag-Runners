using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Inspector for <see cref="HudView"/>.
    ///
    /// Two jobs. The button, which paints every wired element the colour it has on the
    /// first frame of a match — because picking a colour for forty graphics by hand is
    /// forty chances to use the wrong blue, and the wrong blue is invisible until somebody
    /// compares two screenshots.
    ///
    /// And the report above it, which answers the question a list of object fields cannot:
    /// is anything NOT wired. An empty slot in the inspector looks exactly like a filled
    /// one from two metres away, and the failure it causes is a square that never changes
    /// colour — which reads as a design decision rather than as a bug.
    /// </summary>
    [CustomEditor(typeof(HudView))]
    public sealed class HudViewEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            HudView view = (HudView)target;
            EditorGUILayout.Space();

            int wired = Painted(view).Count;
            List<string> gaps = Gaps(view);

            EditorGUILayout.HelpBox(
                gaps.Count == 0
                    ? $"{wired} element(s) wired. Everything this component can paint, it can reach."
                    : $"{wired} element(s) wired, {gaps.Count} gap(s):\n  • " +
                      string.Join("\n  • ", gaps.Take(8)) +
                      (gaps.Count > 8 ? $"\n  • …and {gaps.Count - 8} more" : ""),
                gaps.Count == 0 ? MessageType.Info : MessageType.Warning);

            using (new EditorGUI.DisabledScope(wired == 0))
            {
                if (GUILayout.Button("Paint everything to the starting colours",
                                     GUILayout.Height(30)))
                    Run(view, "Paint HUD", v => v.PaintStartingColours());

                if (GUILayout.Button("Pull the rarity ramp from the simulation, then paint",
                                     GUILayout.Height(22)))
                    Run(view, "Pull HUD rarity ramp", v => v.PullRarityRamp());
            }

            EditorGUILayout.LabelField(
                "Starting colours are: both fighters whole, every charge full, every slot " +
                "empty, every button live.", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// Runs one of the view's own paint methods with every object it touches under a
        /// single undo step.
        ///
        /// Recording the HudView alone is not enough and is the obvious mistake here:
        /// nothing on the HudView changes. What changes is the colour on forty OTHER
        /// components, and an undo that does not name them leaves the scene repainted and
        /// the user convinced they undid it.
        /// </summary>
        static void Run(HudView view, string label, System.Action<HudView> paint)
        {
            Object[] touched = Painted(view).Cast<Object>()
                .Concat(SwapArrows(view).Cast<Object>())
                .Append(view)
                .ToArray();

            Undo.RecordObjects(touched, label);
            paint(view);
            foreach (Object o in touched) EditorUtility.SetDirty(o);

            Debug.Log($"{view.name}: painted {touched.Length - 1} element(s) to the " +
                      "starting colours.", view);
        }

        /// <summary>Every graphic this view is able to colour.</summary>
        static List<Graphic> Painted(HudView v)
        {
            List<Graphic> all = new List<Graphic>();
            void Add(Graphic g) { if (g != null) all.Add(g); }

            foreach (HudBar bar in new[] { v.bar1, v.bar2 })
            {
                if (bar == null) continue;
                Add(bar.frame); Add(bar.fill);
                if (bar.pips != null) foreach (Graphic p in bar.pips) Add(p);
            }
            Add(v.clock);

            if (v.slots != null)
                foreach (HudSlot s in v.slots)
                {
                    if (s == null) continue;
                    Add(s.frame); Add(s.glow); Add(s.icon);
                }

            foreach (HudButton b in new[] { v.dodge, v.action, v.jump })
            {
                if (b == null) continue;
                Add(b.ring); Add(b.glyph);
            }

            // The attack stick, which replaced the attack and block rings (docs/31).
            if (v.aim != null) { Add(v.aim.ring); Add(v.aim.knob); }

            if (v.lineArt != null) foreach (Graphic g in v.lineArt) Add(g);
            return all;
        }

        static List<GameObject> SwapArrows(HudView v)
        {
            List<GameObject> all = new List<GameObject>();
            if (v.slots == null) return all;
            foreach (HudSlot s in v.slots)
                if (s != null && s.swapArrow != null) all.Add(s.swapArrow);
            return all;
        }

        /// <summary>What is missing, in the words of the thing that will go wrong.</summary>
        static List<string> Gaps(HudView v)
        {
            List<string> gaps = new List<string>();

            if (v.player1 == null && v.player2 == null)
                gaps.Add("No fighter wired: nothing will ever change colour in play.");

            for (int i = 0; i < 2; i++)
            {
                HudBar bar = i == 0 ? v.bar1 : v.bar2;
                PlayerController who = i == 0 ? v.player1 : v.player2;
                if (who == null || bar == null) continue;
                if (bar.frame == null && bar.fill == null)
                    gaps.Add($"Player {i + 1} has a fighter but no health bar.");
                int pips = bar.pips == null ? 0 : bar.pips.Count(p => p != null);
                int want = who.tuning != null ? who.tuning.staminaMax : 0;
                if (want > 0 && pips != want)
                    gaps.Add($"Player {i + 1}: {pips} pip(s) for {want} stamina charge(s).");
            }

            if (v.slots == null || v.slots.Length == 0)
                gaps.Add("No equipment squares wired.");
            else
            {
                HashSet<ItemKind> seen = new HashSet<ItemKind>();
                foreach (HudSlot s in v.slots)
                {
                    if (s == null) continue;
                    if (s.frame == null)
                        gaps.Add($"The {s.slot} square has no frame to colour.");
                    if (s.slot == ItemKind.None)
                        gaps.Add("A square is set to slot None and will always read empty.");
                    else if (!seen.Add(s.slot))
                        gaps.Add($"Two squares both read the {s.slot} slot.");
                }
                foreach (ItemKind kind in new[] { ItemKind.Weapon, ItemKind.Helmet,
                                                  ItemKind.Armour, ItemKind.Belt,
                                                  ItemKind.Utility })
                    if (!seen.Contains(kind))
                        gaps.Add($"No square reads the {kind} slot.");
            }

            if (v.aim == null || (v.aim.ring == null && v.aim.knob == null))
                gaps.Add("The attack stick is not wired. It is the whole of the fight " +
                         "(docs/31): without it the right thumb does nothing at all.");

            if (v.dodge != null && v.dodge.ring == null && v.dodge.glyph == null)
                gaps.Add("The dodge button is not wired, so there is no way to roll.");

            return gaps;
        }
    }
}
