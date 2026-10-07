using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Draws what this player can hear.
    ///
    /// docs/02 lists the directional sound indicator among the settings that are NOT optional:
    /// the game is played on phones, usually muted, and a player who cannot hear the other one is
    /// playing a different game to the one that was designed. It is an accessibility feature that
    /// happens to be a core mechanic.
    ///
    /// A sound inside the frame is marked where it happened. One outside is pinned to the edge in
    /// its direction, which is the part that matters: during the scavenge phase the other player
    /// is never on screen (docs/01), so the edge is where all the information is.
    ///
    /// Drawn with IMGUI on purpose for now. It creates nothing, it needs no canvas, and it is
    /// meant to be replaced by the real HUD when the touch controls are built — the reading of
    /// the model is the part worth getting right first.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class NoiseIndicator : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Whose ears these are. Usually the player this camera follows.")]
        public NoiseListener listener;

        [Header("Look")]
        [Tooltip("Optional. Without one, a plain wedge is drawn, which is already readable.")]
        public Texture icon;

        [Tooltip("Size of the marker at full loudness, in pixels.")]
        public float size = 34f;
        [Tooltip("And at the quietest it will still be shown.")]
        public float minSize = 14f;

        [Tooltip("How far in from the edge of the frame an off-screen sound is pinned.")]
        public float margin = 26f;

        [Header("Colour per level")]
        [Tooltip("Loudness is also shown by size and opacity, so these stay legible with a " +
                 "colour-blind palette (docs/02 asks for one).")]
        public Color quiet = new Color(0.65f, 0.80f, 1f);
        public Color medium = new Color(1f, 0.85f, 0.40f);
        public Color loud = new Color(1f, 0.45f, 0.35f);

        [Tooltip("A sound heard through a floor is drawn fainter: knowing somebody is near is " +
                 "not knowing where they are.")]
        [Range(0f, 1f)] public float muffledAlpha = 0.55f;

        [Tooltip("Off by default. docs/02 lists a direction indicator among the settings a " +
                 "player must be able to switch on — for playing muted and for deaf players — " +
                 "and that is what it is: a setting, not something the game shows by default. " +
                 "Left on it also hands you the opponent's position, which is the one thing " +
                 "the noise system exists to make you work for.")]
        public bool draw;

        Camera _camera;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (listener == null)
                Debug.LogWarning($"{name}: no NoiseListener assigned, so nothing will be shown.", this);
        }

        void OnGUI()
        {
            if (!draw || listener == null || _camera == null || Event.current.type != EventType.Repaint)
                return;

            Rect frame = ViewportInPixels();

            for (int i = 0; i < listener.Count; i++)
            {
                HeardSound heard = listener.Get(i);
                float fade = listener.Freshness(i) * heard.Loudness;
                if (fade <= 0.02f) continue;

                Vector3 world = new Vector3(heard.Source.x, heard.Source.y, transform.position.z);
                Vector3 vp = _camera.WorldToViewportPoint(world);
                bool onScreen = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;

                Vector2 at;
                float angle = 0f;

                if (onScreen)
                {
                    at = new Vector2(frame.x + vp.x * frame.width,
                                     frame.y + (1f - vp.y) * frame.height);
                }
                else
                {
                    // Behind the camera flips the viewport point; the direction has to be taken
                    // from the world instead, or sounds behind you point the wrong way.
                    Vector2 dir = heard.Source - listener.Position;
                    if (dir.sqrMagnitude < 0.0001f) continue;
                    dir.Normalize();

                    Vector2 centre = new Vector2(frame.center.x, frame.center.y);
                    Vector2 half = new Vector2(frame.width * 0.5f - margin, frame.height * 0.5f - margin);
                    float scale = Mathf.Min(half.x / Mathf.Max(0.0001f, Mathf.Abs(dir.x)),
                                            half.y / Mathf.Max(0.0001f, Mathf.Abs(dir.y)));
                    at = centre + new Vector2(dir.x, -dir.y) * scale;
                    angle = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
                }

                float px = Mathf.Lerp(minSize, size, fade);
                Color colour = Colour(heard.Level);
                colour.a = fade * (heard.Muffled ? muffledAlpha : 1f);

                Rect r = new Rect(at.x - px * 0.5f, at.y - px * 0.5f, px, px);
                Matrix4x4 saved = GUI.matrix;
                if (!onScreen) GUIUtility.RotateAroundPivot(angle, at);

                Color savedColour = GUI.color;
                GUI.color = colour;
                GUI.DrawTexture(r, icon != null ? icon : Texture2D.whiteTexture);
                GUI.color = savedColour;
                GUI.matrix = saved;
            }
        }

        Color Colour(NoiseLevel level) => level switch
        {
            NoiseLevel.Quiet => quiet,
            NoiseLevel.Medium => medium,
            _ => loud
        };

        /// <summary>This camera's slice of the screen, in GUI pixels (origin top-left).</summary>
        Rect ViewportInPixels()
        {
            Rect r = _camera.rect;
            return new Rect(r.x * Screen.width,
                            (1f - r.y - r.height) * Screen.height,
                            r.width * Screen.width,
                            r.height * Screen.height);
        }
    }
}
