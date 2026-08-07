using Newtonsoft.Json;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public sealed record AvfxImportRequest(
        string OptionName,
        string GroupPath,
        int SlotNumber,
        string SourceAvfxPath);

    public sealed record AvfxImportResult(
        string GroupPath,
        string GroupName,
        string OptionName,
        string CopiedFile,
        string GamePath,
        string LocalPath);

    public static class VfxAvfxImporter
    {
        public static AvfxImportResult Import(AvfxImportRequest request)
        {
            ValidateRequest(request, requireGroupPath: true);

            string modDirectory = GetModDirectory();
            Directory.CreateDirectory(modDirectory);

            string localPath = CopyAvfxIntoMod(request.SourceAvfxPath, modDirectory, request.OptionName);
            return ImportIntoSlotGroup(request, modDirectory, localPath);
        }

        public static List<AvfxImportResult> ImportToAllSlots(AvfxImportRequest request)
        {
            ValidateRequest(request, requireGroupPath: false);

            string modDirectory = GetModDirectory();
            Directory.CreateDirectory(modDirectory);

            string localPath = CopyAvfxIntoMod(request.SourceAvfxPath, modDirectory, request.OptionName);
            var results = new List<AvfxImportResult>();
            for (int slotNumber = 1; slotNumber <= 6; slotNumber++)
            {
                var slotRequest = request with
                {
                    GroupPath = SuggestedDefaultGroupPath(slotNumber),
                    SlotNumber = slotNumber
                };
                results.Add(ImportIntoSlotGroup(slotRequest, modDirectory, localPath));
            }

            return results;
        }

        private static AvfxImportResult ImportIntoSlotGroup(AvfxImportRequest request, string modDirectory, string localPath)
        {
            var group = LoadOrCreateSlotGroup(request.GroupPath, request.SlotNumber);
            string gamePath = $"vfx/slot{request.SlotNumber}.avfx";

            var option = new Option
            {
                Name = request.OptionName.Trim(),
                Description = "",
                Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [gamePath] = localPath
                }
            };

            AddSeparatorIfNeeded(group);
            int existingIndex = group.Options.FindIndex(existing =>
                string.Equals(existing.Name, option.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                group.Options[existingIndex] = option;
            else
                group.Options.Add(option);

            SaveGroup(request.GroupPath, group);

            return new AvfxImportResult(
                request.GroupPath,
                group.Name,
                option.Name,
                Path.Combine(modDirectory, localPath),
                gamePath,
                localPath);
        }

        private static void ValidateRequest(AvfxImportRequest request, bool requireGroupPath)
        {
            if (string.IsNullOrWhiteSpace(request.OptionName))
                throw new InvalidOperationException("Give the imported VFX option a name first.");
            if (requireGroupPath && string.IsNullOrWhiteSpace(request.GroupPath))
                throw new InvalidOperationException("Pick a target VFX slot group first.");
            if (request.SlotNumber < 1 || request.SlotNumber > 6)
                throw new InvalidOperationException("Pick VFX slot 1 through 6.");
            if (string.IsNullOrWhiteSpace(request.SourceAvfxPath) || !File.Exists(request.SourceAvfxPath))
                throw new InvalidOperationException("Pick an AVFX file first.");
        }

        public static string SuggestedDefaultGroupPath(int slotNumber)
        {
            string modDirectory = GetModDirectory();
            Directory.CreateDirectory(modDirectory);

            string preferredName = $"VFX Slot {slotNumber}";
            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                var group = meta!.Groups.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, preferredName, StringComparison.OrdinalIgnoreCase));
                if (group != null)
                    return group.FilePath;

                return PenumbraMeta.CreateGroupReference(
                    modDirectory,
                    new VfxPapGroup
                    {
                        Id = PenumbraMeta.EnsureGuid(null),
                        Name = preferredName
                    });
            }

            string? existing = Directory.GetFiles(modDirectory, "group_*.json")
                .FirstOrDefault(path =>
                {
                    try
                    {
                        var group = JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(path));
                        return group != null &&
                               string.Equals(group.Name, preferredName, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            int groupNumber = slotNumber + 3;
            return Path.Combine(modDirectory, $"group_{groupNumber:000}_vfx slot {slotNumber}.json");
        }

        public static List<string> GetVfxSlotGroupPaths()
        {
            string modDirectory = GetModDirectory();
            if (!Directory.Exists(modDirectory))
                return new List<string>();

            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                return meta!.Groups
                    .Where(group => group.Kind == VfxPapGroupKind.VfxSlot)
                    .Select(group => group.FilePath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return Directory.GetFiles(modDirectory, "group_*.json")
                .Where(path =>
                {
                    try
                    {
                        var group = JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(path));
                        return group?.Kind == VfxPapGroupKind.VfxSlot;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static int GuessSlotNumberFromGroupPath(string groupPath)
        {
            if (!File.Exists(groupPath))
            {
                if (PenumbraMeta.IsGroupReference(groupPath) &&
                    PenumbraMeta.TryParseGroupReference(groupPath, out string modDirectory, out _, out _))
                {
                    var meta = PenumbraMeta.Load(modDirectory);
                    var group = meta.FindGroupByReference(groupPath);
                    if (group != null)
                        return GuessSlotNumberFromGroup(group);
                }

                return 1;
            }

            try
            {
                var group = JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(groupPath));
                var slotPath = group?.Options
                    .SelectMany(option => option.Files?.Keys ?? Enumerable.Empty<string>())
                    .FirstOrDefault(path => path.StartsWith("vfx/slot", StringComparison.OrdinalIgnoreCase) &&
                                            path.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase));
                if (slotPath == null)
                    return 1;

                string numberText = new string(slotPath
                    .Skip("vfx/slot".Length)
                    .TakeWhile(char.IsDigit)
                    .ToArray());
                return int.TryParse(numberText, out int slot) ? Math.Clamp(slot, 1, 6) : 1;
            }
            catch
            {
                return 1;
            }
        }

        private static VfxPapGroup LoadOrCreateSlotGroup(string groupPath, int slotNumber)
        {
            if (PenumbraMeta.IsGroupReference(groupPath) &&
                PenumbraMeta.TryParseGroupReference(groupPath, out string modDirectory, out _, out _))
            {
                var meta = PenumbraMeta.Load(modDirectory);
                return meta.FindOrCreateGroup(groupPath, () => new VfxPapGroup
                {
                    Id = PenumbraMeta.EnsureGuid(null),
                    Name = $"VFX Slot {slotNumber}",
                    Priority = slotNumber + 1,
                    Type = "Single",
                    Options = new List<Option>()
                });
            }

            if (File.Exists(groupPath))
            {
                var group = JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(groupPath));
                if (group != null)
                {
                    group.FilePath = groupPath;
                    group.Options ??= new List<Option>();
                    return group;
                }
            }

            return new VfxPapGroup
            {
                Version = 0,
                Name = $"VFX Slot {slotNumber}",
                Description = "",
                Image = "",
                Page = 0,
                Priority = slotNumber + 1,
                Type = "Single",
                DefaultSettings = 0,
                Options = new List<Option>()
            };
        }

        private static void SaveGroup(string groupPath, VfxPapGroup group)
        {
            if (PenumbraMeta.IsGroupReference(groupPath) &&
                PenumbraMeta.TryParseGroupReference(groupPath, out string modDirectory, out _, out _))
            {
                var meta = PenumbraMeta.Load(modDirectory);
                var target = meta.FindOrCreateGroup(groupPath, () => group);
                target.Id = string.IsNullOrWhiteSpace(target.Id)
                    ? PenumbraMeta.EnsureGuid(group.Id)
                    : target.Id;
                group.Id = target.Id;
                target.Name = group.Name;
                target.Priority = group.Priority;
                target.Type = group.Type;
                target.Options = group.Options;
                meta.Save();
                PenumbraApi.ScheduleReloadModFolder(modDirectory);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(groupPath)!);
            string json = JsonConvert.SerializeObject(group, Formatting.Indented);
            PenumbraMeta.RequireFormat(
                Path.GetDirectoryName(groupPath) ?? "",
                PenumbraModFormat.LegacyGroupFiles);
            File.WriteAllText(groupPath, json);
            PenumbraApi.ScheduleReloadModFolder(Path.GetDirectoryName(groupPath) ?? "");
        }

        private static void AddSeparatorIfNeeded(VfxPapGroup group)
        {
            if (group.Options.Count == 0)
            {
                group.Options.Add(new Option
                {
                    Name = "-----",
                    Description = "",
                    Files = new Dictionary<string, string>(),
                    FileSwaps = new Dictionary<string, string>(),
                    Manipulations = new List<string>()
                });
            }
        }

        private static string CopyAvfxIntoMod(
            string sourcePath,
            string modDirectory,
            string optionName)
        {
            string targetDirectory = Path.Combine(modDirectory, "vfx");
            Directory.CreateDirectory(targetDirectory);

            string fileName = SanitizeAvfxFileName(optionName);
            string targetPath = UniquePath(Path.Combine(targetDirectory, fileName));
            File.Copy(sourcePath, targetPath, overwrite: false);
            return Path.GetRelativePath(modDirectory, targetPath);
        }

        private static string SanitizeAvfxFileName(string value)
        {
            string baseName = string.IsNullOrWhiteSpace(value) ? "vfx" : value.Trim();
            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(baseName
                .Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray())
                .Trim()
                .TrimEnd('.');
            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = "vfx";
            return cleaned + ".avfx";
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            string directory = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }

        private static string GetModDirectory()
        {
            string modDirectory = Settings.VfxModFolder;
            if (string.IsNullOrWhiteSpace(modDirectory))
            {
                throw new InvalidOperationException("Set the Penumbra mod folder first.");
            }

            return modDirectory;
        }

        private static int GuessSlotNumberFromGroup(VfxPapGroup group)
        {
            var slotPath = group.Options
                .SelectMany(option => option.Files?.Keys ?? Enumerable.Empty<string>())
                .FirstOrDefault(path => path.StartsWith("vfx/slot", StringComparison.OrdinalIgnoreCase) &&
                                        path.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase));
            if (slotPath == null)
                return 1;

            string numberText = new string(slotPath
                .Skip("vfx/slot".Length)
                .TakeWhile(char.IsDigit)
                .ToArray());
            return int.TryParse(numberText, out int slot) ? Math.Clamp(slot, 1, 6) : 1;
        }
    }
}
