using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Anything that can produce one tick of intent: a keyboard, the on-screen controls,
    /// a replay, a bot, or later a packet from the other player.
    /// Keeping this behind an interface is what lets the same simulation run from a network
    /// input stream without touching the motor (docs/06).
    /// </summary>
    public interface IPlayerInputSource
    {
        InputFlags Read();
    }
}
