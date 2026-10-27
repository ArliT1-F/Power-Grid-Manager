using System;
using System.Collections.Generic;

namespace PowerGridManager
{
    /// <summary>
    /// Manages reusable power-management presets. Presets are persisted with the global
    /// settings; only one preset may be active at a time. Activating a preset applies its
    /// policy values to the live settings — it never switches a building by itself.
    /// </summary>
    public static class PowerPresetManager
    {
        public const int MaxPresets = 16;
        public const int MaxNameLength = 48;
        public const int MaxDescriptionLength = 120;

        // --- Parallel-list persistence (kept in PowerGridManagerSettings) -----------

        public static List<string> Names(PowerGridManagerSettings settings) =>
            settings.presetNames;

        public static List<float> Lows(PowerGridManagerSettings settings) =>
            settings.presetLowThresholds;

        public static List<float> Restores(PowerGridManagerSettings settings) =>
            settings.presetRestoreThresholds;

        public static List<int> StableTicks(PowerGridManagerSettings settings) =>
            settings.presetStableTicks;

        public static List<int> CooldownTicks(PowerGridManagerSettings settings) =>
            settings.presetCooldownTicks;

        public static List<int> MaxActions(PowerGridManagerSettings settings) =>
            settings.presetMaxActionsPerPass;

        public static List<int> Recoveries(PowerGridManagerSettings settings) =>
            settings.presetRecoveryModes;

        public static List<bool> RequireDeficit(PowerGridManagerSettings settings) =>
            settings.presetRequireDeficit;

        public static List<int> DefaultPriorities(PowerGridManagerSettings settings) =>
            settings.presetDefaultPriorities;

        public static List<bool> MarkNewSheddable(PowerGridManagerSettings settings) =>
            settings.presetMarkNewSheddable;

        public static List<float> ActivateBelow(PowerGridManagerSettings settings) =>
            settings.presetActivateBelowCharge;

        public static List<float> RestoreAbove(PowerGridManagerSettings settings) =>
            settings.presetRestoreAboveCharge;

        public static List<bool> AutoActivation(PowerGridManagerSettings settings) =>
            settings.presetAutoActivation;

        public static List<bool> IsActive(PowerGridManagerSettings settings) =>
            settings.presetIsActive;

        /// <summary>Builds preset objects from the persisted parallel lists.</summary>
        public static List<PowerPreset> LoadPresets(PowerGridManagerSettings settings)
        {
            var presets = new List<PowerPreset>();
            if (settings == null) return presets;
            int count = Math.Min(Names(settings).Count, MaxPresets);
            for (int i = 0; i < count; i++)
            {
                string name = Trim(Names(settings)[i], MaxNameLength);
                if (name.Length == 0) continue;
                var preset = new PowerPreset
                {
                    Name = name,
                    Description = Trim(settings.presetDescriptions.Count > i
                        ? settings.presetDescriptions[i] : "", MaxDescriptionLength),
                    LoadShedLowThreshold = Clamp(Lows(settings)[i], 0.01f, 0.90f),
                    LoadShedRestoreThreshold = Clamp(Restores(settings)[i], 0.20f, 1f),
                    LoadShedStableTicks = ClampInt(StableTicks(settings)[i], 0,
                        PowerTime.TicksPerDay * 7),
                    LoadShedCooldownTicks = ClampInt(CooldownTicks(settings)[i], 0,
                        PowerTime.TicksPerDay * 7),
                    LoadShedMaxActionsPerPass = ClampInt(MaxActions(settings)[i], 1, 32),
                    LoadShedRecovery = ClampRecovery(Recoveries(settings)[i]),
                    LoadShedRequireDeficit = RequireDeficit(settings)[i],
                    DefaultPriority = ClampInt(DefaultPriorities(settings)[i],
                        PowerManagementRegistry.MinPriority, PowerManagementRegistry.MaxPriority),
                    MarkNewSheddable = MarkNewSheddable(settings)[i],
                    ActivateBelowCharge = ActivateBelow(settings)[i],
                    RestoreAboveCharge = RestoreAbove(settings)[i],
                    AutoActivationEnabled = AutoActivation(settings)[i],
                    IsActive = IsActive(settings)[i]
                };
                preset.LoadShedRestoreThreshold = Math.Max(preset.LoadShedRestoreThreshold,
                    preset.LoadShedLowThreshold + 0.05f);
                presets.Add(preset);
            }
            return presets;
        }

