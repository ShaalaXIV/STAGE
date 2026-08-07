using Newtonsoft.Json;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public sealed record PapRenameResult(
        string GroupPath,
        string NewName,
        IReadOnlyList<string> RenamedFiles,
        int UpdatedMappings);

    public static class VfxPapRenamer
    {
        public static PapRenameResult RenameOption(string groupPath, string optionName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidOperationException("Give the PAP option a new name first.");

            var group = LoadGroup(groupPath);
            var option = group.Options.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, optionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
                throw new InvalidOperationException("Could not find that PAP option in the group JSON.");

            option.Name = newName.Trim();

            var modDirectory = GetModDirectory(groupPath);
            var renamedFiles = new List<string>();
            int updatedMappings = 0;
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var localPath in option.Files.Values
                         .Where(IsPapPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList())
            {
                string suffix = GuessSuffix(localPath, pathMap.Count, option.Files.Values.Count(IsPapPath));
                string requestedName = suffix.Length == 0 ? newName : $"{newName}_{suffix}";
                string newRelativePath = RenamePhysicalFile(modDirectory, localPath, requestedName, renamedFiles);
                pathMap[Normalize(localPath)] = newRelativePath;
            }

            foreach (var key in option.Files.Keys.ToList())
            {
                string currentValue = option.Files[key];
                if (pathMap.TryGetValue(Normalize(currentValue), out string newValue))
                {
                    option.Files[key] = newValue;
                    updatedMappings++;
                }
            }

            SaveGroup(groupPath, group);
            return new PapRenameResult(groupPath, option.Name, renamedFiles, updatedMappings);
        }

        public static PapRenameResult RenameMapping(string groupPath, string oldLocalPath, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidOperationException("Give the PAP file a new name first.");
            if (!IsPapPath(oldLocalPath))
                throw new InvalidOperationException("This rename action only supports PAP file mappings.");

            var group = LoadGroup(groupPath);
            var modDirectory = GetModDirectory(groupPath);
            var renamedFiles = new List<string>();
            string newRelativePath = RenamePhysicalFile(modDirectory, oldLocalPath, newName, renamedFiles);

            int updatedMappings = 0;
            string oldNormalized = Normalize(oldLocalPath);
            foreach (var option in group.Options)
            {
                foreach (var key in option.Files.Keys.ToList())
                {
                    if (string.Equals(Normalize(option.Files[key]), oldNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        option.Files[key] = newRelativePath;
                        updatedMappings++;
                    }
                }
            }

            SaveGroup(groupPath, group);
            return new PapRenameResult(groupPath, Path.GetFileNameWithoutExtension(newRelativePath), renamedFiles, updatedMappings);
        }

        private static string RenamePhysicalFile(
            string modDirectory,
            string oldRelativePath,
            string requestedName,
            List<string> renamedFiles)
        {
            string oldFullPath = Path.Combine(modDirectory, ToNativePath(oldRelativePath));
            if (!File.Exists(oldFullPath))
                throw new FileNotFoundException("Could not find the PAP file to rename.", oldFullPath);

            string directory = Path.GetDirectoryName(oldFullPath) ?? modDirectory;
            string newFileName = VfxPapImporter.SanitizePapFileName(requestedName);
            string newFullPath = UniquePath(Path.Combine(directory, newFileName), oldFullPath);

            if (!string.Equals(oldFullPath, newFullPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(oldFullPath, newFullPath);
                renamedFiles.Add(newFullPath);
            }

            return Path.GetRelativePath(modDirectory, newFullPath);
        }

        private static string UniquePath(string path, string oldPath)
        {
            if (!File.Exists(path) || string.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase))
                return path;

            string directory = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
                if (!File.Exists(candidate) || string.Equals(candidate, oldPath, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }

        private static VfxPapGroup LoadGroup(string groupPath)
        {
            if (PenumbraMeta.IsGroupReference(groupPath) &&
                PenumbraMeta.TryParseGroupReference(groupPath, out string modDirectory, out _, out _))
            {
                var meta = PenumbraMeta.Load(modDirectory);
                var groupReference = meta.FindGroupByReference(groupPath)
                    ?? throw new FileNotFoundException("Could not find the source group in meta.json.", groupPath);
                groupReference.Options ??= new List<Option>();
                foreach (var option in groupReference.Options)
                    option.Files ??= new Dictionary<string, string>();
                return groupReference;
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
                target.Name = group.Name;
                target.Priority = group.Priority;
                target.Type = group.Type;
                target.Options = group.Options;
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

        private static string GuessSuffix(string localPath, int index, int total)
        {
            if (total <= 1)
                return "";

            string value = Normalize(localPath);
            if (value.Contains("start", StringComparison.OrdinalIgnoreCase))
                return "start";
            if (value.Contains("loop", StringComparison.OrdinalIgnoreCase))
                return "loop";

            return (index + 1).ToString();
        }

        private static bool IsPapPath(string path) =>
            path.EndsWith(".pap", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string path) =>
            path.Replace('\\', '/').Trim();

        private static string ToNativePath(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}
