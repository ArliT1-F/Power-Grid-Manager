using System;
using System.Globalization;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Number and unit formatting for the dashboard. Watts (power) and watt-days
    /// (energy) are formatted through separate methods and carry distinct units, so the
    /// two are never conflated. Numbers use invariant formatting so the layout is stable
    /// across game locales; only unit words are translated.
    /// </summary>
    public static class PowerFormatting
    {
        private const float KilowattThreshold = 1000f;

        /// <summary>Numeric part of a watt value, without any unit (e.g. "850", "1.25").</summary>
        public static string FormatWattsValue(float watts)
        {
            float magnitude = Math.Abs(watts);
            if (magnitude >= KilowattThreshold) return FormatScaled(magnitude / 1000f);
            return FormatScaled(magnitude);
        }

        /// <summary>Translated unit that belongs to <see cref="FormatWattsValue"/>.</summary>
        public static string WattsUnit(float watts)
        {
            return Math.Abs(watts) >= KilowattThreshold
                ? "PGM_Unit_Kilowatt".Translate().ToString()
                : "PGM_Unit_Watt".Translate().ToString();
        }

        /// <summary>Full power string, e.g. "1.25 kW" or "850 W".</summary>
        public static string FormatWatts(float watts)
        {
            return FormatWattsValue(watts) + " " + WattsUnit(watts);
        }

        /// <summary>Full power string with an explicit sign, used for net balance.</summary>
        public static string FormatWattsSigned(float watts)
        {
            string sign = watts > 0.0001f ? "+" : watts < -0.0001f ? "-" : "";
            return sign + FormatWatts(Math.Abs(watts));
        }

        public static string FormatWattsUnknown(float? watts)
        {
            return watts.HasValue ? FormatWatts(watts.Value) : "PGM_Value_Unknown".Translate().ToString();
        }

        /// <summary>Numeric part of an energy value in watt-days.</summary>
        public static string FormatEnergyValue(float wattDays)
        {
            return FormatScaled(Math.Abs(wattDays));
        }

        public static string EnergyUnit()
        {
            return "PGM_Unit_WattDay".Translate().ToString();
        }

        /// <summary>Full energy string, e.g. "12.50 Wd".</summary>
        public static string FormatEnergy(float wattDays)
        {
            return FormatEnergyValue(wattDays) + " " + EnergyUnit();
        }

        /// <summary>Charge fraction (0..1) as a percentage string.</summary>
        public static string FormatCharge(float? fraction)
        {
            if (!fraction.HasValue) return "PGM_Value_Unknown".Translate().ToString();
            float percent = fraction.Value * 100f;
            if (percent < 0f) percent = 0f;
            if (percent > 100f) percent = 100f;
            return percent.ToString("F0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Elapsed or remaining ticks as a compact duration, e.g. "4.2 h".</summary>
        public static string FormatDuration(int ticks)
        {
            int magnitude = Math.Abs(ticks);
            if (magnitude < GenDate.TicksPerHour)
            {
                float minutes = magnitude * 60f / GenDate.TicksPerHour;
                return FormatFixed(minutes, minutes < 10f ? 1 : 0) + " " + "PGM_Unit_Minute".Translate().ToString();
            }
            if (magnitude < GenDate.TicksPerDay)
            {
                float hours = magnitude / (float)GenDate.TicksPerHour;
                return FormatFixed(hours, hours < 10f ? 1 : 0) + " " + "PGM_Unit_Hour".Translate().ToString();
            }
            float days = magnitude / (float)GenDate.TicksPerDay;
            return FormatFixed(days, days < 10f ? 1 : 0) + " " + "PGM_Unit_Day".Translate().ToString();
        }

        /// <summary>
        /// Daily energy estimate in watt-days for a constant draw, i.e. the draw itself.
        /// RimWorld already measures energy in watt-days, so one day at a constant
        /// <paramref name="watts"/> equals that many watt-days. Labelled as an estimate
        /// by the UI because real demand varies.
        /// </summary>
        public static float EstimateDailyWattDays(float watts)
        {
            if (watts <= 0f) return 0f;
            return watts;
        }

        public static string FormatPosition(int x, int z)
        {
            return "(" + x.ToString(CultureInfo.InvariantCulture) + ", " + z.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private static string FormatScaled(float value)
        {
            if (value < 10f) return FormatFixed(value, 2);
            if (value < 100f) return FormatFixed(value, 1);
            return FormatFixed(value, 0);
        }

        private static string FormatFixed(float value, int decimals)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }
}
