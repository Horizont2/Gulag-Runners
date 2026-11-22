using TMPro;
using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// The one line on the HUD that speaks for the NETWORK.
    ///
    /// <see cref="GulagRunners.Net.NetMatch"/> has told the world two things since it was
    /// written and nothing has ever drawn either of them: that the other player has stopped
    /// answering, and that a bot has taken their fighter for good. A match where the screen
    /// freezes and says nothing is a hang; the same freeze with a line on it is a wait, and
    /// those are different experiences of the same two seconds.
    ///
    /// Both go through one label on purpose. They are the same subject -- what the other end
    /// of the wire is doing -- and they never need to be read at once: the substitution is
    /// what the wait turns INTO.
    ///
    /// Which is also the one piece of ordering that matters here. The bot takes over and the
    /// wait ends on the same beat, so NetMatch raises Substituted and then clears the waiting
    /// line, and a naive label would show the news for a single frame before wiping it. So a
    /// held line wins: nothing may overwrite it until its time is up.
    ///
    /// IT INSTALLS ITSELF WHEN THE SCENE HAS NOT GOT ONE. HudBuilder makes this label and
    /// wires both events, and has done since it was written -- and ui_check still reports
    /// WaitingChanged and Substituted going nowhere in all four arenas, because the scenes
    /// are older than the tool and nobody has re-run it. That is the same drift that left
    /// seventeen labels unable to write a word (see UiType), and it is worth closing the
    /// same way: if a scene has a network match and no notice, one is built on load and
    /// subscribed in code. A scene that WAS built properly keeps its own, untouched --
    /// the test is whether the event already has a listener, not whether it looks wired.
    ///
    /// The third thing it says is newer than either: the link refusing to connect. An
    /// arena the two devices do not agree on is caught at the handshake now
    /// (LanLink.WorldFingerprint), and the reason lands in Desync -- which is a field, with
    /// no event behind it, so it is read rather than listened for.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetNotice : MonoBehaviour
    {
        [Tooltip("The line. Leave empty to use the TMP_Text on this object.")]
        public TMP_Text target;

        [Tooltip("What to say when a bot takes the other fighter over. No countdown and no "
                 + "apology: it is a fact about whose match this now is.")]
        public string substitutedLine = "OPPONENT LEFT — A BOT HAS THEIR FIGHTER";

        [Tooltip("How long that stays up, in seconds. Long enough to read twice, short "
                 + "enough that it is gone before the next fight starts.")]
        [Range(1f, 12f)] public float holdSeconds = 4.5f;

        [Header("Colours")]
        [Tooltip("A wait: something is wrong and may yet come right.")]
        public Color waiting = new Color(0.95f, 0.66f, 0.23f);

        [Tooltip("The substitution: not a warning any more, just how things now are.")]
        public Color settled = new Color(0.93f, 0.95f, 0.97f);

        [Tooltip("The link refusing to connect. Not a wait and not a fact to live with: "
                 + "the match is not going to happen.")]
        public Color refused = new Color(1.00f, 0.27f, 0.22f);

        [Tooltip("What to say once a rematch has been asked for.\n\n" +
                 "ASKED, by this player: MatchResultView raises RematchAsked when Again " +
                 "is pressed on THIS phone and something else owns the restart, because " +
                 "both devices have to deal the new match on the same tick. So the line " +
                 "is about waiting for the other end to agree, not about them wanting " +
                 "one -- and without it Again looks like a button that does nothing for " +
                 "half a second.")]
        public string rematchLine = "REMATCH ASKED FOR — WAITING FOR THEM";

        [Tooltip("The link to read a refusal off. Leave empty to find the one in the scene.")]
        public GulagRunners.Net.LanLink link;

        float _hold;
        bool _said;                              // a refusal is said once and then stands

        void Reset() => target = GetComponent<TMP_Text>();

        void Awake()
        {
            if (target == null) target = GetComponent<TMP_Text>();
            if (link == null)
                link = FindFirstObjectByType<GulagRunners.Net.LanLink>(
                    FindObjectsInactive.Include);
            Write("", waiting);
            Attach();
        }

        /// <summary>
        /// Listens for whatever the scene is not already carrying.
        ///
        /// Per event, not all or nothing, because the three are wired by two different
        /// tools and a scene can easily have one and not the others.
        ///
        ///   WaitingChanged, Substituted   HudBuilder wires both to this component. Added
        ///       here only when NOTHING is on them, or a built scene would write every
        ///       line twice.
        ///   RematchAsked                  NetSetup wires it to NetMatch.AskRematch, which
        ///       is the request and not the caption, so this is added whatever is there.
        ///       The request itself is added only if the event is bare -- see below, where
        ///       an unwired Again is a button that silently does nothing.
        /// </summary>
        void Attach()
        {
            GulagRunners.Net.NetMatch net =
                FindFirstObjectByType<GulagRunners.Net.NetMatch>(FindObjectsInactive.Include);

            if (net != null)
            {
                if (net.WaitingChanged.GetPersistentEventCount() == 0)
                    net.WaitingChanged.AddListener(Say);
                if (net.Substituted.GetPersistentEventCount() == 0)
                    net.Substituted.AddListener(Substituted);
            }

            MatchResultView result =
                FindFirstObjectByType<MatchResultView>(FindObjectsInactive.Include);
            if (result == null) return;

            result.RematchAsked.AddListener(Rematch);

            if (net != null && result.RematchAsked.GetPersistentEventCount() == 0)
            {
                result.RematchAsked.AddListener(net.AskRematch);
                Debug.LogWarning(
                    "NetNotice: nothing was wired to the result screen's Again button, so "
                    + "asking for a rematch would have done nothing on this device. "
                    + "NetMatch.AskRematch has been attached at runtime. Run Tools > "
                    + "Gulag Runners > Setup > Set up two-phone play in this scene to "
                    + "make it part of the scene.", result);
            }
        }

        void Update()
        {
            // The refusal first, and it outranks everything: a held line is news about a
            // match that is running, and this is news that there is no match. Desync is
            // set once by the link and never cleared, which is the right shape for a line
            // that should stay on screen.
            if (!_said && link != null && !string.IsNullOrEmpty(link.Desync))
            {
                _said = true;
                _hold = 0f;
                Write(Shorten(link.Desync), refused);
                return;
            }
            if (_said) return;

            if (_hold <= 0f) return;
            _hold -= Time.unscaledDeltaTime;     // a held line outlives a hit stop
            if (_hold <= 0f) Write("", waiting);
        }

        /// <summary>The other player has asked for another round. MatchResultView.RematchAsked.</summary>
        public void Rematch()
        {
            _hold = holdSeconds;
            Write(rematchLine, settled);
        }

        /// <summary>
        /// The first clause of a refusal, in capitals.
        ///
        /// Desync is written to be read in a log, where the whole paragraph is the point.
        /// On a phone the first clause is what fits across the HUD, and the log still has
        /// the rest of it.
        /// </summary>
        static string Shorten(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "";
            int stop = reason.IndexOf(" (", System.StringComparison.Ordinal);
            if (stop < 0) stop = reason.IndexOf('.');
            string first = stop > 0 ? reason.Substring(0, stop) : reason;
            return first.ToUpperInvariant();
        }

        /// <summary>The waiting line, or empty when the wait is over. NetMatch.WaitingChanged.</summary>
        public void Say(string line)
        {
            if (_hold > 0f) return;              // the held line wins; see the summary
            Write(line, waiting);
        }

        /// <summary>A bot has the other fighter from here. NetMatch.Substituted.</summary>
        public void Substituted()
        {
            _hold = holdSeconds;
            Write(substitutedLine, settled);
        }

        void Write(string line, Color colour)
        {
            if (target == null) return;
            if (target.text != line) target.text = line;
            if (target.color != colour) target.color = colour;
        }

        // -------------------------------------------------- when the scene has not got one

        /// <summary>
        /// Builds a notice in a scene that has a network match and nothing drawing for it.
        ///
        /// AfterSceneLoad, so the NetMatch and the canvases are already awake. It does
        /// nothing at all in a scene with no NetMatch -- the menu -- and nothing in a scene
        /// that already has a NetNotice, which is what an arena rebuilt with HudBuilder
        /// has. So running the tool remains the better answer and this is only the floor
        /// under it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => Fit();
            Fit();
        }

        static void Fit()
        {
            if (FindFirstObjectByType<NetNotice>(FindObjectsInactive.Include) != null) return;

            GulagRunners.Net.NetMatch net =
                FindFirstObjectByType<GulagRunners.Net.NetMatch>(FindObjectsInactive.Include);
            if (net == null) return;

            TMP_Text line = Build();
            if (line == null) return;

            // Switched OFF while it is dressed. AddComponent wakes a behaviour then and
            // there, so fields assigned on the next line are assigned after its Awake has
            // already read them -- which would leave the first line it ever writes in the
            // colour of a default rather than of the palette.
            //
            // What it listens to is Attach's business and not this method's: a notice the
            // builder made needs exactly the same subscriptions as one made here, and
            // only one place should know what they are.
            GameObject holder = line.gameObject;
            holder.SetActive(false);

            NetNotice notice = holder.AddComponent<NetNotice>();
            notice.target = line;
            notice.waiting = UiPalette.Amber;
            notice.settled = UiPalette.Ink;
            notice.refused = UiPalette.Danger;

            holder.SetActive(true);
        }

        /// <summary>
        /// A line of its own on the busiest overlay canvas, under where the round label
        /// sits in a built HUD, so the two agree when one scene has both.
        /// </summary>
        static TMP_Text Build()
        {
            Canvas canvas = null;
            foreach (Canvas c in FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (canvas == null || c.sortingOrder > canvas.sortingOrder) canvas = c;
            }
            if (canvas == null) return null;

            GameObject holder = new GameObject("Net Notice");
            holder.transform.SetParent(canvas.transform, false);

            TextMeshProUGUI text = holder.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.alignment = TextAlignmentOptions.Top;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.fontSize = 30f;
            text.characterSpacing = UiType.Tracking;
            text.fontStyle = FontStyles.Bold;

            RectTransform rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(1400f, 60f);
            rt.anchoredPosition = new Vector2(0f, -170f);

            holder.transform.SetAsLastSibling();
            return text;
        }
    }
}
