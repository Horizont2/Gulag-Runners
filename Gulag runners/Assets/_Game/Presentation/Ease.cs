using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// The curves the interface moves on, in one place.
    ///
    /// TWENTY-TWO FILES WERE DOING THIS THEMSELVES. Counted, not guessed: that is how many
    /// of Presentation and Menu carry their own Lerp, SmoothDamp or InverseLerp, and there
    /// was no shared curve anywhere in the project. The cost of that is not duplication --
    /// it is that every transition is a small research project, so the ones that are not
    /// strictly necessary never get written. The menu's panels appear with SetActive, the
    /// result screen appears with SetActive, the health bar is assigned straight to its
    /// new value, and nothing fades between scenes, all for the same reason.
    ///
    /// So this is the cheap way to say the four things that keep coming up.
    ///
    /// EVERY TIME-BASED CALL TAKES deltaTime AND IS FRAMERATE INDEPENDENT. The usual
    /// `Lerp(a, b, 0.1f)` moves a tenth of the way per FRAME, so the same code is twice as
    /// fast on a 120 Hz phone as on a 60 Hz one -- which is exactly the kind of bug that
    /// reads as "it feels different on my device" and never gets traced. Towards() does the
    /// exponential properly: `seconds` is the time to cover 99% of the distance, whatever
    /// the frame rate.
    ///
    /// Nothing here allocates, nothing here is a coroutine, and nothing here keeps state.
    /// A thing that needs to remember where it is keeps its own float and passes it back
    /// in, which is what makes these safe to call from Update on a hundred widgets.
    /// </summary>
    public static class Ease
    {
        /// <summary>
        /// Move `now` towards `want`, covering 99% of the gap in `seconds`.
        ///
        /// The workhorse. Use it for anything chasing a target that can change while it is
        /// being chased -- a bar following health, a camera following a pose, a panel
        /// following open or shut. It never overshoots and it has no velocity to carry, so
        /// a target that jumps backwards is followed immediately rather than oscillated
        /// around, which is what a spring would do.
        /// </summary>
        public static float Towards(float now, float want, float seconds, float dt)
        {
            if (seconds <= 0.0001f || dt <= 0f) return want;
            // 4.6 is ln(100): the exponent that leaves 1% of the gap after `seconds`.
            return Mathf.Lerp(now, want, 1f - Mathf.Exp(-dt * 4.6f / seconds));
        }

        /// <summary>The same chase, for a colour.</summary>
        public static Color Towards(Color now, Color want, float seconds, float dt)
        {
            if (seconds <= 0.0001f || dt <= 0f) return want;
            return Color.Lerp(now, want, 1f - Mathf.Exp(-dt * 4.6f / seconds));
        }

        /// <summary>The same chase, for a point.</summary>
        public static Vector3 Towards(Vector3 now, Vector3 want, float seconds, float dt)
        {
            if (seconds <= 0.0001f || dt <= 0f) return want;
            return Vector3.Lerp(now, want, 1f - Mathf.Exp(-dt * 4.6f / seconds));
        }

        /// <summary>
        /// Slow at both ends, 0 to 1. The shape of something that starts and stops of its
        /// own accord: a panel arriving, a screen going dark.
        /// </summary>
        public static float InOut(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Fast, then settling. The shape of something that was PUSHED: a number landing, a
        /// bar snapping towards its mark, a card thrown onto the table.
        /// </summary>
        public static float Out(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>
        /// Slow, then arriving all at once. The shape of something falling, and the one to
        /// reach for least: in an interface it usually reads as lag rather than weight.
        /// </summary>
        public static float In(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        /// <summary>
        /// Overshoots and comes back once. For an arrival that should be FELT -- a numeral
        /// struck, a result landing -- and never for anything the player is about to read:
        /// text that is still moving is text nobody starts.
        /// </summary>
        public static float Back(float t, float by = 0.14f)
        {
            t = Mathf.Clamp01(t);
            float out1 = Out(t);
            return out1 + by * Mathf.Sin(t * Mathf.PI) * (1f - t);
        }
    }
}
