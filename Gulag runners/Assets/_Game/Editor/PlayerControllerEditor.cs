using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Inspector for <see cref="PlayerController"/>. Surfaces the one number that is a design
    /// decision rather than a feel preference: the jump apex must stay below the floor height,
    /// otherwise ladders stop mattering and the arena topology of docs/05 collapses.
    /// Also shows live simulation state while playing, so movement can be debugged without
    /// adding a single Debug.Log.
    /// </summary>
    [CustomEditor(typeof(PlayerController))]
    public sealed class PlayerControllerEditor : Editor
    {
        const float FloorHeight = 2.5f;   // docs/05

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayerController pc = (PlayerController)target;
            float apex = pc.tuning.JumpApex;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"Jump apex {apex:0.00} m vs {FloorHeight:0.0} m floor height.\n" +
                (apex >= FloorHeight
                    ? "A jump clears a whole floor. Ladders become pointless — lower jumpSpeed " +
                      "or raise gravity."
                    : "Correct: a jump cannot clear a floor, so ladders are the only way up."),
                apex >= FloorHeight ? MessageType.Error : MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Attach character model", GUILayout.Height(24)))
                {
                    GameObject prefab = PlayerVisualSetup.FindPrefab();
                    if (prefab != null) PlayerVisualSetup.Attach(pc, prefab);
                }
                if (GUILayout.Button("Remove", GUILayout.Height(24), GUILayout.Width(80)))
                    PlayerVisualSetup.DetachFromAll();
            }

            if (!Application.isPlaying) return;

            PlayerSimState s = pc.State;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Simulation", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Mode", s.Mode);
                EditorGUILayout.Vector2Field("Feet", pc.FeetPosition);
                EditorGUILayout.TextField("Velocity", $"{s.Velocity.X}, {s.Velocity.Y}");
                EditorGUILayout.Toggle("Crouching", s.Crouching);
                EditorGUILayout.Toggle("Invulnerable", s.Invulnerable);
                EditorGUILayout.IntField("Stamina", s.StaminaCharges);
                EditorGUILayout.EnumPopup("Noise this tick", s.Noise);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Carrying (docs/02: three slots, no menu)",
                                       EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                string weapon = s.Inventory.BareHanded
                    ? "fists"
                    : $"{s.Inventory.Weapon} ({s.Inventory.WeaponDurability} left)";
                EditorGUILayout.TextField("Weapon", weapon);
                EditorGUILayout.EnumPopup("Armour", s.Inventory.Armour);
                EditorGUILayout.EnumPopup("Utility", s.Inventory.Utility);
                EditorGUILayout.IntField("Opening chest", s.OpeningChest);
            }

            Repaint();
        }
    }
}
