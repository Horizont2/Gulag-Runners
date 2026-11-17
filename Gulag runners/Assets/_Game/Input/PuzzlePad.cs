using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The thumb on a lock (docs/04).
    ///
    /// One tap, one option, one tick. The option rides in the input flags like every other
    /// intent, because rollback replays a tick from its stored input and anything the
    /// simulation read from somewhere else is a desync waiting for a bad connection.
    ///
    /// It asserts ACTION along with the option, and that is not a convenience: a lock is
    /// worked on by a fighter who is standing at the chest with the button down, and asking
    /// for a second thumb on ACTION while the first one picks out a rune is asking for a grip
    /// nobody has on a phone. A tap on the lock IS the whole input.
    ///
    /// The tap survives exactly one simulation tick. A held button is a state the fighter is
    /// in; a tap is a thing that happened, and a thing that happened twice because the frame
    /// rate is higher than the tick rate is a wrong answer nobody gave.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzlePad : MonoBehaviour, IPlayerInputSource
    {
        int _option;
        bool _fresh;

        /// <summary>What the UI calls when a rune, a ring or a gear is tapped. 1..7.</summary>
        public void Touch(int option)
        {
            if (option < 1 || option > 7) return;
            _option = option;
            _fresh = true;
        }

        /// <summary>Nothing is pending. Called when the lock goes away under the thumb.</summary>
        public void Forget()
        {
            _option = 0;
            _fresh = false;
        }

        public TickInput Read()
        {
            if (!_fresh || _option == 0) return TickInput.None;

            // Spent on the way out: the simulation reads a source once a tick, and a tap that
            // answered one tick must not answer the next one as well.
            _fresh = false;
            int option = _option;
            _option = 0;
            return new TickInput(InputFlags.Action.WithChoice(option));
        }
    }
}
