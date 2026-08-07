using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace STAGE
{
    public enum PenumbraModFormat
    {
        LegacyGroupFiles,
        SingularMeta
    }

    public sealed class PenumbraMeta
    {
        private const string MetaGroupSeparator = "|group|";

        public int FileVersion { get; set; } = 4;
        public string Identifier { get; set; } = Guid.NewGuid().ToString();
        public DateTime LastWrite { get; set; } = DateTime.UtcNow;
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Version { get; set; } = "1.0";
        public JObject? DefaultData { get; set; }
        public List<VfxPapGroup> Groups { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken>? ExtensionData { get; set; }

        [JsonIgnore]
        public string FilePath { get; set; } = "";

        [JsonIgnore]
        public string ModDirectory => Path.GetDirectoryName(FilePath) ?? "";

        public static bool Exists(string modDirectory) =>
            File.Exists(GetMetaPath(modDirectory));

        public static string GetMetaPath(string modDirectory) =>
            Path.Combine(modDirectory, "meta.json");

        public static PenumbraModFormat DetectFormat(string modDirectory)
        {
            if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory))
                throw new DirectoryNotFoundException("The Penumbra mod directory does not exist.");

            string metaPath = GetMetaPath(modDirectory);
            if (!File.Exists(metaPath))
                throw new FileNotFoundException("The Penumbra mod does not contain meta.json.", metaPath);

            JObject root;
            try
            {
                root = JObject.Parse(File.ReadAllText(metaPath));
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                throw new InvalidDataException(
                    "Could not determine the Penumbra mod format because meta.json is invalid or unreadable. No JSON files were changed.",
                    ex);
            }

            var groupsProperties = root.Properties()
                .Where(property => string.Equals(property.Name, nameof(Groups), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (groupsProperties.Count > 1)
            {
                throw new InvalidDataException(
                    "meta.json contains multiple Groups fields. The mod format is ambiguous, so no JSON files were changed.");
            }

            if (groupsProperties.Count == 0)
            {
                bool hasSingularData = root.Properties().Any(property =>
                    string.Equals(property.Name, nameof(DefaultData), StringComparison.OrdinalIgnoreCase));
                if (hasSingularData)
                {
                    throw new InvalidDataException(
                        "meta.json contains singular-format data but no Groups array. The mod may be mid-conversion, so no JSON files were changed.");
                }

                return PenumbraModFormat.LegacyGroupFiles;
            }

            JProperty groupsProperty = groupsProperties[0];
            if (groupsProperty.Value.Type != JTokenType.Array)
            {
                throw new InvalidDataException(
                    "meta.json contains a Groups field that is not an array. The mod format is ambiguous, so no JSON files were changed.");
            }

            try
            {
                _ = root.ToObject<PenumbraMeta>()
                    ?? throw new JsonSerializationException("meta.json deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "meta.json contains the singular mod structure but its groups are invalid. No JSON files were changed.",
                    ex);
            }

            return PenumbraModFormat.SingularMeta;
        }

        public static void RequireFormat(string modDirectory, PenumbraModFormat expected)
        {
            PenumbraModFormat actual = DetectFormat(modDirectory);
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"The mod format changed from {DescribeFormat(expected)} to {DescribeFormat(actual)} while STAGE was using it. " +
                    "Reload the mod before making changes. No JSON files were changed.");
            }
        }

        public static PenumbraMeta Load(string modDirectory)
        {
            RequireFormat(modDirectory, PenumbraModFormat.SingularMeta);
            string metaPath = GetMetaPath(modDirectory);
            var meta = JsonConvert.DeserializeObject<PenumbraMeta>(File.ReadAllText(metaPath))
                ?? throw new InvalidDataException("Could not read Penumbra meta.json.");

            meta.FilePath = metaPath;
            meta.Groups ??= new List<VfxPapGroup>();
            foreach (var group in meta.Groups)
            {
                group.Options ??= new List<Option>();
                group.FilePath = CreateGroupReference(modDirectory, group);
                foreach (var option in group.Options)
                    option.Files ??= new Dictionary<string, string>();
            }

            return meta;
        }

        public static bool TryLoad(string modDirectory, out PenumbraMeta? meta)
        {
            meta = null;
            if (DetectFormat(modDirectory) == PenumbraModFormat.LegacyGroupFiles)
                return false;

            meta = Load(modDirectory);
            return true;
        }

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
                throw new InvalidOperationException("Cannot save meta.json without a file path.");

            RequireFormat(ModDirectory, PenumbraModFormat.SingularMeta);
            LastWrite = DateTime.UtcNow;
            EnsureUniqueGuids();

            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            WriteJsonAtomic(FilePath, json);
        }

        public VfxPapGroup? FindGroupByReference(string groupReference)
        {
            if (!TryParseGroupReference(groupReference, out string modDirectory, out string groupId, out string groupName))
                return null;

            if (!SameDirectory(modDirectory, ModDirectory))
                return null;

            return Groups.FirstOrDefault(group =>
                       !string.IsNullOrWhiteSpace(groupId) &&
                       string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase))
                   ?? Groups.FirstOrDefault(group =>
                       string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));
        }

        public VfxPapGroup FindOrCreateGroup(string groupReference, Func<VfxPapGroup> create)
        {
            var existing = FindGroupByReference(groupReference);
            if (existing != null)
                return existing;

            var group = create();
            group.Id = EnsureGuid(group.Id);
            group.Options ??= new List<Option>();
            Groups.Add(group);
            group.FilePath = CreateGroupReference(ModDirectory, group);
            return group;
        }

        public static bool IsGroupReference(string value) =>
            value.Contains(MetaGroupSeparator, StringComparison.Ordinal);

        public static string CreateGroupReference(string modDirectory, VfxPapGroup group)
        {
            string id = string.IsNullOrWhiteSpace(group.Id) ? "" : group.Id;
            return GetMetaPath(modDirectory) + MetaGroupSeparator + id + MetaGroupSeparator + group.Name;
        }

        public static bool TryParseGroupReference(
            string value,
            out string modDirectory,
            out string groupId,
            out string groupName)
        {
            modDirectory = "";
            groupId = "";
            groupName = "";

            string[] parts = value.Split(MetaGroupSeparator);
            if (parts.Length < 3)
                return false;

            string metaPath = parts[0];
            modDirectory = Path.GetDirectoryName(metaPath) ?? "";
            groupId = parts[1];
            groupName = string.Join(MetaGroupSeparator, parts.Skip(2));
            return !string.IsNullOrWhiteSpace(modDirectory);
        }

        public static bool ContainsAudioGroups(string modDirectory) =>
            TryLoad(modDirectory, out var meta) &&
            meta!.Groups.Any(VfxPapGroup.IsAudioPlaylistGroup);

        public static bool ContainsVfxGroups(string modDirectory) =>
            TryLoad(modDirectory, out var meta) &&
            meta!.Groups.Any(group => !VfxPapGroup.IsAudioPlaylistGroup(group));

        public static string EnsureGuid(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out _))
                return value;
            return Guid.NewGuid().ToString();
        }

        public int EnsureUniqueGuids()
        {
            int repaired = 0;
            var used = new HashSet<Guid>();

            Identifier = EnsureUniqueGuid(Identifier, used, ref repaired);
            Groups ??= new List<VfxPapGroup>();
            foreach (var group in Groups)
            {
                group.Options ??= new List<Option>();
                group.Id = EnsureUniqueGuid(group.Id, used, ref repaired);
                foreach (var option in group.Options)
                    option.Id = EnsureUniqueGuid(option.Id, used, ref repaired);
            }

            return repaired;
        }

        public static int RepairDuplicateGuids(string modDirectory)
        {
            if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory))
                return 0;

            if (TryLoad(modDirectory, out var meta))
            {
                int repaired = meta!.EnsureUniqueGuids();
                if (repaired > 0)
                    meta.Save();
                return repaired;
            }

            int legacyRepairs = 0;
            var used = new HashSet<Guid>();
            foreach (string groupPath in Directory.GetFiles(modDirectory, "group_*.json")
                         .OrderBy(path => path, NaturalStringComparer.OrdinalIgnoreCase))
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

                if (group == null)
                    continue;

                int fileRepairs = 0;
                group.Options ??= new List<Option>();
                group.Id = EnsureUniqueGuid(group.Id, used, ref fileRepairs);
                foreach (var option in group.Options)
                    option.Id = EnsureUniqueGuid(option.Id, used, ref fileRepairs);

                if (fileRepairs == 0)
                    continue;

                string json = JsonConvert.SerializeObject(group, Formatting.Indented);
                WriteGroupJsonAtomic(groupPath, json);
                legacyRepairs += fileRepairs;
            }

            return legacyRepairs;
        }

        private static string EnsureUniqueGuid(string? value, HashSet<Guid> used, ref int repaired)
        {
            if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out Guid parsed) && used.Add(parsed))
                return value;

            Guid replacement;
            do
            {
                replacement = Guid.NewGuid();
            }
            while (!used.Add(replacement));

            repaired++;
            return replacement.ToString();
        }

        public static Playlist ToPlaylist(VfxPapGroup group)
        {
            return new Playlist
            {
                Id = group.Id,
                Name = group.Name,
                Priority = group.Priority,
                Options = group.Options ?? new List<Option>(),
                FilePath = group.FilePath
            };
        }

        public static void CopyPlaylistToGroup(Playlist playlist, VfxPapGroup group)
        {
            group.Id = EnsureGuid(string.IsNullOrWhiteSpace(group.Id) ? playlist.Id : group.Id);
            playlist.Id = group.Id;
            group.Name = playlist.Name;
            group.Priority = playlist.Priority;
            group.Type = "Single";
            group.Options = playlist.Options ?? new List<Option>();
        }

        private static bool SameDirectory(string left, string right)
        {
            string l = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string r = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeFormat(PenumbraModFormat format) =>
            format == PenumbraModFormat.SingularMeta
                ? "the singular meta.json format"
                : "the legacy group_*.json format";

        private static void WriteJsonAtomic(string fileName, string json)
        {
            if (JsonConvert.DeserializeObject<PenumbraMeta>(json) == null)
                throw new InvalidDataException("Refusing to write invalid meta.json.");

            string directory = Path.GetDirectoryName(fileName)
                ?? throw new InvalidOperationException("meta.json path has no parent directory.");
            string tempPath = Path.Combine(directory, $".meta.{Guid.NewGuid():N}.tmp");
            string backupPath = fileName + ".stage-backup";

            try
            {
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }

                File.Replace(tempPath, fileName, backupPath, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void WriteGroupJsonAtomic(string fileName, string json)
        {
            string modDirectory = Path.GetDirectoryName(fileName)
                ?? throw new InvalidOperationException("Group JSON path has no parent directory.");
            RequireFormat(modDirectory, PenumbraModFormat.LegacyGroupFiles);

            if (JsonConvert.DeserializeObject<VfxPapGroup>(json) == null)
                throw new InvalidDataException("Refusing to write invalid group JSON.");

            string directory = modDirectory;
            string tempPath = Path.Combine(directory, $".{Path.GetFileName(fileName)}.{Guid.NewGuid():N}.tmp");
            string backupPath = fileName + ".stage-backup";

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }

                File.Replace(tempPath, fileName, backupPath, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
}
