using System;
using System.Collections.Generic;
using System.Linq;

namespace STAGE
{
    internal static class BaselineScdDetector
    {
        public static bool TryApplyFromPlaylists(IEnumerable<Playlist> playlists, out string detectedKey)
        {
            detectedKey = string.Empty;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var playlist in playlists)
            {
                if (playlist.Options == null)
                    continue;

                foreach (var option in playlist.Options)
                {
                    if (option?.Files == null)
                        continue;

                    foreach (string key in option.Files.Keys)
                    {
                        string normalized = NormalizeKey(key);
                        if (!IsBaselineCandidate(normalized))
                            continue;

                        counts.TryGetValue(normalized, out int count);
                        counts[normalized] = count + 1;
                    }
                }
            }

            if (counts.Count == 0)
                return false;

            int bestCount = counts.Values.Max();
            var tiedKeys = counts
                .Where(pair => pair.Value == bestCount)
                .Select(pair => pair.Key)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string current = NormalizeKey(Settings.BaselineScdKey);
            detectedKey = tiedKeys.FirstOrDefault(key =>
                string.Equals(key, current, StringComparison.OrdinalIgnoreCase))
                ?? tiedKeys[0];

            if (string.Equals(current, detectedKey, StringComparison.OrdinalIgnoreCase))
                return false;

            Settings.BaselineScdKey = detectedKey;
            return true;
        }

        private static bool IsBaselineCandidate(string key)
        {
            return key.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)
                && key.StartsWith("sound/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKey(string key)
        {
            return (key ?? string.Empty).Trim().Replace('\\', '/');
        }
    }
}
