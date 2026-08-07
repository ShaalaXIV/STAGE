using Newtonsoft.Json;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public sealed record FilenameSyncResult(
        int FilesCreated,
        int OldFilesRemoved,
        int MappingsUpdated,
        int MissingMappingsSkipped,
        string BackupDirectory,
        IReadOnlyList<string> Changes);

    public static class ModAssetFilenameSynchronizer
    {
        private sealed record MappingOwner(
            VfxPapGroup Group,
            Option Option,
            string GamePath,
            string SourceRelativePath,
            string DesiredRelativePath);

        private sealed record PlannedCopy(
            string SourceRelativePath,
            string DestinationRelativePath,
            IReadOnlyList<MappingOwner> Owners);

        public static FilenameSyncResult Synchronize(string modDirectory)
        {
            modDirectory = Path.GetFullPath(modDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            PenumbraModFormat format = PenumbraMeta.DetectFormat(modDirectory);
            PenumbraMeta? meta = format == PenumbraModFormat.SingularMeta
                ? PenumbraMeta.Load(modDirectory)
                : null;
            var groups = meta?.Groups ?? LoadLegacyGroups(modDirectory);
            var owners = BuildOwners(groups);
            int missingMappings = owners.Count(owner =>
                !File.Exists(ResolveInsideMod(modDirectory, owner.SourceRelativePath)));
            var plans = BuildPlan(modDirectory, owners);

            if (plans.Count == 0)
            {
                return new FilenameSyncResult(
                    0, 0, 0, missingMappings, "", Array.Empty<string>());
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupDirectory = Path.Combine(
                modDirectory, ".stage-backups", $"filename-sync-{stamp}");
            Directory.CreateDirectory(backupDirectory);

            var sourcePaths = plans
                .Select(plan => plan.SourceRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (string relativePath in sourcePaths)
            {
                string source = ResolveInsideMod(modDirectory, relativePath);
                if (!File.Exists(source))
                    throw new FileNotFoundException("A mapped PAP/VFX file is missing.", source);
                string backup = ResolveInsideRoot(backupDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(source, backup, overwrite: false);
            }

            var jsonPaths = format == PenumbraModFormat.SingularMeta
                ? new[] { PenumbraMeta.GetMetaPath(modDirectory) }
                : groups.Select(group => group.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string jsonPath in jsonPaths)
            {
                string backup = Path.Combine(backupDirectory, "json", Path.GetFileName(jsonPath));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(jsonPath, backup, overwrite: false);
            }

            var changes = new List<string>();
            foreach (var plan in plans)
            {
                string sourceBackup = ResolveInsideRoot(backupDirectory, plan.SourceRelativePath);
                string destination = ResolveInsideMod(modDirectory, plan.DestinationRelativePath);
                if (!SamePath(
                        ResolveInsideMod(modDirectory, plan.SourceRelativePath),
                        destination))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(sourceBackup, destination, overwrite: false);
                    changes.Add($"{plan.SourceRelativePath} -> {plan.DestinationRelativePath}");
                }

                foreach (var owner in plan.Owners)
                    owner.Option.Files[owner.GamePath] = plan.DestinationRelativePath;
            }

            if (format == PenumbraModFormat.SingularMeta)
            {
                meta!.Save();
            }
            else
            {
                var changedGroups = plans
                    .SelectMany(plan => plan.Owners)
                    .Select(owner => owner.Group)
                    .Distinct()
                    .ToList();
                foreach (var group in changedGroups)
                {
                    PenumbraMeta.RequireFormat(modDirectory, PenumbraModFormat.LegacyGroupFiles);
                    WriteJsonAtomic(group.FilePath, JsonConvert.SerializeObject(group, Formatting.Indented));
                }
            }

            var stillReferenced = groups
                .SelectMany(group => group.Options)
                .SelectMany(option => option.Files.Values)
                .Select(Normalize)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (string sourceRelativePath in sourcePaths)
            {
                if (stillReferenced.Contains(Normalize(sourceRelativePath)))
                    continue;

                string source = ResolveInsideMod(modDirectory, sourceRelativePath);
                if (File.Exists(source))
                {
                    File.Delete(source);
                    removed++;
                }
            }

            var manifest = new
            {
                CreatedAt = DateTime.Now,
                ModDirectory = modDirectory,
                Format = format.ToString(),
                Changes = changes,
                BackedUpJson = jsonPaths.Select(Path.GetFileName).ToArray(),
                BackedUpAssets = sourcePaths
            };
            File.WriteAllText(
                Path.Combine(backupDirectory, "manifest.json"),
                JsonConvert.SerializeObject(manifest, Formatting.Indented));

            PenumbraApi.ScheduleReloadModFolder(modDirectory);
            return new FilenameSyncResult(
                plans.Count(plan => !SamePath(
                    ResolveInsideMod(modDirectory, plan.SourceRelativePath),
                    ResolveInsideMod(modDirectory, plan.DestinationRelativePath))),
                removed,
                plans.Sum(plan => plan.Owners.Count),
                missingMappings,
                backupDirectory,
                changes);
        }

        private static List<VfxPapGroup> LoadLegacyGroups(string modDirectory)
        {
            var groups = new List<VfxPapGroup>();
            foreach (string path in Directory.GetFiles(modDirectory, "group_*.json"))
            {
                var group = JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(path))
                    ?? throw new InvalidDataException($"Could not read {Path.GetFileName(path)}.");
                group.FilePath = path;
                group.Options ??= new List<Option>();
                foreach (var option in group.Options)
                    option.Files ??= new Dictionary<string, string>();
                groups.Add(group);
            }
            return groups;
        }

        private static List<MappingOwner> BuildOwners(IEnumerable<VfxPapGroup> groups)
        {
            var result = new List<MappingOwner>();
            foreach (var group in groups)
            {
                foreach (var option in group.Options)
                {
                    var assetMappings = option.Files
                        .Where(mapping => IsSupported(mapping.Value))
                        .ToList();
                    int papCount = assetMappings
                        .Select(mapping => mapping.Value)
                        .Where(IsPap)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    int avfxCount = assetMappings
                        .Select(mapping => mapping.Value)
                        .Where(path => path.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    foreach (var mapping in assetMappings)
                    {
                        string extension = Path.GetExtension(mapping.Value).ToLowerInvariant();
                        string suffix = "";
                        if (extension == ".pap" && papCount > 1)
                        {
                            suffix = mapping.Key.Contains("_start.pap", StringComparison.OrdinalIgnoreCase)
                                ? "_start"
                                : mapping.Key.Contains("_loop.pap", StringComparison.OrdinalIgnoreCase)
                                    ? "_loop"
                                    : $"_{assetMappings.IndexOf(mapping) + 1}";
                        }
                        else if (extension == ".avfx" && avfxCount > 1)
                        {
                            string gameName = Path.GetFileNameWithoutExtension(
                                ToNativePath(mapping.Key));
                            suffix = "_" + (string.IsNullOrWhiteSpace(gameName)
                                ? $"vfx{assetMappings.IndexOf(mapping) + 1}"
                                : gameName);
                        }

                        string directory = Path.GetDirectoryName(ToNativePath(mapping.Value)) ?? "";
                        string fileName = Sanitize(option.Name, extension, suffix);
                        string desired = Path.Combine(directory, fileName);
                        result.Add(new MappingOwner(
                            group,
                            option,
                            mapping.Key,
                            mapping.Value,
                            desired));
                    }
                }
            }
            return result;
        }

        private static List<PlannedCopy> BuildPlan(
            string modDirectory,
            IReadOnlyList<MappingOwner> owners)
        {
            var plans = new List<PlannedCopy>();
            var reserved = Directory.EnumerateFiles(modDirectory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sourceGroup in owners.GroupBy(
                         owner => Normalize(owner.SourceRelativePath),
                         StringComparer.OrdinalIgnoreCase))
            {
                string sourceRelative = sourceGroup.First().SourceRelativePath;
                if (!File.Exists(ResolveInsideMod(modDirectory, sourceRelative)))
                    continue;

                foreach (var desiredGroup in sourceGroup.GroupBy(
                             owner => Normalize(owner.DesiredRelativePath),
                             StringComparer.OrdinalIgnoreCase))
                {
                    string requestedRelative = desiredGroup.First().DesiredRelativePath;
                    string destinationRelative = GetAvailablePath(
                        modDirectory,
                        sourceRelative,
                        requestedRelative,
                        reserved,
                        claimed);
                    claimed.Add(ResolveInsideMod(modDirectory, destinationRelative));
                    if (!SamePath(
                            ResolveInsideMod(modDirectory, sourceRelative),
                            ResolveInsideMod(modDirectory, destinationRelative)))
                    {
                        plans.Add(new PlannedCopy(
                            sourceRelative,
                            destinationRelative,
                            desiredGroup.ToList()));
                    }
                }
            }

            return plans;
        }

        private static string GetAvailablePath(
            string modDirectory,
            string sourceRelative,
            string requestedRelative,
            HashSet<string> reserved,
            HashSet<string> claimed)
        {
            string source = ResolveInsideMod(modDirectory, sourceRelative);
            string requested = ResolveInsideMod(modDirectory, requestedRelative);
            if ((!reserved.Contains(requested) || SamePath(source, requested)) &&
                !claimed.Contains(requested))
            {
                return Path.GetRelativePath(modDirectory, requested);
            }

            string directory = Path.GetDirectoryName(requested)!;
            string name = Path.GetFileNameWithoutExtension(requested);
            string extension = Path.GetExtension(requested);
            for (int index = 2; ; index++)
            {
                string candidate = Path.Combine(directory, $"{name}_{index}{extension}");
                if (!reserved.Contains(candidate) && !claimed.Contains(candidate))
                    return Path.GetRelativePath(modDirectory, candidate);
            }
        }

        private static string Sanitize(string optionName, string extension, string suffix)
        {
            string name = string.IsNullOrWhiteSpace(optionName) ? "asset" : optionName.Trim();
            var invalid = Path.GetInvalidFileNameChars();
            name = new string(name.Select(character =>
                invalid.Contains(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(name))
                name = "asset";
            return name + suffix + extension;
        }

        private static bool IsSupported(string path) => IsPap(path) ||
            path.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase);

        private static bool IsPap(string path) =>
            path.EndsWith(".pap", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string path) =>
            path.Replace('\\', '/').Trim();

        private static string ToNativePath(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

        private static string ResolveInsideMod(string modDirectory, string relativePath) =>
            ResolveInsideRoot(modDirectory, relativePath);

        private static string ResolveInsideRoot(string root, string relativePath)
        {
            string safeRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, ToNativePath(relativePath)));
            if (!fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Mapped asset points outside the mod: {relativePath}");
            return fullPath;
        }

        private static bool SamePath(string left, string right) =>
            string.Equals(
                Path.GetFullPath(left).TrimEnd('\\', '/'),
                Path.GetFullPath(right).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);

        private static void WriteJsonAtomic(string path, string json)
        {
            string temporaryPath = path + ".stage-writing";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, true);
        }
    }
}
