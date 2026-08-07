using Newtonsoft.Json;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public enum PapImportMode
    {
        SingleLoop,
        StartAndLoop
    }

    public sealed record PapImportRequest(
        PapImportMode Mode,
        string OptionName,
        string GroupPath,
        string? SinglePapPath,
        string? SinglePapName,
        string? StartPapPath,
        string? StartPapName,
        string? LoopPapName,
        string? LoopPapPath,
        string? TemplatePapPath = null);

    public sealed record PapImportResult(
        string GroupPath,
        string GroupName,
        string OptionName,
        IReadOnlyList<string> CopiedFiles,
        int MappingCount);

    public static class VfxPapImporter
    {
        public const string BeesKneesGroupName = "beesknees";
        public const string StepdanceGroupName = "sdance";

        private static readonly string[] SingleLoopTargets =
        [
            "chara/human/c0101/animation/a0001/bt_common/emote/dance16_loop.pap",
            "chara/human/c0801/animation/a0001/bt_common/emote/dance16_loop.pap",
            "chara/human/c0901/animation/a0001/bt_common/emote/dance16_loop.pap",
            "chara/human/c1301/animation/a0001/bt_common/emote/dance16_loop.pap",
            "chara/human/c1401/animation/a0001/bt_common/emote/dance16_loop.pap"
        ];

        private const string StepdanceLoopTarget =
            "chara/human/c0101/animation/a0001/bt_common/emote/dance_male_loop.pap";

        private const string StepdanceStartTarget =
            "chara/human/c0101/animation/a0001/bt_common/emote/dance_male_start.pap";

        public static PapImportResult Import(PapImportRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.OptionName))
                throw new InvalidOperationException("Give the imported PAP option a name first.");
            if (string.IsNullOrWhiteSpace(request.GroupPath))
                throw new InvalidOperationException("Pick a target animation group first.");

            string modDirectory = GetModDirectory();
            Directory.CreateDirectory(modDirectory);

            var group = LoadOrCreateGroup(request.GroupPath, request.Mode);
            var copiedFiles = new List<string>();
            Option option = request.Mode switch
            {
                PapImportMode.SingleLoop => BuildSingleLoopOption(request, modDirectory, group, copiedFiles),
                PapImportMode.StartAndLoop => BuildStartAndLoopOption(request, modDirectory, copiedFiles),
                _ => throw new InvalidOperationException("Unknown PAP import mode.")
            };

            AddSeparatorIfNeeded(group);
            int existingIndex = group.Options.FindIndex(existing =>
                string.Equals(existing.Name, option.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                group.Options[existingIndex] = option;
            else
                group.Options.Add(option);

            SaveGroup(request.GroupPath, group);

            return new PapImportResult(
                request.GroupPath,
                group.Name,
                option.Name,
                copiedFiles,
                option.Files.Count);
        }

        public static string SuggestedDefaultGroupPath(PapImportMode mode)
        {
            string modDirectory = GetModDirectory();
            Directory.CreateDirectory(modDirectory);

            string preferredName = mode == PapImportMode.StartAndLoop
                ? StepdanceGroupName
                : BeesKneesGroupName;

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
                        Name = preferredName,
                        Id = PenumbraMeta.EnsureGuid(null)
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

            string fileName = mode == PapImportMode.StartAndLoop
                ? "group_002_sdance.json"
                : "group_001_beesknees.json";
            return Path.Combine(modDirectory, fileName);
        }

        public static List<string> GetAnimationGroupPaths()
        {
            string modDirectory = GetModDirectory();
            if (!Directory.Exists(modDirectory))
                return new List<string>();

            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                return meta!.Groups
                    .Where(group => group.Kind == VfxPapGroupKind.Animation)
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
                        return group?.Kind == VfxPapGroupKind.Animation;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetPapImportGroupPaths()
        {
            string modDirectory = GetModDirectory();
            if (!Directory.Exists(modDirectory))
                return new List<string>();

            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                return meta!.Groups
                    .Where(group => !VfxPapGroup.IsAudioPlaylistGroup(group))
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
                        return group != null && !VfxPapGroup.IsAudioPlaylistGroup(ToPlaylistShape(group));
                    }
                    catch
                    {
                        return false;
                    }
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Option BuildSingleLoopOption(PapImportRequest request, string modDirectory, VfxPapGroup group, List<string> copiedFiles)
        {
            if (string.IsNullOrWhiteSpace(request.SinglePapPath) || !File.Exists(request.SinglePapPath))
                throw new InvalidOperationException("Pick the single loop PAP first.");

            string relativePapPath = CopyPapIntoMod(
                request.SinglePapPath,
                modDirectory,
                "pap\\nostart",
                request.OptionName,
                copiedFiles);
            if (!string.IsNullOrWhiteSpace(request.TemplatePapPath))
            {
                BeesKneesPapTmbPatcher.PatchImportedPapFromTemplate(
                    Path.Combine(modDirectory, relativePapPath),
                    request.TemplatePapPath);
            }
            else
            {
                BeesKneesPapTmbPatcher.PatchImportedBeesKneesPap(Path.Combine(modDirectory, relativePapPath));
            }

            return new Option
            {
                Name = request.OptionName.Trim(),
                Description = "",
                Files = GetSingleLoopTargetPaths(group, request).ToDictionary(
                    target => target,
                    _ => relativePapPath,
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Option BuildStartAndLoopOption(PapImportRequest request, string modDirectory, List<string> copiedFiles)
        {
            if (string.IsNullOrWhiteSpace(request.StartPapPath) || !File.Exists(request.StartPapPath))
                throw new InvalidOperationException("Pick the startup PAP first.");
            if (string.IsNullOrWhiteSpace(request.LoopPapPath) || !File.Exists(request.LoopPapPath))
                throw new InvalidOperationException("Pick the loop PAP first.");
            if (string.IsNullOrWhiteSpace(request.TemplatePapPath) || !File.Exists(request.TemplatePapPath))
                throw new InvalidOperationException("Configure a valid donor animation under Settings → Animation first.");

            string startPath = CopyPapIntoMod(
                request.StartPapPath,
                modDirectory,
                "pap\\start",
                request.OptionName + "_start",
                copiedFiles);
            string loopPath = CopyPapIntoMod(
                request.LoopPapPath,
                modDirectory,
                "pap\\start",
                request.OptionName + "_loop",
                copiedFiles);
            BeesKneesPapTmbPatcher.PatchImportedPapFromTemplate(
                Path.Combine(modDirectory, loopPath),
                request.TemplatePapPath);

            return new Option
            {
                Name = request.OptionName.Trim(),
                Description = "",
                Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [StepdanceLoopTarget] = loopPath,
                    [StepdanceStartTarget] = startPath
                }
            };
        }

        private static VfxPapGroup LoadOrCreateGroup(string groupPath, PapImportMode mode)
        {
            if (PenumbraMeta.IsGroupReference(groupPath) &&
                PenumbraMeta.TryParseGroupReference(groupPath, out string modDirectory, out _, out _))
            {
                var meta = PenumbraMeta.Load(modDirectory);
                return meta.FindOrCreateGroup(groupPath, () => new VfxPapGroup
                {
                    Id = PenumbraMeta.EnsureGuid(null),
                    Name = mode == PapImportMode.StartAndLoop ? StepdanceGroupName : BeesKneesGroupName,
                    Priority = mode == PapImportMode.StartAndLoop ? 1 : 0,
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
                Name = mode == PapImportMode.StartAndLoop ? StepdanceGroupName : BeesKneesGroupName,
                Description = "",
                Image = "",
                Page = 0,
                Priority = mode == PapImportMode.StartAndLoop ? 1 : 0,
                Type = "Single",
                DefaultSettings = 0,
                Options = new List<Option>()
            };
        }

        private static IReadOnlyList<string> GetSingleLoopTargetPaths(VfxPapGroup group, PapImportRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TemplatePapPath))
                return SingleLoopTargets;

            var targets = group.Options
                .Where(option => option.Files != null)
                .SelectMany(option => option.Files.Keys)
                .Where(path => path.EndsWith(".pap", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targets.Count == 0)
            {
                throw new InvalidOperationException(
                    "The selected group JSON does not contain any existing PAP mappings to borrow. Pick an animation group JSON with PAP mappings first.");
            }

            return targets;
        }

        private static Playlist ToPlaylistShape(VfxPapGroup group)
        {
            return new Playlist
            {
                Name = group.Name,
                Priority = group.Priority,
                Options = group.Options
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

        private static string CopyPapIntoMod(
            string sourcePath,
            string modDirectory,
            string relativeFolder,
            string? requestedName,
            List<string> copiedFiles)
        {
            string targetDirectory = Path.Combine(modDirectory, relativeFolder);
            Directory.CreateDirectory(targetDirectory);

            string fileName = SanitizePapFileName(
                string.IsNullOrWhiteSpace(requestedName)
                    ? Path.GetFileNameWithoutExtension(sourcePath)
                    : requestedName);
            string targetPath = UniquePath(Path.Combine(targetDirectory, fileName));
            File.Copy(sourcePath, targetPath, overwrite: false);
            copiedFiles.Add(targetPath);
            return Path.GetRelativePath(modDirectory, targetPath);
        }

        public static string SanitizePapFileName(string value)
        {
            string baseName = string.IsNullOrWhiteSpace(value)
                ? "animation"
                : Path.GetFileNameWithoutExtension(value.Trim());

            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(baseName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = "animation";

            return cleaned.EndsWith(".pap", StringComparison.OrdinalIgnoreCase)
                ? cleaned
                : cleaned + ".pap";
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
    }
}
