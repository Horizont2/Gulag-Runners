using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Anything that can produce one tick of intent: a keyboard, the on-screen controls,
    /// a replay, a bot, or later a packet from the other player.
    /// Keeping this behind an interface is what lets the same simulation run from a network
    /// input stream without touching the motor (docs/06).
    ///
    /// Read() hands back the buttons AND the attack stick together, in one value. They
    /// used to be separable and must not be: rollback replays a tick from a stored input,
    /// and a stick sampled outside that value would replay as whatever the thumb is doing
    /// now rather than what it did then.
    /// </summary>
    public interface IPlayerInputSource
    {
        TickInput Read();
    }
}
