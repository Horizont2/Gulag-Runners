using System;
using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// What the phone is actually giving, in three numbers that do not flatter it.
    ///
    /// FrameRate asks for sixty. Asking is not getting, and there was no way at all to tell
    /// the difference: nothing in this project measured a frame at runtime. perf_check
    /// counts triangles and textures off the project, which says what the scene COSTS and
    /// nothing about what the device does with it.
    ///
    /// AN AVERAGE IS THE WRONG NUMBER and that is the whole reason this exists. A run that
    /// holds sixty and drops to twenty twice a second averages about fifty-eight and feels
    /// broken; a run that sits flat at fifty-five averages fifty-five and feels fine. The
    /// average cannot tell those apart, so it is not shown. What is shown:
    ///
    ///   MEDIAN -- what the game is doing most of the time. This is "the frame rate".
    ///   WORST 1% -- the hitches. The number that decides whether it feels smooth.
    ///   MISSED -- what share of frames took longer than the target allows. Zero is
    ///       smooth. Anything over about one in twenty is felt as stutter even when the
    ///       median is perfect.
    ///
    /// It also prints what was ASKED for, read back out of Application.targetFrameRate
    /// rather than from the constant, so a platform that ignored the request says so
    /// instead of leaving the numbers to be puzzled over.
    ///
    /// On in a development build, and otherwise only when switched on. It costs one ring
    /// buffer write per frame and a sort of a few hundred floats twice a second.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrameMeter : MonoBehaviour
    {
        const string Pref = "gr.framemeter";

        /// <summary>Whether to show it. Saved, so a phone keeps the answer.</summary>
        public static bool Wanted
        {
            get => PlayerPrefs.GetInt(Pref, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(Pref, value ? 1 : 0);
                PlayerPrefs.Save();
                if (value) Make();
                else if (_only != null) Destroy(_only.gameObject);
            }
        }

        static FrameMeter _only;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Debug.isDebugBuild || Application.isEditor || Wanted) Make();
        }

        static void Make()
        {
            if (_only != null) return;
            GameObject made = new GameObject("Frame Meter");
            DontDestroyOnLoad(made);
            _only = made.AddComponent<FrameMeter>();
        }

        // Three seconds at sixty, which is long enough for a hitch to be in the window and
        // short enough that walking into a heavy part of the arena shows up while you are
        // standing in it.
        const int Window = 180;

        readonly float[] _ring = new float[Window];
        readonly float[] _sorted = new float[Window];
        int _at;
        int _have;

        float _next;
        float _median;
        float _worst;
        float _missed;

        GUIStyle _style;

        // Built when the numbers change, not when the frame is drawn. perf_check caught the
        // first version allocating three strings per frame inside OnGUI -- and OnGUI runs
        // more than once a frame -- which is a meter causing the very hitches it exists to
        // find. Twice a second is how often these numbers move.
        string _line = "";
        GUIContent _content = new GUIContent("");
        Rect _box;

        void Update()
        {
            // unscaledDeltaTime: a hit stop is the game holding time still on purpose and
            // is not a dropped frame.
            _ring[_at] = Time.unscaledDeltaTime;
            _at = (_at + 1) % Window;
            if (_have < Window) _have++;

            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Measure();
        }

        void Measure()
        {
            if (_have == 0) return;

            Array.Copy(_ring, _sorted, _have);
            Array.Sort(_sorted, 0, _have);

            _median = _sorted[_have / 2];

            // The worst one per cent, not the single worst: one frame can be a garbage
            // collection or the operating system looking at a notification, and neither is
            // the game's fault or the player's problem.
            int tail = Mathf.Max(1, _have / 100);
            float sum = 0f;
            for (int i = _have - tail; i < _have; i++) sum += _sorted[i];
            _worst = sum / tail;

            // What the target allows a frame, plus a tenth for honest jitter.
            int want = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
            float budget = 1f / want * 1.1f;

            int over = 0;
            for (int i = 0; i < _have; i++) if (_sorted[i] > budget) over++;
            _missed = over / (float)_have;

            Compose();
        }

        /// <summary>The whole readout, as one string, made twice a second.</summary>
        void Compose()
        {
            int asked = Application.targetFrameRate;
            _line =
                $"{1f / _median:0} fps   worst 1% {1f / _worst:0}   missed {_missed * 100f:0}%\n"
                + $"asked for {(asked > 0 ? asked.ToString() : "nothing")}, "
                + $"vSync {QualitySettings.vSyncCount}";
            _content.text = _line;

            if (_style == null) return;
            float pad = _style.fontSize * 0.4f;
            Vector2 size = _style.CalcSize(new GUIContent(_line.Replace("\n", " ")));
            _box = new Rect(pad, pad, size.x + pad * 2f, _style.fontSize * 2.6f);
        }

        void OnGUI()
        {
            if (_median <= 0f) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(14, Screen.height / 42),
                    alignment = TextAnchor.UpperLeft,
                };
                Compose();                       // the box needs the style to be measured
            }

            float pad = _style.fontSize * 0.4f;

            // Black behind it: over a lit arena, text on nothing is unreadable, and a meter
            // you cannot read is a meter that gets ignored.
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(_box, Texture2D.whiteTexture);
            GUI.color = _missed > 0.05f ? new Color(1f, 0.45f, 0.4f)
                      : _missed > 0.01f ? new Color(1f, 0.8f, 0.35f)
                      : new Color(0.75f, 1f, 0.75f);
            GUI.Label(new Rect(_box.x + pad, _box.y + pad * 0.5f, _box.width, _box.height),
                      _content, _style);
            GUI.color = was;
        }

        void OnDestroy() { if (_only == this) _only = null; }
    }
}
