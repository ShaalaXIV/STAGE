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
        bool UsesSeparateAudioMod);

    public static class ModProfileDetector
    {
        private static readonly ModProfile Generic = new(
            ModProfileKind.Generic, "Generic Penumbra Mod", null, "beesknees", "sdance", false);

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
                    return new(ModProfileKind.Thunderdome, "Thunderdome", "sound/dam.scd",
                        "Stage Toggle // Character Height", null, false);
                }

                if (HasGroups("Required to Play Music", "Dances", "DJ Booth Lights", "Lights and Lazers") &&
                    HasPaths("sound/bpmloop.scd", "vfx/bpmloop-others.avfx", "vfx/alinapickle2.avfx"))
                {
                    return new(ModProfileKind.Pickles, "Pickles", "sound/bpmloop.scd",
                        "Dances", "Dances", false);
                }

                bool hasCassSlots = Enumerable.Range(1, 5).All(slot =>
                    gamePaths.Contains($"vfx/slot{slot}.avfx"));
                if (HasGroups("beesknees", "beesknees 2", "sdance", "Luna VFX Options") && hasCassSlots)
                {
                    return new(ModProfileKind.CassBase, "Cass Base", null,
                        "beesknees", "sdance", false);
                }

                bool hasShaalaSlots = Enumerable.Range(1, 6).All(slot =>
                    gamePaths.Contains($"vfx/slot{slot}.avfx"));
                if (HasGroups("beesknees", "sdance", "Dance specific VFX colors", "DJ Groupings") && hasShaalaSlots)
                {
                    return new(ModProfileKind.ShaalaDj, "Shaala DJ", null,
                        "beesknees", "sdance", true);
                }

                if (HasGroups("Base Files", "Race", "Sparkle VFX", "EDM", "D&B") &&
                    HasPaths("sound/lolo.scd", "vfx/dj.avfx", "chara/human/c0101/animation/a0001/bt_common/emote_sp/sp12_loop.pap"))
                {
                    return new(ModProfileKind.YueLuDj, "Yue & Lu's DJ", "sound/lolo.scd",
                        "Base Files", null, false);
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

        public static string NormalizeGamePath(string path) =>
            (path ?? "").Replace('\\', '/').TrimStart('/');
    }
}
