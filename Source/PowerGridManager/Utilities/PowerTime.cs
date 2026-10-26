namespace PowerGridManager
{
    /// <summary>
    /// Game-time constants used for durations, retention and history windows.
    /// <para>
    /// RimWorld's <c>Verse.GenDate</c> exposes these same two values, but this mod is
    /// compiled against three different game generations (1.5, 1.6 and the .NET 10 build
    /// of 1.7) and against the offline test harness. Owning the two primitives here keeps
    /// the assembly from depending on that particular type being present in every build it
    /// is compiled against, and keeps the numbers visible next to the code that uses them.
    /// </para>
    /// At 1x game speed one in-game hour is 2,500 ticks and one in-game day is 60,000 ticks.
    /// </summary>
    public static class PowerTime
    {
        public const int TicksPerHour = 2500;
        public const int TicksPerDay = 60000;
    }
}
