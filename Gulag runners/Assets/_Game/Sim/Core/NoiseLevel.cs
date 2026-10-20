namespace GulagRunners.Sim
{
    /// <summary>
    /// How loud an action is.
    /// In this game sound is not decoration: players never see each other during the scavenge
    /// phase, so noise is the main channel of information (docs/01-concept.md). That makes
    /// loudness part of the simulation state, not a property of an audio clip — otherwise the
    /// two clients would "hear" different things and the match would be unfair.
    /// </summary>
    public enum NoiseLevel : byte
    {
        Silent = 0,  // crouch-walking, standing still
        Quiet  = 1,  // climbing, soft landing
        Medium = 2,  // dodging, normal landing
        Loud   = 3   // running, hard landing
    }
}
