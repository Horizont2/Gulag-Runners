namespace GulagRunners.Game
{
    /// <summary>
    /// Every sound the game can make, as an id rather than a clip reference.
    ///
    /// The game asks for a SoundId; what that turns into is the backend's problem. That is the
    /// whole reason FMOD can be dropped in later without touching a single call site: the list
    /// below is the contract, and <see cref="IAudioBackend"/> is the only thing that changes.
    ///
    /// The combat entries exist before combat does, on purpose. A sound list that grows one
    /// entry at a time ends up organised by the order features were built; this one is organised
    /// by what the player is doing.
    /// </summary>
    public enum SoundId
    {
        None = 0,

        // ---- movement (docs/01: in the blind phase these are the only way to be found)
        FootstepQuiet = 10,
        FootstepLoud = 11,
        Jump = 12,
        LandSoft = 13,
        LandHard = 14,
        Climb = 15,
        LadderGrab = 16,
        LadderOut = 17,
        // 18 was Dodge. The dodge has gone with the stamina that paid for it, so
        // there is no such moment to be heard. The number is left as a gap rather
        // than reused, for the same reason as 55 and 56 below: a saved sound table
        // in a scene stores the value, not the name.

        // ---- loot (docs/03: the chest table is a noise table)
        ChestPryQuiet = 30,
        ChestPryLoud = 31,
        ChestOpen = 32,
        ItemLand = 33,
        ItemPickup = 34,
        ItemDrop = 35,
        WeaponBreak = 36,

        // ---- combat
        AttackSwingLight = 50,
        AttackSwingHeavy = 51,
        AttackHit = 52,
        AttackHitArmoured = 53,
        AttackWhiff = 54,
        // 55 and 56 were BlockHold and BlockImpact. docs/31 took the guard out and
        // made the weapon the defence, so neither moment exists to be heard. The
        // numbers are left as a gap rather than reused: a saved sound table in a
        // scene stores the value, and handing 56 to a different sound would make
        // every arena play a parry where it used to play a block.
        Parry = 57,
        Stagger = 58,
        Death = 59,

        // ---- match
        RoundStart = 70,
        Siren = 71,
        RoundEnd = 72,

        /// <summary>The pressure building under the floor, three seconds before the gas.</summary>
        PoisonWarn = 73,

        /// <summary>The clank and the hiss of it coming out. Heard through every floor.</summary>
        PoisonVent = 74
    }
}
