using TMPro;
using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// One line of text, behind a METHOD.
    ///
    /// <see cref="MenuUI"/> sends its readouts out as UnityEvents and references no uGUI
    /// type at all, which is what keeps the menu free to be drawn by something else later.
    /// A UnityEvent needs something to call, though, and a C# property setter is not a
    /// method a persistent listener can be given from an editor script without reflection.
    ///
    /// So: a method. Three lines of component, and the wiring in the inspector then reads
    /// "goldChanged -> Gold label -> Set", which is a sentence rather than a riddle.
    ///
    /// Its own file, like every MonoBehaviour here. Unity resolves a behaviour by matching
    /// the class to the FILE NAME, and three of these once shared one called MenuWidgets.cs
    /// -- which the scene then deserialised onto whichever object it liked, putting a rank
    /// badge on the gold label and leaving the real one unwired.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuLabel : MonoBehaviour
    {
        [Tooltip("The text to write into. Left empty, one is looked for on this object.")]
        public TMP_Text target;

        [Tooltip("What it says while the game is not running, so the screen can be laid " +
                 "out against something that looks like real content.")]
        public string placeholder = "";

        void Reset() => target = GetComponent<TMP_Text>();

        void Awake()
        {
            if (target == null) target = GetComponent<TMP_Text>();
        }

        [Tooltip("Count the digits up instead of replacing them. A reward that lands as " +
                 "\"+450\" is a number; one that runs up to +450 is a reward.\n\n" +
                 "Only when the two strings have the same shape -- the same words around " +
                 "the same count of numbers. \"+120\" to \"+450\" rolls; \"SEARCHING\" " +
                 "to \"FOUND\" cannot and is simply written.")]
        public bool roll = true;

        [Tooltip("Seconds a roll takes.")]
        [Range(0.05f, 2f)] public float rollSeconds = 0.5f;

        string _from;
        string _to;
        float _t = 1f;

        public void Set(string value)
        {
            if (target == null) target = GetComponent<TMP_Text>();
            if (target == null || value == null) return;

            string now = _t < 1f ? _to : target.text;
            if (now == value) return;

            if (roll && Application.isPlaying && Shaped(now, value))
            {
                _from = now;
                _to = value;
                _t = 0f;
                return;
            }

            _t = 1f;
            target.text = value;
        }

        void Update()
        {
            if (_t >= 1f) return;

            _t += Time.unscaledDeltaTime / Mathf.Max(0.01f, rollSeconds);
            if (_t >= 1f)
            {
                _t = 1f;
                target.text = _to;      // land exactly on it, never on a rounded guess
                return;
            }

            target.text = Between(_from, _to, Ease.Out(_t));
        }

        /// <summary>
        /// Can these two be rolled between: same text around the same number of numbers.
        ///
        /// The check is the whole safety of this. Without it a label going from "SEARCHING"
        /// to "FOUND" would be asked to count between things that are not counts, and a
        /// score going from "2-1" to "ROUND OVER" would come out as neither.
        /// </summary>
        static bool Shaped(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (Skeleton(a) != Skeleton(b)) return false;

            var was = Numbers(a);
            var now = Numbers(b);
            if (was.Count == 0 || was.Count != now.Count) return false;

            // EVERY NUMBER HAS TO MOVE THE SAME WAY. Each one is rolled on its own, so a
            // pair going in opposite directions passes through states that never existed:
            // a clock from 09:58 to 10:02 reads 10:30 half way, because the minutes run
            // backwards from 58 to 02 while the hours go up. Mixed directions mean the
            // numbers are not one quantity, and a roll is a lie about one quantity.
            int sign = 0;
            for (int i = 0; i < was.Count; i++)
            {
                if (now[i] == was[i]) continue;
                int way = now[i] > was[i] ? 1 : -1;
                if (sign == 0) sign = way;
                else if (sign != way) return false;
            }
            return sign != 0;
        }

        /// <summary>The string with every run of digits replaced by one mark.</summary>
        static string Skeleton(string s)
        {
            var sb = new System.Text.StringBuilder();
            bool inRun = false;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) { if (!inRun) { sb.Append('#'); inRun = true; } }
                else { sb.Append(c); inRun = false; }
            }
            return sb.ToString();
        }

        static System.Collections.Generic.List<long> Numbers(string s)
        {
            var found = new System.Collections.Generic.List<long>();
            long run = 0;
            bool inRun = false;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) { run = run * 10 + (c - '0'); inRun = true; }
                else if (inRun) { found.Add(run); run = 0; inRun = false; }
            }
            if (inRun) found.Add(run);
            return found;
        }

        /// <summary>
        /// The same text with each number part of the way from one to the other.
        ///
        /// Leading zeros are kept only where the ending string has them -- a clock wants
        /// them, a reward does not. Padding everything to the final width turned "+9" on
        /// its way to "+120" into "+064", which reads as a different number rather than
        /// as a count.
        /// </summary>
        static string Between(string from, string to, float k)
        {
            var a = Numbers(from);
            var b = Numbers(to);
            if (a.Count != b.Count) return to;

            var sb = new System.Text.StringBuilder();
            int at = 0;
            bool inRun = false;

            for (int i = 0; i < to.Length; i++)
            {
                char c = to[i];
                if (char.IsDigit(c))
                {
                    if (inRun) continue;
                    inRun = true;

                    int width = 0;
                    for (int j = i; j < to.Length && char.IsDigit(to[j]); j++) width++;

                    long now = (long)System.Math.Round(a[at] + (b[at] - a[at]) * (double)k);

                    // Leading zeros only where the ENDING string has them. Padding
                    // everything to the final width turned "+9" on its way to "+120" into
                    // "+064", which reads as a different number rather than as a count.
                    bool pad = width > 1 && to[i] == '0';
                    at++;
                    sb.Append(pad ? now.ToString().PadLeft(width, '0') : now.ToString());
                }
                else { inRun = false; sb.Append(c); }
            }
            return sb.ToString();
        }
    }
}