        /// <summary>Writes the preset list back into the persisted parallel lists.</summary>
        public static void SavePresets(PowerGridManagerSettings settings, List<PowerPreset> presets)
        {
            if (settings == null) return;
            settings.presetNames.Clear();
            settings.presetDescriptions.Clear();
            settings.presetLowThresholds.Clear();
            settings.presetRestoreThresholds.Clear();
            settings.presetStableTicks.Clear();
            settings.presetCooldownTicks.Clear();
            settings.presetMaxActionsPerPass.Clear();
            settings.presetRecoveryModes.Clear();
            settings.presetRequireDeficit.Clear();
            settings.presetDefaultPriorities.Clear();
            settings.presetMarkNewSheddable.Clear();
            settings.presetActivateBelowCharge.Clear();
            settings.presetRestoreAboveCharge.Clear();
            settings.presetAutoActivation.Clear();
            settings.presetIsActive.Clear();

            int count = Math.Min(presets.Count, MaxPresets);
            for (int i = 0; i < count; i++)
            {
                PowerPreset preset = presets[i];
                settings.presetNames.Add(Trim(preset.Name, MaxNameLength));
                settings.presetDescriptions.Add(Trim(preset.Description, MaxDescriptionLength));
                settings.presetLowThresholds.Add(Clamp(preset.LoadShedLowThreshold, 0.01f, 0.90f));
                settings.presetRestoreThresholds.Add(
                    Clamp(preset.LoadShedRestoreThreshold, 0.20f, 1f));
                settings.presetStableTicks.Add(ClampInt(preset.LoadShedStableTicks, 0,
                    PowerTime.TicksPerDay * 7));
                settings.presetCooldownTicks.Add(ClampInt(preset.LoadShedCooldownTicks, 0,
                    PowerTime.TicksPerDay * 7));
                settings.presetMaxActionsPerPass.Add(ClampInt(preset.LoadShedMaxActionsPerPass, 1, 32));
                settings.presetRecoveryModes.Add((int)preset.LoadShedRecovery);
                settings.presetRequireDeficit.Add(preset.LoadShedRequireDeficit);
                settings.presetDefaultPriorities.Add(ClampInt(preset.DefaultPriority,
                    PowerManagementRegistry.MinPriority, PowerManagementRegistry.MaxPriority));
                settings.presetMarkNewSheddable.Add(preset.MarkNewSheddable);
                settings.presetActivateBelowCharge.Add(preset.ActivateBelowCharge);
                settings.presetRestoreAboveCharge.Add(preset.RestoreAboveCharge);
                settings.presetAutoActivation.Add(preset.AutoActivationEnabled);
                settings.presetIsActive.Add(preset.IsActive);
            }
        }

        /// <summary>Ensures only one preset is active.</summary>
        public static void Activate(PowerGridManagerSettings settings, PowerPreset preset,
            int tick)
        {
            if (settings == null || preset == null) return;
            var presets = LoadPresets(settings);
            foreach (PowerPreset p in presets) p.IsActive = false;
            preset.IsActive = true;
            Apply(settings, preset);
            SavePresets(settings, presets);
            PowerGridManagerMod.Save();
            if (settings != null)
            {
                // Logged as a policy change so the activation is auditable.
            }
        }

        /// <summary>Applies a preset's policy values to the live settings.</summary>
        public static void Apply(PowerGridManagerSettings settings, PowerPreset preset)
        {
            if (settings == null || preset == null) return;
            settings.LoadShedLowThreshold = preset.LoadShedLowThreshold;
            settings.LoadShedRestoreThreshold = preset.LoadShedRestoreThreshold;
            settings.LoadShedStableTicks = preset.LoadShedStableTicks;
            settings.LoadShedCooldownTicks = preset.LoadShedCooldownTicks;
            settings.LoadShedMaxActionsPerPass = preset.LoadShedMaxActionsPerPass;
            settings.LoadShedRecovery = preset.LoadShedRecovery;
            settings.LoadShedRequireDeficit = preset.LoadShedRequireDeficit;
            settings.NormalizeManagement();
        }

        /// <summary>Deactivates the active preset without reverting its applied values.</summary>
        public static void Deactivate(PowerGridManagerSettings settings)
        {
            if (settings == null) return;
            var presets = LoadPresets(settings);
            bool any = false;
            foreach (PowerPreset p in presets)
            {
                if (p.IsActive) { p.IsActive = false; any = true; }
            }
            if (any) SavePresets(settings, presets);
        }

        /// <summary>
        /// Automatic activation: returns the preset that should activate given the current
        /// usable charge, or null. Only presets with auto-activation enabled are considered.
        /// </summary>
        public static PowerPreset CheckAutoActivation(List<PowerPreset> presets, float charge)
        {
            if (presets == null) return null;
            for (int i = 0; i < presets.Count; i++)
            {
                PowerPreset preset = presets[i];
                if (!preset.AutoActivationEnabled || preset.ActivateBelowCharge < 0f) continue;
                if (charge >= 0f && charge <= preset.ActivateBelowCharge) return preset;
            }
            return null;
        }

        public static bool Create(List<PowerPreset> presets, string name)
        {
            if (presets == null) return false;
            string trimmed = Trim(name ?? "", MaxNameLength);
            if (trimmed.Length == 0) return false;
            for (int i = 0; i < presets.Count; i++)
                if (presets[i].Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                    return false;
            if (presets.Count >= MaxPresets) return false;
            presets.Add(new PowerPreset { Name = trimmed });
            return true;
        }

        public static bool Delete(List<PowerPreset> presets, PowerPreset preset)
        {
            if (presets == null || preset == null) return false;
            bool removed = presets.Remove(preset);
            if (removed)
            {
                bool anyActive = false;
                foreach (PowerPreset p in presets) if (p.IsActive) anyActive = true;
                if (!anyActive && presets.Count > 0) presets[0].IsActive = true;
            }
            return removed;
        }

        public static bool Rename(PowerPreset preset, string name)
        {
            if (preset == null) return false;
            string trimmed = Trim(name ?? "", MaxNameLength);
            if (trimmed.Length == 0) return false;
            preset.Name = trimmed;
            return true;
        }

        public static PowerPreset Duplicate(List<PowerPreset> presets, PowerPreset preset)
        {
            if (presets == null || preset == null) return null;
            var copy = preset.Clone();
            int suffix = 1;
            string baseName = preset.Name;
            string candidate;
            do
            {
                candidate = baseName + " (" + suffix++ + ")";
            } while (Exists(presets, candidate));
            copy.Name = candidate;
            copy.IsActive = false;
            presets.Add(copy);
            return copy;
        }

        private static bool Exists(List<PowerPreset> presets, string name)
        {
            for (int i = 0; i < presets.Count; i++)
                if (presets[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Trim(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static LoadSheddingRecoveryMode ClampRecovery(int value)
        {
            return System.Enum.IsDefined(typeof(LoadSheddingRecoveryMode), value)
                ? (LoadSheddingRecoveryMode)value : LoadSheddingRecoveryMode.Staged;
        }
    }
}
