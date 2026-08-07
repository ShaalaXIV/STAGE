using Newtonsoft.Json;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public sealed record VfxPapDeleteResult(
        string GroupPath,
        int RemovedOptions,
        int RemovedMappings,
        IReadOnlyList<string> DeletedFiles,
        IReadOnlyList<string> KeptReferencedFiles);

    public static class VfxPapDeleter
    {
        private static readonly string[] DeletableExtensions = [".pap", ".avfx"];

        public static VfxPapDeleteResult DeleteOption(string groupPath, string optionName)
        {
            var group = LoadGroup(groupPath);
            var option = group.Options.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, optionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
                throw new InvalidOperationException("Could not find that option in the group JSON.");

            var candidateFiles = GetDeletableLocalPaths(option.Files?.Values ?? Enumerable.Empty<string>()).ToList();
            group.Options.Remove(option);
            SaveGroup(groupPath, group);

            var (deletedFiles, keptFiles) = DeleteUnusedPhysicalFiles(groupPath, candidateFiles);
            return new VfxPapDeleteResult(groupPath, 1, option.Files?.Count ?? 0, deletedFiles, keptFiles);
        }

        public static VfxPapDeleteResult DeleteMapping(
            string groupPath,
            string optionName,
            string gamePath,
            string localPath)
        {
            var group = LoadGroup(groupPath);
            var option = group.Options.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, optionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
                throw new InvalidOperationException("Could not find that option in the group JSON.");

            if (option.Files == null ||
                !option.Files.TryGetValue(gamePath, out string mappedLocalPath) ||
                !string.Equals(Normalize(mappedLocalPath), Normalize(localPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Could not find that file mapping in the group JSON.");
            }

            option.Files.Remove(gamePath);
            SaveGroup(groupPath, group);

            var candidates = IsDeletableAssetPath(localPath)
                ? new[] { localPath }
                : Array.Empty<string>();
            var (deletedFiles, keptFiles) = DeleteUnusedPhysicalFiles(groupPath, candidates);
            return new VfxPapDeleteResult(groupPath, 0, 1, deletedFiles, keptFiles);
        }

        private static (IReadOnlyList<string> DeletedFiles, IReadOnlyList<string> KeptFiles) DeleteUnusedPhysicalFiles(
            string groupPath,
            IEnumerable<string> relativePaths)
        {
            string modDirectory = GetModDirectory(groupPath);
            var deletedFiles = new List<string>();
            var keptFiles = new List<string>();

            foreach (string relativePath in relativePaths
                         .Where(IsDeletableAssetPath)
                         .Select(Normalize)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (IsReferencedByAnyGroup(modDirectory, relativePath))
                {
                    keptFiles.Add(relativePath);
                    continue;
                }

                string fullPath = Path.Combine(modDirectory, ToNativePath(relativePath));
                if (!File.Exists(fullPath))
                    continue;

                File.Delete(fullPath);
                deletedFiles.Add(fullPath);
                DeleteEmptyParentDirectory(fullPath, modDirectory);
            }

            return (deletedFiles, keptFiles);
        }

        private static bool IsReferencedByAnyGroup(string modDirectory, string relativePath)
        {
            if (!Directory.Exists(modDirectory))
                return false;

            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                return meta!.Groups
                    .SelectMany(group => group.Options ?? new List<Option>())
                    .Any(option => option.Files?.Values.Any(value =>
                        string.Equals(Normalize(value), relativePath, StringComparison.OrdinalIgnoreCase)) == true);
            }

            foreach (string groupPath in Directory.GetFiles(modDirectory, "group_*.json"))
            {
                VfxPapGroup? group;
                try
                {
                    group = JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(groupPath));
                }
                catch
                {
                    continue;
                }

                if (group?.Options == null)
                    continue;

                foreach (var option in group.Options)
                {
                    if (option.Files?.Values.Any(value =>
                            string.Equals(Normalize(value), relativePath, StringComparison.OrdinalIgnoreCase)) == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<string> GetDeletableLocalPaths(IEnumerable<string> paths) =>
            paths.Where(IsDeletableAssetPath);

        private static bool IsDeletableAssetPath(string path) =>
            DeletableExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        private static VfxPapGroup LoadGroup(string groupPath)
        {
            if (PenumbraMeta.IsGroupReference(groupPath) &&
                PenumbraMeta.TryParseGroupReference(groupPath, out string modDirectory, out _, out _))
            {
                var meta = PenumbraMeta.Load(modDirectory);
                var metaGroup = meta.FindGroupByReference(groupPath)
                    ?? throw new FileNotFoundException("Could not find the source group in meta.json.", groupPath);
                metaGroup.Options ??= new List<Option>();
                foreach (var option in metaGroup.Options)
                    option.Files ??= new Dictionary<string, string>();
                return metaGroup;
            }

            if (string.IsNullOrWhiteSpace(groupPath) || !File.Exists(groupPath))
                throw new FileNotFoundException("Could not find the source group JSON.", groupPath);

            var group = JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(groupPath));
            if (group == null)
                throw new InvalidOperationException("Could not read the source group JSON.");

            group.Options ??= new List<Option>();
            foreach (var option in group.Options)
                option.Files ??= new Dictionary<string, string>();

            return group;
        }

        private static void SaveGroup(string groupPath, VfxPapGroup group)
        {
            if (PenumbraMeta.IsGroupReference(groupPath) &&
                PenumbraMeta.TryParseGroupReference(groupPath, out string modDirectory, out _, out _))
            {
                var meta = PenumbraMeta.Load(modDirectory);
                var target = meta.FindGroupByReference(groupPath)
                    ?? throw new FileNotFoundException("Could not find the source group in meta.json.", groupPath);
                target.Options = group.Options;
                target.Name = group.Name;
                target.Priority = group.Priority;
                target.Type = group.Type;
                meta.Save();
                PenumbraApi.ScheduleReloadModFolder(modDirectory);
                return;
            }

            string json = JsonConvert.SerializeObject(group, Formatting.Indented);
            PenumbraMeta.RequireFormat(
                Path.GetDirectoryName(groupPath) ?? "",
                PenumbraModFormat.LegacyGroupFiles);
            File.WriteAllText(groupPath, json);
            PenumbraApi.ScheduleReloadModFolder(GetModDirectory(groupPath));
        }

        private static string GetModDirectory(string groupPath)
        {
            if (PenumbraMeta.IsGroupReference(groupPath) &&
                PenumbraMeta.TryParseGroupReference(groupPath, out string modDirectory, out _, out _))
            {
                return modDirectory;
            }

            string? directory = Path.GetDirectoryName(groupPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Could not determine the mod folder from the group JSON path.");

            return directory;
        }

        private static void DeleteEmptyParentDirectory(string filePath, string stopAtDirectory)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory) ||
                string.Equals(directory, stopAtDirectory, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(directory) ||
                Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return;
            }

            Directory.Delete(directory);
        }

        private static string Normalize(string path) =>
            path.Replace('\\', '/').Trim();

        private static string ToNativePath(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}
