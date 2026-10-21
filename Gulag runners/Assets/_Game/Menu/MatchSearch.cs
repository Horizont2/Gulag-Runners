using UnityEngine;
using UnityEngine.Events;
using GulagRunners.Meta;

namespace GulagRunners.Game
{
    /// <summary>
    /// Something that knows whether the other runner has finished loading.
    ///
    /// There is no net layer yet, and this is the shape of the hole where one goes. The
    /// alternative was to invent a wait -- spin for a plausible second and call it the
    /// opponent -- which is the same lie as a progress bar that is not measuring anything,
    /// and it would have to be found and removed the day a real one arrives.
    /// </summary>
    public interface IMatchGate
    {
        /// <summary>True once the other side says it is in.</summary>
        bool OpponentReady { get; }
    }

    /// <summary>
    /// The search screen, from pressing PLAY to the arena appearing.
    ///
    /// ONE screen with five phases, and the bar is a different measurement in each. That
    /// is the whole design. A single bar pretending to be progress while nothing is
    /// loading is the oldest lie on a loading screen, and a player who waits through it
    /// twice stops reading it:
    ///
    ///   Searching  the net being cast, 0 at your own rating and 1 at the widest it goes.
    ///              A search has no length, so this measures the one thing that IS moving.
    ///   Found      full, held a beat -- long enough to read who, short enough not to wait.
    ///   Loading    the real one. MenuDirector's LoadSceneAsync progress and nothing else.
    ///   Waiting    full and still. Their load, not yours.
    ///   Starting   the hand-off.
    ///
    /// It creates nothing and draws nothing: the readouts go out as UnityEvents, exactly
    /// like <see cref="MenuUI"/>, so the screen can be drawn by uGUI today and by
    /// something else later. Wire Phase Changed to a MenuLabel.Set, Bar Filled to a
    /// MenuBar.Fill, and the cancel button to Cancel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchSearch : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Runs the walk through the gate and loads the arena. Found when empty.")]
        public MenuDirector director;

        [Tooltip("The ladder and the purse, for who is searching. Found when empty.")]
        public PlayerProgress progress;

        [Tooltip("Optional. Something implementing IMatchGate.\\n\\n" +
                 "Empty means the opponent is ready the moment this side is, which is the " +
                 "truth while there is no net layer: there IS nobody else. The waiting " +
                 "phase then flashes past rather than being faked, and that is correct.")]
        public MonoBehaviour opponentGate;

        [Tooltip("Optional. The stills behind it all.")]
        public MenuBackdrop backdrop;

        [Header("Rules")]
        public SearchRules rules = SearchRules.Default;

        [Tooltip("Seconds the found opponent is shown before the walk begins.")]
        [Range(0.2f, 3f)] public float foundSeconds = 1.1f;

        [Header("Tips")]
        [Tooltip("Shown one at a time, top left. The other half of what docs/09 asks the " +
                 "daily contracts to do: teach the mechanics. A player stares at this " +
                 "screen twice a match and at nothing else while they wait.")]
        [TextArea(2, 4)]
        public string[] tips =
        {
            "A SIREN AT 0:25\nGIVES A DIRECTION,\nNEVER A POSITION.",
            "A GUARD HELD TOO LONG\nBREAKS. THE SECOND\nAFTER IS WHEN YOU DIE.",
            "BARE HANDS HIT HARDER\nBELOW A THIRD OF YOUR\nHEALTH. SO DO THEIRS.",
            "A CHEST YOU OPEN IS\nA SOUND THEY HEAR.\nOPEN IT ANYWAY.",
            "THE CEILING COMES DOWN\nAT 1:00. THE ROOMS SHUT\nBEFORE IT DOES.",
            "A PARRY COSTS NOTHING\nAND BUYS SEVEN FRAMES.\nA BLOCK COSTS STAMINA.",
        };

        [Tooltip("Seconds each tip is held.")]
        [Range(2f, 12f)] public float tipSeconds = 5f;

        [Header("Readouts")]
        [Tooltip("\"SEARCHING\", \"OPPONENT FOUND\", \"LOADING THE ARENA\"...")]
        public UnityEvent<string> phaseChanged;

        [Tooltip("The line under it: the league and the seconds, or the seed.")]
        public UnityEvent<string> detailChanged;

        [Tooltip("0 to 1. What it MEANS depends on the phase — see the class comment.")]
        public UnityEvent<float> barFilled;

        [Tooltip("The tip, top left.")]
        public UnityEvent<string> tipChanged;

        [Tooltip("True while this screen should be on at all.")]
        public UnityEvent<bool> shownChanged;

        [Header("Debug")]
        public SearchPhase phase = SearchPhase.Cancelled;
        public float elapsed;

        IMatchGate _gate;
        bool _running;
        float _held;
        int _tip = -1;
        float _tipHeld;

