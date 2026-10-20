using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Inspector for <see cref="SideViewCamera"/>: shows what the current framing actually
    /// means in the two numbers that matter on a phone, and gives one-click border fitting.
    /// </summary>
    [CustomEditor(typeof(SideViewCamera))]
    public sealed class SideViewCameraEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SideViewCamera cam = (SideViewCamera)target;

            EditorGUILayout.Space();

            // Measured against the full screen, which is what the docs/02 rule is about: in a
            // half-height split view the fighter fills twice as much of its own viewport.
            float height = cam.EffectiveViewHeight;
            float viewportShare = cam.applyViewportRect ? Mathf.Clamp01(cam.viewportRect.height) : 1f;
            float fighterShare = height > 0f ? 1.8f / height * viewportShare : 0f;
            string verdict = fighterShare >= 0.17f && fighterShare <= 0.20f
                ? "within the 17-20% docs/02 asks for"
                : "OUTSIDE the 17-20% docs/02 asks for";

            EditorGUILayout.HelpBox(
                $"A 1.8 m fighter fills {fighterShare * 100f:0.#}% of screen height — {verdict}.\n" +
                $"View: {height:0.#} m tall. Arena borders: " +
                $"{cam.bordersMax.x - cam.bordersMin.x:0.#} x " +
                $"{cam.bordersMax.y - cam.bordersMin.y:0.#} m.",
                fighterShare >= 0.17f && fighterShare <= 0.20f ? MessageType.Info : MessageType.Warning);

            if (GUILayout.Button("Fit borders to arena", GUILayout.Height(24)))
            {
                Undo.RecordObject(cam, "Fit borders to arena");
                cam.FitBordersToArena();
                EditorUtility.SetDirty(cam);
            }
        }
    }
}
