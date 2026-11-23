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

            float height = cam.EffectiveViewHeight;
            float body = cam.FramingBodyHeight;
            float share = height > 0f ? body / height : 0f;
            bool readable = share >= 0.17f && share <= 0.20f;

            // Width is where a split-screen test goes wrong, not height: half of a 16:9 window
            // is 32:9, and at a readable character size that shows most of the arena at once.
            Vector2 view = Handles.GetMainGameViewSize();
            Rect rect = cam.applyViewportRect
                ? cam.FittedViewportRect(view) : new Rect(0f, 0f, 1f, 1f);
            float px = Mathf.Max(1f, view.x * rect.width);
            float py = Mathf.Max(1f, view.y * rect.height);
            float aspect = px / py;
            float width = height * aspect;

            float arenaW = cam.bordersMax.x - cam.bordersMin.x;
            float arenaH = cam.bordersMax.y - cam.bordersMin.y;
            bool pinnedX = cam.useBorders && width >= arenaW;
            bool pinnedY = cam.useBorders && height >= arenaH;

            EditorGUILayout.HelpBox(
                $"A {body:0.##} m fighter fills {share * 100f:0.#}% of the view height — " +
                (readable ? "within" : "OUTSIDE") + " the 17-20% docs/02 asks for.\n" +
                $"View: {width:0.#} x {height:0.#} m at {aspect:0.00}:1. " +
                $"Arena borders: {arenaW:0.#} x {arenaH:0.#} m.",
                readable ? MessageType.Info : MessageType.Warning);

            if (pinnedX || pinnedY)
            {
                EditorGUILayout.HelpBox(
                    "The view is as large as the arena " +
                    (pinnedX && pinnedY ? "in both directions" : pinnedX ? "horizontally" : "vertically") +
                    ", so the borders hold the camera still and none of the follow rules below " +
                    "can do anything. Either frame the fighter larger, or give this viewport a " +
                    "device aspect so it stops being a letterbox.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Fit borders to arena", GUILayout.Height(24)))
            {
                Undo.RecordObject(cam, "Fit borders to arena");
                cam.FitBordersToArena();
                EditorUtility.SetDirty(cam);
            }
        }
    }
}
