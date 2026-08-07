using Newtonsoft.Json;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public sealed record ModCleanupResult(
        int FilesScanned,
        int JsonFilesUpdated,
        int RemovedAudioTracks,
        int RemovedVfxPapOptions,
        int RemovedVfxPapMappings,
        IReadOnlyList<string> Errors);

    public static class ModCleanupService
    {
        private static readonly string[] AudioExtensions = [".scd"];
        private static readonly string[] VfxPapExtensions = [".pap", ".avfx"];

        public static ModCleanupResult PurgeMissingReferences()
        {
            int scanned = 0;
            int updated = 0;
            int removedAudio = 0;
            int removedOptions = 0;
            int removedMappings = 0;
            var errors = new List<string>();

            foreach (string modDirectory in GetCleanupModFolders())
            {
                if (!Directory.Exists(modDirectory))
                    continue;

                if (PenumbraMeta.TryLoad(modDirectory, out var meta))
                {
                    scanned++;
                    try
                    {
                        bool changed = false;
                        foreach (var group in meta!.Groups)
                        {
                            if (VfxPapGroup.IsAudioPlaylistGroup(group))
                            {
                                int before = group.Options.Count;
                                group.Options = group.Options
                                    .Where(option => ShouldKeepAudioOption(modDirectory, option))
                                    .ToList();
                                int removed = before - group.Options.Count;
                                if (removed > 0)
                                {
                                    changed = true;
                                    removedAudio += removed;
                                }
                            }
                            else if (PurgeMissingVfxPapFromGroup(modDirectory, group, ref removedOptions, ref removedMappings))
                            {
                                changed = true;
                            }
                        }

                        if (changed)
                        {
                            meta.Save();
                            PenumbraApi.ScheduleReloadModFolder(modDirectory);
                            updated++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(modDirectory)}\\meta.json: {ex.Message}");
                    }

                    continue;
                }

            foreach (string groupPath in Directory.GetFiles(modDirectory, "group_*.json")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                scanned++;
                try
                {
                    string json = File.ReadAllText(groupPath);
                    var playlist = JsonConvert.DeserializeObject<Playlist>(json);
                    if (playlist?.Options == null)
                        continue;

                    if (VfxPapGroup.IsAudioPlaylistGroup(playlist))
                    {
                        int before = playlist.Options.Count;
                        playlist.Options = playlist.Options
                            .Where(option => ShouldKeepAudioOption(modDirectory, option))
                            .ToList();
                        int removed = before - playlist.Options.Count;
                        if (removed > 0)
                        {
                            WriteGroupJson(groupPath, playlist);
                            PenumbraApi.ScheduleReloadModFolder(modDirectory);
                            updated++;
                            removedAudio += removed;
                        }
                    }
                    else
                    {
                        var group = JsonConvert.DeserializeObject<VfxPapGroup>(json);
                        if (group?.Options == null)
                            continue;

                        bool changed = PurgeMissingVfxPapFromGroup(
                            modDirectory,
                            group,
                            ref removedOptions,
                            ref removedMappings);

                        if (changed)
                        {
                            WriteGroupJson(groupPath, group);
                            PenumbraApi.ScheduleReloadModFolder(modDirectory);
                            updated++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(groupPath)}: {ex.Message}");
                }
            }
            }

            return new ModCleanupResult(
                scanned,
                updated,
                removedAudio,
                removedOptions,
                removedMappings,
                errors);
        }

        private static bool PurgeMissingVfxPapFromGroup(
            string modDirectory,
            VfxPapGroup group,
            ref int removedOptions,
            ref int removedMappings)
        {
            bool changed = false;
            foreach (var option in group.Options.ToList())
            {
                if (option.Files == null || option.Files.Count == 0)
                    continue;

                var keysToRemove = option.Files
                    .Where(pair => IsTargetExtension(pair.Key, pair.Value, VfxPapExtensions))
                    .Where(pair => !MappedFileExists(modDirectory, pair.Value))
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (string key in keysToRemove)
                    option.Files.Remove(key);

                if (keysToRemove.Count > 0)
                {
                    changed = true;
                    removedMappings += keysToRemove.Count;
                }

                if (keysToRemove.Count > 0 && option.Files.Count == 0 && !IsProtectedOption(option))
                {
                    group.Options.Remove(option);
                    removedOptions++;
                }
            }

            return changed;
        }

        private static IReadOnlyList<string> GetCleanupModFolders()
        {
            var folders = Settings.ManagedModFolders.ToList();
            string active = Settings.ActiveModFolder;
            if (!string.IsNullOrWhiteSpace(active) &&
                !folders.Contains(active, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(active);
            }

            return folders
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ShouldKeepAudioOption(string modDirectory, Option option)
        {
            if (IsProtectedOption(option))
                return true;
            if (option.Files == null || option.Files.Count == 0)
                return true;
            if (!option.Files.Any(pair => IsTargetExtension(pair.Key, pair.Value, AudioExtensions)))
                return true;

            string scdPath = Playlist.GetScdPath(option);
            return string.IsNullOrWhiteSpace(scdPath) || MappedFileExists(modDirectory, scdPath);
        }

        private static bool IsProtectedOption(Option option)
        {
            return option.Name.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
                   option.Name.Equals("Default", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTargetExtension(string gamePath, string localPath, IReadOnlyCollection<string> extensions)
        {
            return extensions.Any(extension =>
                gamePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
                localPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MappedFileExists(string modDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return true;

            string fullPath = Path.GetFullPath(Path.Combine(modDirectory, ToNativePath(relativePath)));
            string modRoot = Path.GetFullPath(modDirectory).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(modRoot, StringComparison.OrdinalIgnoreCase))
                return true;

            return File.Exists(fullPath);
        }

        private static void WriteGroupJson(string groupPath, object group)
        {
            string json = JsonConvert.SerializeObject(group, Formatting.Indented);
            PenumbraMeta.RequireFormat(Path.GetDirectoryName(groupPath) ?? "", PenumbraModFormat.LegacyGroupFiles);
            File.WriteAllText(groupPath, json);
        }

        private static string ToNativePath(string path)
        {
            return path.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}
