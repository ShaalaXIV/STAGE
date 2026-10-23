using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public enum ModProfileKind
    {
        Generic,
        Pickles,
        Thunderdome,
        CassBase,
        ShaalaDj,
        YueLuDj
    }

    public sealed record ModProfile(
        ModProfileKind Kind,
        string DisplayName,
        string? BaselineScdKey,
        string? LoopAnimationGroup,
        string? StartLoopAnimationGroup,
        bool UsesSeparateAudioMod,
        string? PreferredAudioGroup = null);

    public static class ModProfileDetector
    {
        private static readonly ModProfile Generic = new(
            ModProfileKind.Generic, "Generic Penumbra Mod", null, "beesknees", "sdance", false);

        public static IReadOnlyList<ModProfile> Profiles { get; } = new[]
        {
            Generic,
            new ModProfile(ModProfileKind.Pickles, "Pickles", "sound/bpmloop.scd", "Dances", "Dances", false, "test"),
            new ModProfile(ModProfileKind.Thunderdome, "Thunderdome", "sound/dam.scd", "Stage Toggle // Character Height", null, false, "MUSIC // My Playlist"),
            new ModProfile(ModProfileKind.CassBase, "Cass Base", null, "beesknees", "sdance", false),
            new ModProfile(ModProfileKind.ShaalaDj, "Shaala DJ", null, "beesknees", "sdance", true),
            new ModProfile(ModProfileKind.YueLuDj, "Yue & Lu's DJ", "sound/lolo.scd", "Base Files", null, false, "EDM")
        };

        public static ModProfile GetProfile(ModProfileKind kind) =>
            Profiles.First(profile => profile.Kind == kind);

        public static ModProfile Resolve(string modDirectory)
        {
            ModProfileKind? assigned = Settings.GetModProfileOverride(modDirectory);
            return assigned.HasValue ? GetProfile(assigned.Value) : Detect(modDirectory);
        }

        public static ModProfile Detect(string modDirectory)
        {
            if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory) ||
                !File.Exists(Path.Combine(modDirectory, "meta.json")))
                return Generic;

            try
            {
                var groups = LoadGroups(modDirectory);
                var names = new HashSet<string>(groups.Select(group => group.Name ?? ""), StringComparer.OrdinalIgnoreCase);
                var gamePaths = new HashSet<string>(groups
                    .SelectMany(group => group.Options ?? new List<Option>())
                    .SelectMany(option => option.Files?.Keys ?? Enumerable.Empty<string>())
                    .Select(NormalizeGamePath), StringComparer.OrdinalIgnoreCase);

                bool HasGroups(params string[] required) => required.All(names.Contains);
                bool HasPaths(params string[] required) => required.All(path => gamePaths.Contains(NormalizeGamePath(path)));

                // Structural signatures deliberately require several independent matches. A folder
                // name or author alone is never enough to select a write profile.
                if (HasGroups("VJ SCREEN // Background", "VJ SCREEN // Foreground", "VFX // Speaker RGB", "VFX // Speaker Effects") &&
                    HasPaths("sound/dam.scd", "vfx/Stage.avfx"))
                {
                    return GetProfile(ModProfileKind.Thunderdome);
                }

                if (HasGroups("Required to Play Music", "Dances", "DJ Booth Lights", "Lights and Lazers") &&
                    HasPaths("sound/bpmloop.scd", "vfx/bpmloop-others.avfx", "vfx/alinapickle2.avfx"))
                {
                    return GetProfile(ModProfileKind.Pickles);
                }

                bool hasCassSlots = Enumerable.Range(1, 5).All(slot =>
                    gamePaths.Contains($"vfx/slot{slot}.avfx"));
                if (HasGroups("beesknees", "beesknees 2", "sdance", "Luna VFX Options") && hasCassSlots)
                {
                    return GetProfile(ModProfileKind.CassBase);
                }

                bool hasShaalaSlots = Enumerable.Range(1, 6).All(slot =>
                    gamePaths.Contains($"vfx/slot{slot}.avfx"));
                if (HasGroups("beesknees", "sdance", "Dance specific VFX colors", "DJ Groupings") && hasShaalaSlots)
                {
                    return GetProfile(ModProfileKind.ShaalaDj);
                }

                if (HasGroups("Base Files", "Race", "Sparkle VFX", "EDM", "D&B") &&
                    HasPaths("sound/lolo.scd", "vfx/dj.avfx", "chara/human/c0101/animation/a0001/bt_common/emote_sp/sp12_loop.pap"))
                {
                    return GetProfile(ModProfileKind.YueLuDj);
                }
            }
            catch
            {
                // Invalid or incomplete mods remain generic. The format detector still guards every
                // actual write and will provide the actionable error rather than guessing here.
            }

            return Generic;
        }

        public static IReadOnlyList<VfxPapGroup> LoadGroups(string modDirectory)
        {
            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
                return meta!.Groups;

            var groups = new List<VfxPapGroup>();
            foreach (string path in Directory.GetFiles(modDirectory, "group_*.json")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var group = Newtonsoft.Json.JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(path));
                    if (group == null)
                        continue;
                    group.FilePath = path;
                    group.Options ??= new List<Option>();
                    groups.Add(group);
                }
                catch
                {
                    // A malformed group is not evidence for a profile.
                }
            }
            return groups;
        }

        public static string? ResolvePapDonor(string modDirectory, string groupReference)
        {
            if (Resolve(modDirectory).Kind == ModProfileKind.Generic)
                return null;

            VfxPapGroup? group = FindGroup(modDirectory, groupReference);
            var mappings = group?.Options
                .SelectMany(option => option.Files ?? new Dictionary<string, string>())
                .Where(mapping => mapping.Key.EndsWith(".pap", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (mappings == null || mappings.Count == 0)
                return null;

            var loopMappings = mappings.Where(mapping =>
                Path.GetFileNameWithoutExtension(mapping.Key).EndsWith("loop", StringComparison.OrdinalIgnoreCase)).ToList();
            if (loopMappings.Count > 0)
                mappings = loopMappings;

            return mappings
                .Select(mapping => ResolveExistingLocalFile(modDirectory, mapping.Value))
                .Where(path => path != null)
                .GroupBy(path => path!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(items => items.Count())
                .Select(items => items.Key)
                .FirstOrDefault();
        }

        public static string? ResolveScdDonor(string modDirectory)
        {
            if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory))
                return null;

            ModProfile profile = Resolve(modDirectory);
            var groups = LoadGroups(modDirectory);
            IEnumerable<VfxPapGroup> ordered = groups;
            if (!string.IsNullOrWhiteSpace(profile.PreferredAudioGroup))
                ordered = groups.OrderByDescending(group => string.Equals(group.Name, profile.PreferredAudioGroup, StringComparison.OrdinalIgnoreCase));

            return ordered
                .SelectMany(group => group.Options ?? new List<Option>())
                .SelectMany(option => option.Files ?? new Dictionary<string, string>())
                .Where(mapping => mapping.Key.EndsWith(".scd", StringComparison.OrdinalIgnoreCase) &&
                                  mapping.Value.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
                .Select(mapping => ResolveExistingLocalFile(modDirectory, mapping.Value))
                .FirstOrDefault(path => path != null);
        }

        private static VfxPapGroup? FindGroup(string modDirectory, string groupReference)
        {
            if (PenumbraMeta.TryParseGroupReference(groupReference, out _, out string groupId, out string groupName))
                return LoadGroups(modDirectory).FirstOrDefault(group =>
                    string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));

            return LoadGroups(modDirectory).FirstOrDefault(group =>
                string.Equals(group.FilePath, groupReference, StringComparison.OrdinalIgnoreCase));
        }

        private static string? ResolveExistingLocalFile(string modDirectory, string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath)) return null;
            string root = Path.GetFullPath(modDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(modDirectory, localPath.Replace('/', Path.DirectorySeparatorChar)));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate) ? candidate : null;
        }

        public static string NormalizeGamePath(string path) =>
            (path ?? "").Replace('\\', '/').TrimStart('/');
    }
}
