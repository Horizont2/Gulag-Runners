using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Inspector for <see cref="AudioManager"/>. Its job is to make the gaps obvious: the table is
    /// meant to be filled in while the game is built, and a sound nobody noticed was missing is a
    /// mechanic that silently does not work.
    /// </summary>
    [CustomEditor(typeof(AudioManager))]
    public sealed class AudioManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            AudioManager manager = (AudioManager)target;

            int rows = manager.sounds.entries.Count;
            int filled = 0, named = 0;
            foreach (SoundEntry e in manager.sounds.entries)
            {
                if (e == null) continue;
                if (e.HasClip) filled++;
                if (!string.IsNullOrEmpty(e.fmodEvent)) named++;
            }

            int all = System.Enum.GetValues(typeof(SoundId)).Length - 1;   // minus None

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"{rows} of {all} sounds have a row; {filled} have a clip" +
                (named > 0 ? $", {named} an FMOD event" : "") + ".\n" +
                "A row with no clip is silent and harmless — fill them in as the game is built.",
                filled == 0 ? MessageType.Warning : MessageType.Info);

            if (GUILayout.Button("Add a row for every sound", GUILayout.Height(24)))
            {
                Undo.RecordObject(manager, "Fill the sound table");
                int added = manager.sounds.FillGaps();
                EditorUtility.SetDirty(manager);
                Debug.Log($"{manager.name}: added {added} empty sound row(s).", manager);
            }

            if (manager.backendBehaviour == null)
                EditorGUILayout.HelpBox(
                    "No backend, so nothing will be audible. The indicator still works — it reads " +
                    "the hearing model, not the backend.", MessageType.Warning);
            else if (!(manager.backendBehaviour is IAudioBackend))
                EditorGUILayout.HelpBox("That component is not an IAudioBackend.", MessageType.Error);
        }
    }
}
