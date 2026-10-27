namespace PowerGridManager
{
    /// <summary>
    /// A reusable power-management configuration: the load-shedding policy values and
    /// default priority a preset applies when activated, plus an optional charge-based
    /// automatic activation condition. Presets define rules; they never assign buildings
    /// to groups or switch anything by themselves.
    /// </summary>
    public sealed class PowerPreset
    {
        public string Name = "";
        public string Description = "";

        // --- Policy values the preset applies when activated -----------------------

        public float LoadShedLowThreshold = 0.25f;
        public float LoadShedRestoreThreshold = 0.55f;
        public int LoadShedStableTicks = PowerTime.TicksPerHour / 2;
        public int LoadShedCooldownTicks = PowerTime.TicksPerHour;
        public int LoadShedMaxActionsPerPass = 4;
        public LoadSheddingRecoveryMode LoadShedRecovery = LoadSheddingRecoveryMode.Staged;
        public bool LoadShedRequireDeficit = true;

        // --- Default priority for buildings the player later marks sheddable ------

        public int DefaultPriority = PowerManagementRegistry.DefaultPriority;
        public bool MarkNewSheddable = false;

        // --- Optional automatic activation ----------------------------------------

        /// <summary>Auto-activate when usable charge is at or below this, or -1 to disable.</summary>
        public float ActivateBelowCharge = -1f;

        /// <summary>Auto-deactivate (revert to previous) when charge is at or above this.</summary>
        public float RestoreAboveCharge = -1f;

        public bool AutoActivationEnabled = false;

        // --- Runtime ---------------------------------------------------------------

        /// <summary>Whether this preset is the active one. Only one preset may be active.</summary>
        public bool IsActive;

        public PowerPreset Clone() => (PowerPreset)MemberwiseClone();
    }
}
