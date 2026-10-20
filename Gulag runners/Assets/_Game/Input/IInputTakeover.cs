namespace GulagRunners.Game
{
    /// <summary>
    /// An input source that answers ALONE while it is switched on.
    ///
    /// <see cref="PlayerInputRouter"/> merges its sources, which is right for a keyboard and
    /// a phone being live at once and wrong for a bot: a fighter driven by both at the same
    /// time holds whatever either of them presses, which is a fighter who will not stop
    /// walking. So a source can say it has taken the fighter over, and the router then stops
    /// reading anything else.
    /// </summary>
    public interface IInputTakeover
    {
        bool TakingOver { get; }
    }
}
