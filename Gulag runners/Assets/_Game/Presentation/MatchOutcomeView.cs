using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// What happens in the second somebody wins.
    ///
    /// Before this there was nothing. <see cref="MatchState.Ended"/> is raised on the tick
    /// the match is decided and had NO subscribers at all: the loser's health reached zero,
    /// both fighters carried on standing in whatever pose they were in, the clock ran out
    /// its Over phase, and the next round dealt. The fight stopped rather than finished.
    ///
    /// Four things, in the order they read:
    ///
    ///   the freeze    a beat on the last blow, longer than an ordinary hit stop, because
    ///                 the thing it is punctuating is the end of the match rather than a hit.
    ///   slow motion   ramped back to full over about a second. The simulation keeps
    ///                 ticking at sixty; what changes is how much real time a tick takes,
    ///                 so both devices still run the SAME ticks in the same order and the
    ///                 rollback layer never notices (docs/06). Nothing here may touch the
    ///                 tick count, and nothing here does.
    ///   the camera    pushes in on the winner. Not a cut — a cut in a game with one
    ///                 camera reads as a bug.
    ///   the bodies    the winner plays a state you name; the loser goes down.
    ///
    /// GOING DOWN IS DONE HERE AND NOT IN THE ANIMATOR ON PURPOSE. GR_Player has no death
    /// clip — the pack has none — so PlayerAnimator falls through to the stagger pose and
    /// the loser stands there looking fine. Laying the body over is a rotation of the
    /// visual root, costs no asset, and reads as a knockout from across the arena. Drop a
    /// death clip in later and set Loser State to it; then turn Lay The Loser Down off.
    ///
    /// Everything it does is a field in the inspector, and the UnityEvents are where a
    /// results panel hangs — this creates no UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchOutcomeView : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The match. Found in the scene when empty.")]
        public MatchState match;

        [Tooltip("The camera that pushes in. The main camera when empty.")]
        public Camera view;

        [Header("The beat")]
        [Tooltip("Seconds everything holds still on the finishing blow, at real time. " +
                 "Longer than a hit stop because it is punctuating the match, not a hit.")]
        [Range(0f, 1f)] public float freeze = 0.22f;

        [Tooltip("How slow it goes once the freeze lets go. 1 is no slow motion.")]
        [Range(0.05f, 1f)] public float slowMotion = 0.35f;

        [Tooltip("Seconds the slow motion takes to ramp back to full speed.")]
        [Range(0f, 4f)] public float slowSeconds = 1.1f;

        [Header("The camera")]
        [Tooltip("Metres the camera moves towards the winner. 0 leaves it where it is.")]
        [Range(0f, 6f)] public float pushIn = 1.6f;

        [Tooltip("Seconds the push takes. It runs on unscaled time, so the slow motion " +
                 "does not also slow the camera down to a crawl.")]
        [Range(0.1f, 4f)] public float pushSeconds = 1.3f;

        public AnimationCurve pushEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("The bodies")]
        [Tooltip("Animator state the winner plays. Empty leaves him alone.")]
        public string winnerState = "";

        [Tooltip("Animator state the loser plays. Empty leaves whatever PlayerAnimator " +
                 "chose, which with no death clip in the controller is the stagger pose.")]
        public string loserState = "";

        [Tooltip("Lay the loser's model over, since there is no death clip. Turn this off " +
                 "the day Loser State names a real one.")]
        public bool layTheLoserDown = true;

        [Range(0f, 110f)] public float fallDegrees = 82f;
        [Range(0.05f, 2f)] public float fallSeconds = 0.45f;

        [Tooltip("Metres the fallen body settles by, so a model pivoted at the feet does " +
                 "not hang in the air once it is on its side.")]
        [Range(0f, 1f)] public float fallSink = 0.12f;

        [Header("Hooks")]
        [Tooltip("The winner's index, or 2 for a draw. Wire a results panel here.")]
        public UnityEvent<int> onEnded;
        public UnityEvent onDraw;

        [Tooltip("Fires once the whole sequence has played out and speed is back to normal.")]
        public UnityEvent onSettled;

        [Header("Debug")]
        public string state = "waiting";

        Coroutine _running;
        float _scaleWas = 1f;

        // The body that went down, and how it stood before it did. Without these the next
        // round deals with one fighter still lying on his side.
        Transform _fallen;
        Quaternion _stoodRotation;
        Vector3 _stoodPosition;

        void Reset() => match = FindFirstObjectByType<MatchState>();

        void OnEnable()
        {
            if (match == null) match = FindFirstObjectByType<MatchState>();
            if (view == null) view = Camera.main;

            if (match != null)
            {
                match.Ended += OnEnded;
                match.RoundBegan += OnRoundBegan;
            }
            else
            {
                Debug.LogWarning($"{name}: no MatchState in the scene, so the end of a " +
                                 "match will pass unmarked.", this);
            }
        }

        void OnDisable()
        {
            if (match != null)
            {
                match.Ended -= OnEnded;
                match.RoundBegan -= OnRoundBegan;
            }
            Restore();
        }

        /// <summary>A new round: whatever the last one left behind goes back.</summary>
        void OnRoundBegan(int round)
        {
            if (_running != null) { StopCoroutine(_running); _running = null; }
            Restore();
            state = "waiting";
        }

        void OnEnded(int winner)
        {
            onEnded?.Invoke(winner);
            if (winner == 2) onDraw?.Invoke();

            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Finish(winner));
        }

        IEnumerator Finish(int winner)
        {
            PlayerController won = Fighter(winner);
            PlayerController lost = Loser(winner);

            Pose(won, winnerState);
            Pose(lost, loserState);

            _scaleWas = Time.timeScale;

            // The freeze, on UNSCALED time: it is the thing setting the time scale, so it
            // cannot also be measured by it.
            state = "freeze";
            if (freeze > 0f)
            {
                Time.timeScale = 0f;
                yield return new WaitForSecondsRealtime(freeze);
            }

            state = "slow";
            Transform cam = view != null ? view.transform : null;
            Vector3 camFrom = cam != null ? cam.position : Vector3.zero;
            Vector3 toward = Toward(cam, won);

            Transform body = lost != null && lost.visualRoot != null ? lost.visualRoot : null;
            if (body != null && layTheLoserDown)
            {
                _fallen = body;
                _stoodRotation = body.localRotation;
                _stoodPosition = body.localPosition;
            }
            Quaternion fellFrom = body != null ? body.localRotation : Quaternion.identity;
            Vector3 sankFrom = body != null ? body.localPosition : Vector3.zero;
            int side = Side(lost, won);

            float longest = Mathf.Max(slowSeconds, Mathf.Max(pushSeconds, fallSeconds));
            for (float t = 0f; t < longest; t += Time.unscaledDeltaTime)
            {
                if (slowSeconds > 0f)
                {
                    float k = Mathf.Clamp01(t / slowSeconds);
                    Time.timeScale = Mathf.Lerp(slowMotion, _scaleWas, k * k);
                }
                else Time.timeScale = _scaleWas;

                if (cam != null && pushIn > 0f && pushSeconds > 0f)
                {
                    float k = pushEase.Evaluate(Mathf.Clamp01(t / pushSeconds));
                    cam.position = camFrom + toward * (pushIn * k);
                }

                // Written from the coroutine, which resumes AFTER every Update has run --
                // including PlayerController's, which rewrites this same rotation from the
                // simulation every frame. Later write wins, so the body stays down.
                if (body != null && layTheLoserDown && fallSeconds > 0f)
                {
                    float k = Mathf.Clamp01(t / fallSeconds);
                    float eased = 1f - (1f - k) * (1f - k);          // falls, does not float
                    body.localRotation =
                        fellFrom * Quaternion.Euler(0f, 0f, fallDegrees * eased * side);
                    body.localPosition = sankFrom - new Vector3(0f, fallSink * eased, 0f);
                }

                yield return null;
            }

            Time.timeScale = _scaleWas;
            state = "settled";
            onSettled?.Invoke();

            // And keep holding him down. The ramp is over long before the Over phase is,
            // and PlayerController would quietly stand him back up on the next frame.
            if (body != null && layTheLoserDown)
            {
                Quaternion down = fellFrom * Quaternion.Euler(0f, 0f, fallDegrees * side);
                Vector3 sunk = sankFrom - new Vector3(0f, fallSink, 0f);
                while (true)
                {
                    body.localRotation = down;
                    body.localPosition = sunk;
                    yield return null;
                }
            }

            _running = null;
        }

        /// <summary>Which way the camera travels: towards the winner, flattened.</summary>
        Vector3 Toward(Transform cam, PlayerController won)
        {
            if (cam == null) return Vector3.zero;
            if (won == null) return cam.forward;

            Vector3 at = won.transform.position - cam.position;
            at.y = 0f;
            return at.sqrMagnitude > 0.0001f ? at.normalized : cam.forward;
        }

        /// <summary>Which way a body falls: away from whoever hit it last.</summary>
        static int Side(PlayerController lost, PlayerController won)
        {
            if (lost == null || won == null) return 1;
            return lost.transform.position.x >= won.transform.position.x ? 1 : -1;
        }

        PlayerController Fighter(int index)
        {
            if (match == null || match.fighters == null) return null;
            if (index < 0 || index >= match.fighters.Count) return null;
            return match.fighters[index];
        }

        /// <summary>Everyone who is not the winner. With two fighters, the other one.</summary>
        PlayerController Loser(int winner)
        {
            if (match == null || match.fighters == null) return null;
            for (int i = 0; i < match.fighters.Count; i++)
                if (i != winner) return match.fighters[i];
            return null;
        }

        static void Pose(PlayerController who, string stateName)
        {
            if (who == null || string.IsNullOrEmpty(stateName)) return;

            PlayerAnimator anim = who.GetComponentInChildren<PlayerAnimator>(true);
            if (anim == null || anim.animator == null) return;

            int hash = Animator.StringToHash(stateName);
            if (!anim.animator.HasState(0, hash))
            {
                Debug.LogWarning($"{who.name}: the controller has no state called " +
                                 $"\"{stateName}\", so the end of the match leaves him in " +
                                 "whatever pose he was already holding.", who);
                return;
            }
            anim.animator.Play(hash, 0, 0f);
        }

        /// <summary>Speed back to normal and the loser back on his feet.</summary>
        void Restore()
        {
            if (Time.timeScale <= 0f || !Mathf.Approximately(Time.timeScale, _scaleWas))
                Time.timeScale = _scaleWas > 0f ? _scaleWas : 1f;

            if (_fallen != null)
            {
                _fallen.localRotation = _stoodRotation;
                _fallen.localPosition = _stoodPosition;
                _fallen = null;
            }
        }
    }
}
