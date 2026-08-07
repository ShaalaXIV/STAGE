using Newtonsoft.Json;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public enum VfxPapGroupKind
    {
        Animation,
        ColorVariant,
        VfxSlot,
        Other
    }

    public sealed class VfxPapGroup
    {
        public int Version { get; set; }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Image { get; set; } = "";
        public int Page { get; set; }
        public int Priority { get; set; }
        public string Type { get; set; } = "Single";
        public int DefaultSettings { get; set; }
        public List<Option> Options { get; set; } = new();

        [JsonIgnore]
        public string FilePath { get; set; } = "";

        [JsonIgnore]
        public VfxPapGroupKind Kind => Classify(this);

        public static List<VfxPapGroup> GetAll()
        {
            string modDirectory = Settings.VfxModFolder;
            if (string.IsNullOrWhiteSpace(modDirectory))
            {
                return new List<VfxPapGroup>();
            }

            if (!Directory.Exists(modDirectory))
                return new List<VfxPapGroup>();

            PenumbraMeta.RepairDuplicateGuids(modDirectory);

            var groups = new List<VfxPapGroup>();
            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                groups.AddRange(meta!.Groups.Where(group => !IsAudioPlaylistGroup(group)));
            }
            else
            {
                foreach (string file in Directory.GetFiles(modDirectory, "group_*.json")
                             .OrderBy(path => path, NaturalStringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var group = JsonConvert.DeserializeObject<VfxPapGroup>(File.ReadAllText(file));
                        if (group == null || IsAudioPlaylistGroup(group))
                            continue;

                        group.FilePath = file;
                        groups.Add(group);
                    }
                    catch
                    {
                        // Ignore malformed group files here; playlist loading has its own error path.
                    }
                }
            }

            return groups
                .OrderBy(group => group.Priority)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsAudioPlaylistGroup(Playlist playlist)
        {
            return playlist.Options?.Any(OptionHasScdMapping) == true;
        }

        public static bool IsAudioPlaylistGroup(VfxPapGroup group)
        {
            return group.Options?.Any(OptionHasScdMapping) == true;
        }

        public bool ShouldSerializeVersion() => Version != 0;
        public bool ShouldSerializeId() => !string.IsNullOrWhiteSpace(Id);
        public bool ShouldSerializeDescription() => !string.IsNullOrWhiteSpace(Description);
        public bool ShouldSerializeImage() => !string.IsNullOrWhiteSpace(Image);
        public bool ShouldSerializePage() => Page != 0;
        public bool ShouldSerializePriority() => Priority != 0;
        public bool ShouldSerializeDefaultSettings() => DefaultSettings != 0;

        private static bool OptionHasScdMapping(Option option)
        {
            return option.Files?.Any(pair =>
                EndsWithExtension(pair.Key, ".scd") ||
                EndsWithExtension(pair.Value, ".scd")) == true;
        }

        private static VfxPapGroupKind Classify(VfxPapGroup group)
        {
            if (group.Options.Any(option => option.Files?.Keys.Any(IsSlotPath) == true))
                return VfxPapGroupKind.VfxSlot;

            if (group.Name.Contains("color", StringComparison.OrdinalIgnoreCase) ||
                group.Options.Any(option => option.Files?.Any(pair =>
                    EndsWithExtension(pair.Key, ".atex") ||
                    EndsWithExtension(pair.Value, ".atex")) == true))
            {
                return VfxPapGroupKind.ColorVariant;
            }

            if (group.Options.Any(option => option.Files?.Any(pair =>
                    EndsWithExtension(pair.Key, ".pap") ||
                    EndsWithExtension(pair.Value, ".pap") ||
                    EndsWithExtension(pair.Key, ".tmb") ||
                    EndsWithExtension(pair.Value, ".tmb")) == true))
            {
                return VfxPapGroupKind.Animation;
            }

            return VfxPapGroupKind.Other;
        }

        private static bool IsSlotPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.StartsWith("vfx/slot", StringComparison.OrdinalIgnoreCase) &&
                   normalized.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EndsWithExtension(string path, string extension)
        {
            return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
