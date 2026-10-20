using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GulagRunners.Game
{
    /// <summary>
    /// Runs the menu scene: what is in it, and the one move it makes.
    ///
    /// The move is the whole reason the menu is a cell rather than a screen with buttons.
    /// docs/01 opens with "two wake in cages at opposite edges of the arena. The doors
    /// open." So PLAY opens this one: the portcullis rises, the camera walks through it,
    /// and the match loads behind that movement. No loading screen, because there is
    /// something to watch instead.
    ///
    /// Nothing here is created at play time. The gate, the camera and the fighter are
    /// objects in the scene; this only moves two of them and then asks for the next scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuDirector : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The fighter on show. Found in the scene when empty.")]
        public MenuFighter fighter;

        [Tooltip("The camera that walks through the gate. The main camera when empty.")]
        public Camera view;

        [Tooltip("The portcullis, or whatever shuts the way out. Leave empty and only the " +
                 "camera moves, which still reads as leaving.")]
        public Transform gate;

        [Header("The gate")]
        [Tooltip("How far it rises, in metres. Far enough that its bottom edge leaves the " +
                 "top of the frame — a gate that stops halfway is a gate you walk into.")]
        [Range(0f, 6f)] public float gateRise = 2.6f;

        [Tooltip("Seconds it takes. Heavy things start slowly and arrive slowly.")]
        [Range(0.1f, 4f)] public float gateSeconds = 1.1f;

        public AnimationCurve gateEase =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("The walk through")]
        [Tooltip("How far the camera moves forward, in metres. It does not have to reach " +
                 "the gate: it has to be MOVING when the screen is handed over.")]
        [Range(0f, 20f)] public float walkDistance = 5f;

        [Tooltip("Seconds of walking. It begins part way through the gate's rise, so the " +
                 "two moves overlap rather than queue.")]
        [Range(0.1f, 6f)] public float walkSeconds = 1.4f;

        [Range(0f, 1f)] public float walkStartsAt = 0.45f;

        public AnimationCurve walkEase =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("The match")]
        [Tooltip("Scene to load. Empty plays the whole move and loads nothing, which is how " +
                 "to look at the transition without leaving the menu.")]
        public string matchScene = "SetupScene";

        [Tooltip("Start loading at this point of the walk, 0 to 1, and show the next scene " +
                 "only once the walk has finished. Loading early hides the wait inside the " +
                 "move; showing early throws the move away.")]
        [Range(0f, 1f)] public float loadStartsAt = 0.2f;

        [Header("Debug")]
        public string state = "waiting";

        bool _leaving;
        Vector3 _gateHome;
        Vector3 _viewHome;

        void Reset()
        {
            fighter = FindFirstObjectByType<MenuFighter>();
            view = Camera.main;
        }

        void Awake()
        {
            if (fighter == null) fighter = FindFirstObjectByType<MenuFighter>();
            if (view == null) view = Camera.main;

            if (gate != null) _gateHome = gate.localPosition;
            if (view != null) _viewHome = view.transform.position;
        }

        /// <summary>
        /// Opens the gate and leaves. Wire PLAY to this.
        ///
        /// Ignores a second press on purpose: the one thing a player will do while a door
        /// is opening is press the door again.
        /// </summary>
        public void Play()
        {
            if (_leaving) return;
            _leaving = true;
            StartCoroutine(Leave());
        }

        /// <summary>Puts everything back, for testing the move more than once.</summary>
        [ContextMenu("Reset the gate")]
        public void Rewind()
        {
            StopAllCoroutines();
            _leaving = false;
            state = "waiting";
            if (gate != null) gate.localPosition = _gateHome;
            if (view != null) view.transform.position = _viewHome;
        }

        IEnumerator Leave()
        {
            state = "opening";

            Vector3 from = view != null ? view.transform.position : Vector3.zero;
            Vector3 forward = view != null ? view.transform.forward : Vector3.forward;

            AsyncOperation load = null;
            float longest = Mathf.Max(gateSeconds, walkStartsAt * gateSeconds + walkSeconds);
            float t = 0f;

            while (t < longest)
            {
                t += Time.deltaTime;

                if (gate != null && gateSeconds > 0f)
                {
                    float g = gateEase.Evaluate(Mathf.Clamp01(t / gateSeconds));
                    gate.localPosition = _gateHome + Vector3.up * (gateRise * g);
                }

                float began = walkStartsAt * gateSeconds;
                float w = walkSeconds > 0f
                    ? Mathf.Clamp01((t - began) / walkSeconds)
                    : 1f;

                if (w > 0f && view != null)
                    view.transform.position = from + forward * (walkDistance * walkEase.Evaluate(w));

                // Loading starts inside the move, so the wait happens while there is
                // something to look at, and the move is never cut short by it.
                if (load == null && w >= loadStartsAt && !string.IsNullOrEmpty(matchScene))
                {
                    state = "loading";
                    load = SceneManager.LoadSceneAsync(matchScene);
                    if (load != null) load.allowSceneActivation = false;
                }

                yield return null;
            }

            if (load == null)
            {
                state = string.IsNullOrEmpty(matchScene) ? "done (no scene set)" : "done";
                yield break;
            }

            state = "handing over";
            load.allowSceneActivation = true;
        }
    }
}
