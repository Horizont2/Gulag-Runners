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
        Dodge = 18,

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
        BlockHold = 55,
        BlockImpact = 56,
        Parry = 57,
        Stagger = 58,
        Death = 59,

        // ---- match
        RoundStart = 70,
        Siren = 71,
        RoundEnd = 72
    }
}