        /// <summary>No gate wired means nobody to wait for, and that is not a stub.</summary>
        bool OpponentReady => _gate == null || _gate.OpponentReady;

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<MenuDirector>();
            if (progress == null) progress = PlayerProgress.Instance;
            if (progress == null) progress = FindFirstObjectByType<PlayerProgress>();
            _gate = opponentGate as IMatchGate;

            if (opponentGate != null && _gate == null)
                Debug.LogWarning($"{name}: {opponentGate.GetType().Name} is in the " +
                                 "Opponent Gate slot but does not implement IMatchGate, so " +
                                 "nothing is being waited for.", this);

            shownChanged?.Invoke(false);
        }

        /// <summary>Wire PLAY to this instead of to MenuDirector.Play.</summary>
        public void Begin()
        {
            if (_running) return;

            _running = true;
            elapsed = 0f;
            _held = 0f;
            _tip = -1;
            _tipHeld = float.MaxValue;      // forces the first tip on the first frame
            Enter(SearchPhase.Searching);
            shownChanged?.Invoke(true);
        }

        /// <summary>
        /// The cross, top right.
        ///
        /// Only while there is something to come back from. Once the arena is loading, the
        /// other runner is already committed to this match: letting one side walk out of a
        /// match the other has joined is how a ladder learns to hate you.
        /// </summary>
        public void Cancel()
        {
            if (!_running) return;
            if (phase != SearchPhase.Searching && phase != SearchPhase.Found) return;

            _running = false;
            Enter(SearchPhase.Cancelled);
            shownChanged?.Invoke(false);
            if (director != null) director.Rewind();
        }

        void Update()
        {
            if (!_running) return;

            elapsed += Time.deltaTime;
            Tips();

            switch (phase)
            {
                case SearchPhase.Searching: Searching(); break;
                case SearchPhase.Found: Found(); break;
                case SearchPhase.Loading: Loading(); break;
                case SearchPhase.Waiting: Waiting(); break;
            }
        }

        void Searching()
        {
            int played = progress != null ? progress.matchesPlayed : 0;
            double rating = progress != null ? progress.rating : 1500.0;

            Say(Matchmaker.Detail(rating, elapsed, played));
            Fill(Matchmaker.Cast(rules, elapsed));

            // Nothing else is looking yet -- there is no queue to ask. So the only
            // opponent this can honestly produce is the one docs/09 already promises:
            // a bot, offered immediately for the first five matches and otherwise once
            // the net is fully cast and the clock has run out.
            if (!Matchmaker.OfferBot(rules, played, elapsed)) return;

            _held = 0f;
            Enter(SearchPhase.Found);
            Say(played < 5 ? "TRAINING OPPONENT" : "A BOT, WHILE THE QUEUE IS THIN");
            Fill(1f);
        }

        void Found()
        {
            _held += Time.deltaTime;
            if (_held < foundSeconds) return;

            Enter(SearchPhase.Loading);
            if (director != null)
            {
                director.waitForRelease = true;
                director.Play();
            }
        }

        void Loading()
        {
            if (director == null) { Enter(SearchPhase.Starting); Done(); return; }

            Say(director.state);
            Fill(director.LoadProgress);

            if (!director.ReadyToHandOver) return;
            Enter(OpponentReady ? SearchPhase.Starting : SearchPhase.Waiting);
            if (phase == SearchPhase.Starting) Done();
        }

        void Waiting()
        {
            Say("");
            Fill(1f);
            if (!OpponentReady) return;
            Enter(SearchPhase.Starting);
            Done();
        }

        void Done()
        {
            Fill(1f);
            _running = false;
            if (director != null) director.Release();
        }

        void Tips()
        {
            if (tips == null || tips.Length == 0) return;

            _tipHeld += Time.deltaTime;
            if (_tipHeld < Mathf.Max(1f, tipSeconds)) return;

            _tipHeld = 0f;
            // Walked rather than rolled: a random pick repeats, and a repeat on a screen
            // somebody sees twenty times a session reads as the game being broken.
            _tip = (_tip + 1) % tips.Length;
            tipChanged?.Invoke(tips[_tip]);

            if (backdrop != null) backdrop.Next();
        }

        void Enter(SearchPhase next)
        {
            phase = next;
            phaseChanged?.Invoke(Label(next));
        }

        void Say(string detail) => detailChanged?.Invoke(detail);
        void Fill(float share) => barFilled?.Invoke(Mathf.Clamp01(share));

        static string Label(SearchPhase p) => p switch
        {
            SearchPhase.Searching => "SEARCHING",
            SearchPhase.Found => "OPPONENT FOUND",
            SearchPhase.Loading => "LOADING THE ARENA",
            SearchPhase.Waiting => "WAITING FOR THE OTHER RUNNER",
            SearchPhase.Starting => "STARTING",
            _ => "",
        };
    }
}
