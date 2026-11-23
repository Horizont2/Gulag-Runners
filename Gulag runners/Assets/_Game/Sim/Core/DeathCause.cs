namespace GulagRunners.Sim
{
    /// <summary>
    /// What took a fighter's last point of health.
    ///
    /// This was a hole in the simulation that two separate things ran into within an hour of
    /// each other, which is usually the sign that it was a hole rather than a nicety.
    ///
    /// docs/40 wanted it for the match file: a round that ends with somebody on zero was
    /// recorded as "knockout" whether a mace or the gas did it, and those are not the same
    /// round. docs/09 wanted it for one of the three daily tasks -- "kill with a hazard" --
    /// which cannot be awarded by a game that does not know whether the hazard killed
    /// anybody.
    ///
    /// It lives on <see cref="PlayerSimState"/> rather than being worked out in presentation
    /// because only the simulation knows, and because a guess from outside would be wrong in
    /// the one case that matters: a fighter the gas has been biting who is then finished by a
    /// blow, and a fighter a blow nearly finished who then drowns.
    ///
    /// Being simulation state it rolls back with everything else -- PlayerSimState is a
    /// struct and the rollback copies it whole -- so a death that is rewound is un-caused
    /// too.
    /// </summary>
    public enum DeathCause : byte
    {
        /// <summary>Still alive, or alive again after a rollback.</summary>
        None = 0,

        /// <summary>A weapon or a fist. The ordinary way to lose.</summary>
        Blow = 1,

        /// <summary>The floor, from high enough up. docs/02's fall damage.</summary>
        Fall = 2,

        /// <summary>The rising gas (docs/33). The arena winning, rather than the other man.</summary>
        Poison = 3,
    }
}
