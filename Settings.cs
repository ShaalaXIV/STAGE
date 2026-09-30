using Microsoft.Win32;
using STAGE.Tools;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public static class Settings
    {
        private static string s_valueName = "PenumbraPath";
        private static string s_subKey = AppEnvironment.RegistrySubKey;
        private static string[] s_defaultModNames = {
            "STAGE DJ Muzik, Movez, and VFX",
            "DAMThunderdome.exe",
            "[yue's + lu's] dj",
            "[Yue & Lu's] Mega Music Mod",
        };
        // Initialized with three dummy keys/values
        private static Dictionary<string, string> s_defaultBaselineScdKey = new Dictionary<string, string>
        {
            { s_defaultModNames[0], "sound/bpmloop.scd" },
            { s_defaultModNames[1], "sound/dam.scd" },
            { s_defaultModNames[2], "sound/lolo.scd" },
            { s_defaultModNames[3], "sound/lolo.scd" }
        };
            
        public static string[] SupportedFileTypes = new string[] { ".ogg", ".wav", ".mp3", ".m4a", ".flac", ".scd" };

        public static string PenumbraLocation
        {
            get
            {
                // Read the value from the registry
                string retval = (string)Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue(s_valueName, null);
                if (string.IsNullOrWhiteSpace(retval))
                {
                    retval = PenumbraApi.GetPenumbraDirectory();
                    if (!string.IsNullOrWhiteSpace(retval))
                    {
                        PenumbraLocation = retval; // save it for next time
                    }
                }
                return retval;
            }
            set
            {
                // Specify the registry key and value

                // Open or create the registry key
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(s_subKey))
                {
                    if (key != null)
                    {
                        // Write the value
                        key.SetValue(s_valueName, value);
                    }
                }
            }
        }
        public static string ModName
        {
            get
            {
                return (string)Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("ModName")
                    ?? string.Empty;
            }
            set
            {
                // Open or create the registry key
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(s_subKey))
                {
                    if (key != null)
                    {
                        // Write the value
                        key.SetValue("ModName", value);
                    }
                }
            }
        }

        public static string ActiveModFolder
        {
            get
            {
                string parent = PenumbraLocation;
                string name = ModName;
                if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                    return string.Empty;
                return Path.GetFullPath(Path.Combine(parent, name))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        public static string AudioModFolder => ActiveAudioModFolder;

        public static string VfxModFolder => ActiveVfxModFolder;

        private static string ResolveManagedModFolder(Func<string, bool> predicate, string preferredValueName)
        {
            string preferred = GetScopedModFolder(preferredValueName);
            if (!string.IsNullOrWhiteSpace(preferred) && Directory.Exists(preferred))
                return preferred;

            string active = ActiveModFolder;
            if (!string.IsNullOrWhiteSpace(active) && Directory.Exists(active))
                return active;

            foreach (string folder in ManagedModFolders)
                if (Directory.Exists(folder) && predicate(folder))
                    return folder;

            return active;
        }

        public static string ActiveAudioModFolder
        {
            get => ResolveManagedModFolder(PenumbraMeta.ContainsAudioGroups, "ActiveAudioModFolder");
            set => SetScopedModFolder("ActiveAudioModFolder", value);
        }

        public static string ActiveVfxModFolder
        {
            get => ResolveManagedModFolder(PenumbraMeta.ContainsVfxGroups, "ActiveVfxModFolder");
            set => SetScopedModFolder("ActiveVfxModFolder", value);
        }

        private static string GetScopedModFolder(string valueName)
        {
            try
            {
                var value = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue(valueName) as string;
                return string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeModFolder(value);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void SetScopedModFolder(string valueName, string path)
        {
            string normalized = NormalizeModFolder(path);
            using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
            key?.SetValue(valueName, normalized);
            AddManagedModFolder(normalized);
        }

        public static IReadOnlyList<string> ManagedModFolders
        {
            get
            {
                try
                {
                    var value = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("ManagedModFolders");
                    if (value is string[] paths)
                    {
                        return paths
                            .Where(path => !string.IsNullOrWhiteSpace(path))
                            .Select(NormalizeModFolder)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    }
                }
                catch { }

                return Array.Empty<string>();
            }
            set
            {
                string[] paths = value
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(NormalizeModFolder)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                key?.SetValue("ManagedModFolders", paths, RegistryValueKind.MultiString);
            }
        }

        public static void AddManagedModFolder(string path)
        {
            string normalized = NormalizeModFolder(path);
            var paths = ManagedModFolders.ToList();
            if (!paths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(normalized);
                ManagedModFolders = paths;
            }
        }

        public static void SetActiveModFolder(string path)
        {
            string normalized = NormalizeModFolder(path);
            string? modName = Path.GetFileName(normalized);
            string? parent = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(modName) || string.IsNullOrWhiteSpace(parent))
                throw new ArgumentException("Select a mod folder, not a drive root.", nameof(path));

            PenumbraLocation = parent;
            ModName = modName;
            AddManagedModFolder(normalized);
            if (Directory.Exists(normalized))
            {
                if (PenumbraMeta.ContainsAudioGroups(normalized))
                    ActiveAudioModFolder = normalized;
                if (PenumbraMeta.ContainsVfxGroups(normalized))
                    ActiveVfxModFolder = normalized;
            }
        }

        private static string NormalizeModFolder(string path)
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string BaselineScdKey
        {
            get
            {
                string key = (string)Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("BaselineScdKey");
                if (string.IsNullOrWhiteSpace(key))
                {
                    if (s_defaultBaselineScdKey.TryGetValue(ModName, out var defaultKey))
                    {
                        BaselineScdKey = defaultKey; // save it for next time
                        return defaultKey;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(ModName) && ModName.Contains("[yue & lu's]", StringComparison.OrdinalIgnoreCase))
                        {
                            return "sound/lolo.scd";
                        }
                        return s_defaultBaselineScdKey[s_defaultModNames[0]]; // fallback to first default if mod name is unrecognized, but don't save it
                    }
                }
                return key;
            }
            set
            {
                string normalized = value.Trim();
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(s_subKey))
                {
                    if (key != null)
                    {
                        key.SetValue("BaselineScdKey", normalized);
                    }
                }
            }
        }


        /// <summary>
        /// Controls whether converted audio should be loudness-normalized.
        /// Default: true.
        /// Stored as integer 1 (true) or 0 (false) under the same registry subkey.
        /// </summary>
        public static bool NormalizeVolume
        {
            get
            {
                try
                {
                    var value = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("NormalizeVolume", 0);
                    if (value is int iv) return iv != 0;
                    if (value is long lv) return lv != 0;
                    if (value is string sv && bool.TryParse(sv, out var bv)) return bv;
                    if (value is string sv2 && int.TryParse(sv2, out var parsed)) return parsed != 0;
                }
                catch
                {
                    // fallthrough to default
                }
                return false;
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(s_subKey))
                {
                    if (key != null)
                    {
                        key.SetValue("NormalizeVolume", value ? 1 : 0);
                    }
                }
            }
        }

        public static EqualizerPreset EqualizerPreset
        {
            get
            {
                try
                {
                    var value = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("EqualizerPreset", 0);
                    int index = value is int iv ? iv : Convert.ToInt32(value);
                    if (Enum.IsDefined(typeof(EqualizerPreset), index))
                        return (EqualizerPreset)index;
                }
                catch { }
                return EqualizerPreset.Neutral;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                key?.SetValue("EqualizerPreset", (int)value, RegistryValueKind.DWord);
            }
        }

        public static float ScdAudioVolume
        {
            get
            {
                try
                {
                    var value = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("ScdAudioVolume", 1.5f);
                    float parsed = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                    return Math.Clamp(parsed, 1f, 5f);
                }
                catch { }
                return 1.5f;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                key?.SetValue("ScdAudioVolume", Math.Clamp(value, 1f, 5f).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        public static YtCookieBrowser YouTubeCookieBrowser
        {
            get
            {
                try
                {
                    var value = Registry.CurrentUser.OpenSubKey(s_subKey)
                        ?.GetValue("YouTubeCookieBrowser", 0);
                    int index = value is int iv ? iv : Convert.ToInt32(value);
                    if (Enum.IsDefined(typeof(YtCookieBrowser), index))
                        return (YtCookieBrowser)index;
                }
                catch { }

                return YtCookieBrowser.None;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                key?.SetValue("YouTubeCookieBrowser", (int)value, RegistryValueKind.DWord);
            }
        }

        /// <summary>
        /// Controls whether the mod should be auto-reloaded (Penumbra) after changes.
        /// Default: true.
        /// Stored as integer 1 (true) or 0 (false) under the same registry subkey.
        /// </summary>
        public static bool AutoReloadMod
        {
            get
            {
                try
                {
                    var value = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("AutoReloadMod", 1);
                    if (value is int iv) return iv != 0;
                    if (value is long lv) return lv != 0;
                    if (value is string sv && bool.TryParse(sv, out var bv)) return bv;
                    if (value is string sv2 && int.TryParse(sv2, out var parsed)) return parsed != 0;
                }
                catch
                {
                    // fallthrough to default
                }
                return true;
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(s_subKey))
                {
                    if (key != null)
                    {
                        key.SetValue("AutoReloadMod", value ? 1 : 0);
                    }
                }
            }
        }

        

        public static bool AutoUpdateDependencies
        {
            get
            {
                try
                {
                    var value = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("AutoUpdateDependencies", 1);
                    if (value is int iv) return iv != 0;
                    if (value is long lv) return lv != 0;
                    if (value is string sv && bool.TryParse(sv, out var bv)) return bv;
                    if (value is string sv2 && int.TryParse(sv2, out var parsed)) return parsed != 0;
                }
                catch { }
                return true;
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(s_subKey))
                {
                    key?.SetValue("AutoUpdateDependencies", value ? 1 : 0);
                }
            }
        }

        public static bool BpmFirstTimeMessageShown
        {
            get
            {
                try
                {
                    var value = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("BpmFirstTimeMessageShown", 0);
                    if (value is int iv) return iv != 0;
                    if (value is long lv) return lv != 0;
                    if (value is string sv && bool.TryParse(sv, out var bv)) return bv;
                    if (value is string sv2 && int.TryParse(sv2, out var parsed)) return parsed != 0;
                }
                catch { }
                return false;
            }
            set
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(s_subKey);
                key?.SetValue("BpmFirstTimeMessageShown", value ? 1 : 0);
            }
        }

        public static string FfmpegBuildTag
        {
            get => (string)Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("FfmpegBuildTag", string.Empty) ?? string.Empty;
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                if (string.IsNullOrWhiteSpace(value))
                    key?.DeleteValue("FfmpegBuildTag", throwOnMissingValue: false);
                else
                    key?.SetValue("FfmpegBuildTag", value.Trim());
            }
        }

        public static readonly string DefaultBackgroundImagePath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "ui", "stagebackground.png");

        public static string BackgroundImagePath
        {
            get
            {
                string? saved = null;
                try
                {
                    saved = Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("BackgroundImagePath", null) as string;
                }
                catch
                {
                    saved = null;
                }

                if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
                    return saved;

                return File.Exists(DefaultBackgroundImagePath)
                    ? DefaultBackgroundImagePath
                    : string.Empty;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                if (string.IsNullOrEmpty(value) || value == DefaultBackgroundImagePath)
                    key?.DeleteValue("BackgroundImagePath", throwOnMissingValue: false);
                else
                    key?.SetValue("BackgroundImagePath", value);
            }
        }

        public static string DefaultScdTemplateSourcePath
        {
            get => (string)Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("DefaultScdTemplateSourcePath", string.Empty) ?? string.Empty;
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                if (string.IsNullOrWhiteSpace(value))
                    key?.DeleteValue("DefaultScdTemplateSourcePath", throwOnMissingValue: false);
                else
                    key?.SetValue("DefaultScdTemplateSourcePath", value.Trim());
            }
        }

        public static string DefaultDonorPapPath
        {
            get => (string)Registry.CurrentUser.OpenSubKey(s_subKey)?.GetValue("DefaultDonorPapPath", string.Empty) ?? string.Empty;
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                if (string.IsNullOrWhiteSpace(value))
                    key?.DeleteValue("DefaultDonorPapPath", throwOnMissingValue: false);
                else
                    key?.SetValue("DefaultDonorPapPath", value.Trim());
            }
        }

        public static int AutoBackupRetentionDays
        {
            get
            {
                object? value = Registry.CurrentUser
                    .OpenSubKey(s_subKey)?
                    .GetValue("AutoBackupRetentionDays", 30);
                return value is int days ? Math.Clamp(days, 1, 3650) : 30;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                key?.SetValue("AutoBackupRetentionDays", Math.Clamp(value, 1, 3650));
            }
        }

        public static (int Width, int Height) WindowSize
        {
            get
            {
                try
                {
                    var k = Registry.CurrentUser.OpenSubKey(s_subKey);
                    if (k != null)
                    {
                        var w = k.GetValue("WindowWidth");
                        var h = k.GetValue("WindowHeight");
                        if (w is int wi && h is int hi && wi > 100 && hi > 100)
                            return (wi, hi);
                    }
                }
                catch { }
                return (900, 600);
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(s_subKey);
                key?.SetValue("WindowWidth",  value.Width,  RegistryValueKind.DWord);
                key?.SetValue("WindowHeight", value.Height, RegistryValueKind.DWord);
            }
        }
    }
}
