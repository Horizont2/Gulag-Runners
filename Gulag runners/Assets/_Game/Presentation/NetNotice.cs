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

        float _hold;

        void Reset() => target = GetComponent<TMP_Text>();

        void Awake()
        {
            if (target == null) target = GetComponent<TMP_Text>();
            Write("", waiting);
        }

        void Update()
        {
            if (_hold <= 0f) return;
            _hold -= Time.unscaledDeltaTime;     // a held line outlives a hit stop
            if (_hold <= 0f) Write("", waiting);
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
    }
}
