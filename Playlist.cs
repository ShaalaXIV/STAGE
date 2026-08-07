using Newtonsoft.Json;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VfxEditor.ScdFormat;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace STAGE
{
    enum SortDirection
    {
        Ascending,
        Descending
    }

    public class Playlist
    {
        public int Version { get { return 0; } }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get { return string.Empty; } }
        public string Image { get { return string.Empty; } }
        public int Page { get { return 0; } }
        public int Priority { get; set; }
        public string Type { get { return "Single"; } }
        public int DefaultSettings { get { return 0; } }
        public List<Option> Options { get; set; } = new List<Option>();

        [JsonIgnore]
        public string FilePath { get; set; } = "";

        public bool ShouldSerializeId() => !string.IsNullOrWhiteSpace(Id);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);


        public static void Create(string playlistName, string dir, Action<int>? callback)
        {
            if (playlistName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Playlist name cannot contain any of the following characters: "
                    + string.Join(" ", Path.GetInvalidFileNameChars()));

            string modDirectory = GetModDirectory();
            Playlist group = new Playlist();
            group.Id = PenumbraMeta.EnsureGuid(null);
            group.Name = playlistName;
            group.Options = new List<Option>();

            Playlist mergedGroup = null;
            var meta = PenumbraMeta.TryLoad(modDirectory, out var loadedMeta) ? loadedMeta : null;
            var existingMetaGroup = meta?.Groups.FirstOrDefault(existing =>
                string.Equals(existing.Name, playlistName, StringComparison.OrdinalIgnoreCase));
            var groupFileNames = meta == null ? GetJsonFiles(playlistName) : Array.Empty<string>();
            string fileName;
            if (existingMetaGroup != null)
            {
                mergedGroup = PenumbraMeta.ToPlaylist(existingMetaGroup);
                fileName = existingMetaGroup.FilePath;
            }
            else if (groupFileNames.Length == 1)
            {
                fileName = groupFileNames[0];
                mergedGroup = JsonConvert.DeserializeObject<Playlist>(File.ReadAllText(fileName));
            }
            else
            {
                Option opt = new Option();
                opt.Name = "Off";
                opt.Files = new Dictionary<string, string>();
                group.Options.Add(opt);
                groupFileNames = Directory.GetFiles(modDirectory, "group_*");
                List<string> groupfiles = new List<string>(groupFileNames);
                groupfiles.Sort(NaturalStringComparer.OrdinalIgnoreCase);
                mergedGroup = group;

                int groupNumber = 1;
                if (groupfiles.Count > 0)
                {
                    string lastName = groupfiles[groupfiles.Count - 1];
                    groupNumber = Int32.Parse(Path.GetFileNameWithoutExtension(lastName).Substring(6, 3)) + 1;
                    mergedGroup.Priority = groupNumber;
                }

                fileName = string.Format("group_{0}_{1}.json", string.Format("{0:D3}", groupNumber), playlistName);
            }

            if (!string.IsNullOrEmpty(dir))
            {
                var supported = new HashSet<string>(
                    Settings.SupportedFileTypes, StringComparer.OrdinalIgnoreCase);
                var fileNames = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(file => supported.Contains(Path.GetExtension(file)))
                    .OrderBy(path => path, NaturalStringComparer.OrdinalIgnoreCase)
                    .ToList();

                int count = 0;
                foreach (string file in fileNames)
                {
                    AddFiles(playlistName, mergedGroup, file);
                    if (callback != null)
                        callback((int)((float)(++count) / Math.Max(1, fileNames.Count) * 100));
                }
            }

            string json = JsonConvert.SerializeObject(mergedGroup, Formatting.Indented);


            if (meta != null)
            {
                var metaGroup = existingMetaGroup ?? new VfxPapGroup();
                PenumbraMeta.CopyPlaylistToGroup(mergedGroup, metaGroup);
                if (existingMetaGroup == null)
                    meta.Groups.Add(metaGroup);
                meta.Save();
            }
            else
            {
                WritePlaylistJsonAtomic(Path.Combine(modDirectory, fileName), json);
            }
            Directory.CreateDirectory(Path.Combine(modDirectory, playlistName));

            // Notify Penumbra (if present) to refresh this mod because files/config changed.
            RefreshPenumbraMod();
        }

        static Option AddFiles(
            string playlistName, Playlist group, string file, EqualizerSettings? audioSettings = null)
        {
            Option opt = null;
            string? preparedAudioPath = null;
            try
            {
                string importPath = file;
                bool isScd = file.EndsWith(".scd", StringComparison.OrdinalIgnoreCase);
                if (!isScd && audioSettings != null)
                {
                    preparedAudioPath = Path.Combine(
                        Path.GetTempPath(), $"stage-import-{Guid.NewGuid():N}.ogg");
                    FFMpeg.PrepareImportAudio(
                        file, preparedAudioPath, audioSettings, Settings.NormalizeVolume);
                    importPath = preparedAudioPath;
                }

                ScdFile scdFile = ScdFile.Import(importPath, processAudio: audioSettings == null);
                string filenameroot = Path.GetFileNameWithoutExtension(file);
                if (filenameroot.Equals("bpmloop", StringComparison.OrdinalIgnoreCase))
                {
                    filenameroot = Path.GetDirectoryName(file);
                    filenameroot = filenameroot.Split(Path.DirectorySeparatorChar).Last();
                }
                string cleanPlaylistName = playlistName.Replace("/", "_");
                string outDir = Path.Combine(GetModDirectory(), cleanPlaylistName);
                Directory.CreateDirectory(outDir);
                using (BinaryWriter writer = new BinaryWriter(new FileStream(Path.Combine(outDir, Path.GetFileName(filenameroot)+".scd"), FileMode.Create)))
                {
                    scdFile.Write(writer);
                }
                opt = new Option();
                opt.Name = filenameroot;
                opt.Files = new Dictionary<string, string>();
                opt.Files.Add(
                    Settings.BaselineScdKey,
                    Path.Combine(cleanPlaylistName, Path.GetFileName(filenameroot)+".scd"));
                group.Options.Add(opt);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error adding file " + file + ": " + ex.Message, ex);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(preparedAudioPath) && File.Exists(preparedAudioPath))
                    File.Delete(preparedAudioPath);
            }
            return opt;
        }

        public void Cleanup()
        {
            Playlist playlist = this;
            string cleanPlaylistName = playlist.Name.Replace("/", "_");
            string modDirectory = GetModDirectory();
            string outDir = Path.Combine(modDirectory, cleanPlaylistName);
            Directory.CreateDirectory(outDir);
            List<Option> optionsToRemove = new List<Option>();
            foreach (Option song in playlist.Options)
            {
                if (song.Name.Equals("Off", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (song.Name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (song.Files != null)
                {
                    string oldPath = Path.Combine(modDirectory, song.Files[song.Files.Keys.First()]);
                    if (!File.Exists(oldPath))
                    {
                        optionsToRemove.Add(song);
                        continue;
                    }
                    if (Path.GetExtension(oldPath) != ".scd")
                    {
                        continue;
                    }

                    // Sanitize the desired filename so it is valid on Windows
                    var safeName = SanitizeFileName(song.Name);
                    if (string.IsNullOrWhiteSpace(safeName))
                        safeName = "audio";

                    // Ensure extension is .scd
                    string fileName = safeName.EndsWith(".scd", StringComparison.OrdinalIgnoreCase) ? safeName : safeName + ".scd";

                    string newPath = Path.Combine(outDir, fileName);

                    if (oldPath != newPath)
                    {
                        // If target file already exists, append a numeric suffix to avoid collision
                        newPath = GetNonCollidingPath(newPath);

                        File.Move(oldPath, newPath);
                        string oldBaselinePath = oldPath + ".stage-audio-baseline";
                        if (File.Exists(oldBaselinePath))
                            File.Move(oldBaselinePath, newPath + ".stage-audio-baseline");
                        BPMDetector.UpdateCacheForSCD(oldPath, newPath);
                        song.Files[song.Files.Keys.First()] = Path.Combine(cleanPlaylistName, Path.GetFileName(newPath));
                    }
                }

                // delete empty folders
                string playlistFolder = Path.Combine(modDirectory, cleanPlaylistName);
                foreach (string subDir in Directory.GetDirectories(playlistFolder))
                {
                    if (Directory.GetFiles(subDir).Length == 0 && Directory.GetDirectories(subDir).Length == 0)
                    {
                        Directory.Delete(subDir);
                    }
                }
            }
            foreach (Option opt in optionsToRemove)
            {
                playlist.Options.Remove(opt);
            }
            this.Save();
            // Save() will refresh Penumbra; no need to call here.
        }

        private static string GetNonCollidingPath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int idx = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name}_{idx}{ext}");
                idx++;
            } while (File.Exists(candidate));
            return candidate;
        }

        public static string? GetScdKey(Option opt)
        {
            if (opt?.Files == null || opt.Files.Count == 0)
                return null;

            if (opt.Files.ContainsKey(Settings.BaselineScdKey))
                return Settings.BaselineScdKey;

            if (opt.Files.ContainsKey("sound/bpmloop.scd"))
                return "sound/bpmloop.scd";

            return opt.Files.Keys.FirstOrDefault(k => k.EndsWith(".scd", StringComparison.OrdinalIgnoreCase));
        }

        public static string GetScdPath(Option opt)
        {
            string? key = GetScdKey(opt);
            if (key == null)
                return string.Empty;
            return opt.Files[key];
        }

        public static string GetFullScdPath(Option opt)
        {
            string scdPath = GetScdPath(opt);
            return string.IsNullOrWhiteSpace(scdPath)
                ? string.Empty
                : GetFullScdPath(scdPath);
        }

        public static string GetFullScdPath(string scdPath)
        {
            if (string.IsNullOrWhiteSpace(scdPath))
                return string.Empty;

            return Path.GetFullPath(Path.Combine(GetAudioModDirectory(), scdPath));
        }

        public static string GetAudioModDirectory() => GetModDirectory();

        public static string GetPlaylistDirectory(string playlistName) =>
            Path.Combine(GetAudioModDirectory(), playlistName);

        public static string GetBaselineScdFileName()
        {
            string key = Settings.BaselineScdKey.Replace('/', Path.DirectorySeparatorChar);
            string fileName = Path.GetFileName(key);
            if (string.IsNullOrWhiteSpace(fileName))
                return "bpmloop.scd";
            return fileName;
        }

        public static Dictionary<string, Playlist> GetAll()
        {
            Dictionary<string, Playlist> playlists = new Dictionary<string, Playlist>();
            string modDirectory = GetModDirectory();
            if (string.IsNullOrWhiteSpace(modDirectory))
                return playlists;

            if (!Directory.Exists(modDirectory))
                return playlists;

            PenumbraMeta.RepairDuplicateGuids(modDirectory);

            var loadedPlaylists = new List<Playlist>();
            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                foreach (var group in meta!.Groups.Where(VfxPapGroup.IsAudioPlaylistGroup))
                    loadedPlaylists.Add(PenumbraMeta.ToPlaylist(group));
            }
            else
            {
                var fileNames = Directory.GetFiles(modDirectory, "group_*.json");
                foreach (string file in fileNames.OrderBy(path => path, NaturalStringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        Playlist playlist = JsonConvert.DeserializeObject<Playlist>(File.ReadAllText(file));

                        if (playlist == null)
                        {
                            Console.Error.WriteLine("Error loading playlist from file " + file);
                        }
                        else
                        {
                            playlist.FilePath = file;
                            loadedPlaylists.Add(playlist);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Error loading playlist from file " + file + ": " + ex);
                    }
                }
            }

            foreach (var playlist in loadedPlaylists
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                playlists[playlist.Name] = playlist;
            }
            return playlists;
        }

        public static void SortAllPlaylistsByName()
        {
            string modDirectory = GetModDirectory();
            if (string.IsNullOrWhiteSpace(modDirectory))
                return;

            if (!Directory.Exists(modDirectory))
                return;

            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                var audioGroups = meta!.Groups
                    .Where(VfxPapGroup.IsAudioPlaylistGroup)
                    .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(group => group.Priority)
                    .ToList();

                for (int i = 0; i < audioGroups.Count; i++)
                    audioGroups[i].Priority = i + 1;

                meta.Save();
                RefreshPenumbraMod();
                return;
            }

            var playlistFiles = Directory.GetFiles(modDirectory, "group_*.json")
                .Select(file => new
                {
                    OriginalPath = file,
                    Playlist = JsonConvert.DeserializeObject<Playlist>(File.ReadAllText(file))
                })
                .Where(item => item.Playlist != null)
                .Select(item => new PlaylistFile(item.OriginalPath, item.Playlist!))
                .OrderBy(item => item.Playlist.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Playlist.Priority)
                .ThenBy(item => item.OriginalPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (playlistFiles.Count == 0)
                return;

            string backupDirectory = Path.Combine(Path.GetTempPath(), "StagePlaylistSort_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupDirectory);

            var targetPaths = playlistFiles
                .Select((item, index) => GetOrderedGroupFilePath(modDirectory, item.Playlist.Name, index + 1))
                .ToList();
            var stagedPaths = new List<string>();

            try
            {
                foreach (var item in playlistFiles)
                {
                    File.Copy(item.OriginalPath, Path.Combine(backupDirectory, Path.GetFileName(item.OriginalPath)), true);
                }

                var originalPaths = new HashSet<string>(playlistFiles.Select(item => item.OriginalPath), StringComparer.OrdinalIgnoreCase);
                foreach (var targetPath in targetPaths)
                {
                    if (File.Exists(targetPath) && !originalPaths.Contains(targetPath))
                        throw new IOException($"Cannot sort playlists because target file already exists: {targetPath}");
                }

                for (int i = 0; i < playlistFiles.Count; i++)
                {
                    var item = playlistFiles[i];
                    item.Playlist.Priority = i + 1;
                    string json = JsonConvert.SerializeObject(item.Playlist, Formatting.Indented);
                    string stagedPath = Path.Combine(
                        modDirectory, $".stage-sort-{Guid.NewGuid():N}.tmp");
                    WritePlaylistJsonAtomic(stagedPath, json);
                    stagedPaths.Add(stagedPath);
                }

                PenumbraMeta.RequireFormat(modDirectory, PenumbraModFormat.LegacyGroupFiles);
                for (int i = 0; i < targetPaths.Count; i++)
                {
                    if (File.Exists(targetPaths[i]))
                        File.Replace(stagedPaths[i], targetPaths[i], null, true);
                    else
                        File.Move(stagedPaths[i], targetPaths[i]);
                }

                var targetSet = new HashSet<string>(targetPaths, StringComparer.OrdinalIgnoreCase);
                foreach (var originalPath in originalPaths)
                {
                    if (!targetSet.Contains(originalPath) && File.Exists(originalPath))
                        File.Delete(originalPath);
                }

                RefreshPenumbraMod();
            }
            catch
            {
                bool canRestoreLegacyFiles = false;
                try
                {
                    canRestoreLegacyFiles =
                        PenumbraMeta.DetectFormat(modDirectory) == PenumbraModFormat.LegacyGroupFiles;
                }
                catch
                {
                    // An invalid or changing meta.json is never safe to overwrite.
                }

                if (canRestoreLegacyFiles)
                {
                    foreach (var currentFile in Directory.GetFiles(modDirectory, "group_*.json"))
                        File.Delete(currentFile);

                    foreach (var backupFile in Directory.GetFiles(backupDirectory))
                        File.Copy(backupFile, Path.Combine(modDirectory, Path.GetFileName(backupFile)), true);
                }

                throw;
            }
            finally
            {
                try
                {
                    foreach (var stagedPath in stagedPaths)
                    {
                        if (File.Exists(stagedPath))
                            File.Delete(stagedPath);
                    }
                    if (Directory.Exists(backupDirectory))
                        Directory.Delete(backupDirectory, true);
                }
                catch
                {
                    // best-effort cleanup only
                }
            }
        }

        private static string GetOrderedGroupFilePath(string modDirectory, string playlistName, int order)
        {
            return Path.Combine(modDirectory, $"group_{order:D3}_{playlistName.Replace("/", "_")}.json");
        }

        private sealed record PlaylistFile(string OriginalPath, Playlist Playlist);

        public void Add(
            string[] fileNames, Action<int>? callback = null, EqualizerSettings? audioSettings = null)
        {
            int count = 0;
            foreach (string file in fileNames.OrderBy(path => path, NaturalStringComparer.OrdinalIgnoreCase))
            {
                if (Settings.SupportedFileTypes.Contains(Path.GetExtension(file).ToLower()))
                    AddFiles(Name, this, file, audioSettings);
                if (callback != null)
                    callback((int)((float)(++count)/fileNames.Length*100));
            }
            Save();
        }

        public void Insert(
            string[] fileNames, int index, Action<int>? callback = null,
            EqualizerSettings? audioSettings = null)
        {
            int count = 0;
            foreach (string file in fileNames)
            {
                Option opt = AddFiles(Name, this, file, audioSettings);
                Options.RemoveAt(Options.Count - 1);
                Options.Insert(index, opt);
                index++;
                if (callback != null)
                    callback((int)((float)(++count) / fileNames.Length * 100));
            }
            Save();
        }


        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        public bool Rename(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return false;

            newName = newName.Trim();
            if (!IsValidName(newName))
                throw new ArgumentException("Playlist name cannot contain any invalid filename characters.");

            string oldName = Name;
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
                return false;

            if (GetJsonFiles(newName).Length > 0)
                throw new InvalidOperationException($"A playlist named '{newName}' already exists.");

            string modDirectory = GetModDirectory();
            bool metaMode = PenumbraMeta.TryLoad(modDirectory, out var meta);
            VfxPapGroup? metaGroup = null;
            string? oldJsonPath = null;
            if (metaMode)
            {
                metaGroup = meta!.Groups.FirstOrDefault(group =>
                    string.Equals(group.Id, Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(group.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (metaGroup == null)
                    throw new FileNotFoundException("Playlist group was not found in meta.json.");
            }
            else
            {
                oldJsonPath = GetJsonFiles(oldName).FirstOrDefault();
                if (string.IsNullOrEmpty(oldJsonPath) || !File.Exists(oldJsonPath))
                    throw new FileNotFoundException("Playlist JSON file not found.", oldJsonPath);
            }

            string oldFolder = Path.Combine(modDirectory, oldName);
            string newFolder = Path.Combine(modDirectory, newName);
            PenumbraMeta.RequireFormat(
                modDirectory,
                metaMode ? PenumbraModFormat.SingularMeta : PenumbraModFormat.LegacyGroupFiles);

            if (Directory.Exists(oldFolder) && !string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(newFolder))
                    throw new InvalidOperationException($"A playlist folder named '{newName}' already exists.");
                Directory.Move(oldFolder, newFolder);
            }

            if (!metaMode && oldJsonPath != null)
            {
                string newJsonPath = Path.Combine(modDirectory, Path.GetFileName(oldJsonPath).Replace(oldName.Replace("/", "_"), newName.Replace("/", "_")));
                if (!string.Equals(oldJsonPath, newJsonPath, StringComparison.OrdinalIgnoreCase))
                    File.Move(oldJsonPath, newJsonPath);
            }

            Name = newName;

            if (Options != null)
            {
                foreach (var song in Options)
                {
                    if (song?.Files == null) continue;
                    var keys = song.Files.Keys.ToList();
                    foreach (var key in keys)
                    {
                        var rel = song.Files[key];
                        if (string.IsNullOrWhiteSpace(rel)) continue;
                        var updated = rel.Replace(oldName.Replace("/", "_") + Path.DirectorySeparatorChar, newName.Replace("/", "_") + Path.DirectorySeparatorChar)
                                         .Replace(oldName.Replace("/", "_") + '/', newName.Replace("/", "_") + '/');
                        song.Files[key] = updated;
                    }
                }
            }

            if (metaMode && metaGroup != null && meta != null)
            {
                PenumbraMeta.CopyPlaylistToGroup(this, metaGroup);
                meta.Save();
                RefreshPenumbraMod();
            }
            else
            {
                Save();
            }
            return true;
        }

        public void Save()
        {
            string modDirectory = GetModDirectory();
            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                var group = meta!.Groups.FirstOrDefault(candidate =>
                    (!string.IsNullOrWhiteSpace(Id) && string.Equals(candidate.Id, Id, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(candidate.Name, Name, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                    return;

                PenumbraMeta.CopyPlaylistToGroup(this, group);
                meta.Save();
                RefreshPenumbraMod();
                return;
            }

            var fileNames = GetJsonFiles(Name);
            if (fileNames.Length == 0) return;
            string fileName = fileNames[0];
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            WritePlaylistJsonAtomic(fileName, json);

            // Notify Penumbra (if present) that the mod directory changed so it can refresh.
            RefreshPenumbraMod();
        }

        private static void WritePlaylistJsonAtomic(string fileName, string json)
        {
            PenumbraMeta.RequireFormat(
                Path.GetDirectoryName(fileName) ?? "",
                PenumbraModFormat.LegacyGroupFiles);
            string directory = Path.GetDirectoryName(fileName)
                ?? throw new InvalidOperationException("Playlist JSON path has no parent directory.");
            var validated = JsonConvert.DeserializeObject<Playlist>(json);
            if (validated == null || string.IsNullOrWhiteSpace(validated.Name) || validated.Options == null)
                throw new InvalidDataException("Refusing to write an invalid playlist JSON file.");

            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(
                directory, $".{Path.GetFileName(fileName)}.{Guid.NewGuid():N}.tmp");
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

                if (File.Exists(fileName))
                    File.Replace(tempPath, fileName, backupPath, true);
                else
                    File.Move(tempPath, fileName);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public void Delete()
        {
            if (string.IsNullOrEmpty(Name))
                return;

            string modDirectory = GetModDirectory();
            if (PenumbraMeta.TryLoad(modDirectory, out var meta))
            {
                int removed = meta!.Groups.RemoveAll(group =>
                    (!string.IsNullOrWhiteSpace(Id) && string.Equals(group.Id, Id, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(group.Name, Name, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                    meta.Save();
            }
            else if (GetJsonFiles(Name).Length > 0)
            {
                PenumbraMeta.RequireFormat(modDirectory, PenumbraModFormat.LegacyGroupFiles);
                File.Delete(GetJsonFiles(Name)[0]);
            }

            PenumbraMeta.RequireFormat(
                modDirectory,
                meta != null ? PenumbraModFormat.SingularMeta : PenumbraModFormat.LegacyGroupFiles);
            if (Directory.Exists(Path.Combine(modDirectory, Name)))
                Directory.Delete(Path.Combine(modDirectory, Name), true);
            // Notify Penumbra after removing files
            RefreshPenumbraMod();
        }

        private static string[] GetJsonFiles(string name)
        {
            string modDirectory = GetModDirectory();
            if (string.IsNullOrWhiteSpace(modDirectory))
                return Array.Empty<string>();

            if (!Directory.Exists(modDirectory))
                return Array.Empty<string>();

            return Directory.GetFiles(modDirectory, "group_*_" + name.Replace("/","_") + ".json");
        }

        internal void Shuffle()
        {
            var jsonFiles = GetJsonFiles(Name);
            string? backupPath = null;
            if (jsonFiles.Length > 0 && File.Exists(jsonFiles[0]))
            {
                backupPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(jsonFiles[0]));
                File.Copy(jsonFiles[0], backupPath, true);
            }
            try
            {
                int n = Options.Count;
                List<Option> shuffledOptions = new List<Option>(n);
                shuffledOptions.Add(Options[0]); // Keep the "Off" option in place
                Options.RemoveAt(0);
                Random rng = new Random();
                while (Options.Count > 0)
                {
                    int k = rng.Next(Options.Count);
                    shuffledOptions.Add(Options[k]);
                    Options.RemoveAt(k);
                }
                Options = shuffledOptions;
                Save();
            }
            catch (Exception ex)
            {
                if (backupPath != null && File.Exists(backupPath) && jsonFiles.Length > 0)
                    File.Copy(backupPath, jsonFiles[0], true);
                MessageBoxW(IntPtr.Zero, ex.ToString(), "Shuffle Error", 0x00000010); // MB_OK | MB_ICONERROR
                throw;
            }
        }

        internal void Sort(SortDirection direction)
        {
            var jsonFiles = GetJsonFiles(Name);
            string? backupPath = null;
            if (jsonFiles.Length > 0 && File.Exists(jsonFiles[0]))
            {
                backupPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(jsonFiles[0]));
                File.Copy(jsonFiles[0], backupPath, true);
            }
            try
            {
                Option offOption = Options.FirstOrDefault(o => o.Name.Equals("Off", StringComparison.OrdinalIgnoreCase));
                List<Option> otherOptions = Options.Where(o => !o.Name.Equals("Off", StringComparison.OrdinalIgnoreCase)).ToList();
                if (direction == SortDirection.Ascending)
                    otherOptions = otherOptions.OrderBy(o => BPMDetector.GetBPMFromSCD(GetScdPath(o))).ToList();
                else
                    otherOptions = otherOptions.OrderByDescending(o => BPMDetector.GetBPMFromSCD(GetScdPath(o))).ToList();
                Options = new List<Option>();
                if (offOption != null)
                    Options.Add(offOption);
                Options.AddRange(otherOptions);
                Save();
            }
            catch (Exception ex)
            {
                if (backupPath != null && File.Exists(backupPath) && jsonFiles.Length > 0)
                    File.Copy(backupPath, jsonFiles[0], true);
                MessageBoxW(IntPtr.Zero, ex.ToString(), "Sort Error", 0x00000010); // MB_OK | MB_ICONERROR
                throw;
            }
        }

        internal void SortByName()
        {
            var jsonFiles = GetJsonFiles(Name);
            string? backupPath = null;
            if (jsonFiles.Length > 0 && File.Exists(jsonFiles[0]))
            {
                backupPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(jsonFiles[0]));
                File.Copy(jsonFiles[0], backupPath, true);
            }
            try
            {
                Option offOption = Options.FirstOrDefault(o => o.Name.Equals("Off", StringComparison.OrdinalIgnoreCase));
                List<Option> otherOptions = Options.Where(o => !o.Name.Equals("Off", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(o => o.Name, NaturalStringComparer.OrdinalIgnoreCase).ToList();
                Options = new List<Option>();
                if (offOption != null) Options.Add(offOption);
                Options.AddRange(otherOptions);
                Save();
            }
            catch (Exception ex)
            {
                if (backupPath != null && File.Exists(backupPath) && jsonFiles.Length > 0)
                    File.Copy(backupPath, jsonFiles[0], true);
                MessageBoxW(IntPtr.Zero, ex.ToString(), "Sort Error", 0x00000010); // MB_OK | MB_ICONERROR
                throw;
            }
        }

        /// <summary>
        /// Replace invalid characters for Windows filenames, trim trailing dots/spaces and limit length.
        /// </summary>
        private static string SanitizeFileName(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Replace directory separators and invalid filename characters with underscore
            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (invalid.Contains(ch) || char.IsControl(ch))
                    sb.Append('_');
                else
                    sb.Append(ch);
            }

            var result = sb.ToString();

            // Trim trailing spaces and dots (Windows does not allow names that end with dot/space)
            result = result.TrimEnd(' ', '.');

            // Limit filename length (reserve space for extension .scd)
            const int maxFileName = 200;
            if (result.Length > maxFileName)
                result = result.Substring(0, maxFileName);

            // As an extra safeguard remove any remaining invalid subsequences
            result = Regex.Replace(result, @"[\\\/:\*\?""<>\|]", "_");

            return result;
        }

        /// <summary>
        /// Attempt to notify Penumbra (FFXIV Dalamud plugin) that the mod folder changed so Penumbra can refresh its cache.
        /// This is a best-effort notification: Penumbra watches file changes; touching meta.json or creating a transient marker file
        /// commonly triggers a refresh. This function does not interact with Dalamud directly.
        /// </summary>
        private static void RefreshPenumbraMod()
        {
            try
            {
                if (!Settings.AutoReloadMod)
                    return;

                string modDirectory = GetModDirectory();
                if (string.IsNullOrWhiteSpace(modDirectory))
                    return;

                PenumbraApi.ScheduleReloadModFolder(modDirectory);
            }
            catch
            {
                // best-effort only; swallow any errors to avoid breaking the UI
            }
        }

        private static string GetModDirectory()
        {
            string modDirectory = Settings.AudioModFolder;
            if (!string.IsNullOrWhiteSpace(modDirectory))
                return modDirectory;

            if (!string.IsNullOrWhiteSpace(Settings.PenumbraLocation) &&
                !string.IsNullOrWhiteSpace(Settings.ModName))
            {
                return Path.Combine(Settings.PenumbraLocation, Settings.ModName);
            }

            return string.Empty;
        }
    }
}
