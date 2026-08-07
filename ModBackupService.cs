using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using STAGE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace STAGE
{
    public sealed record ModBackupResult(string BackupPath, int FileCount, long TotalBytes);

    public sealed record ModRestoreResult(
        string RestoredFrom,
        string SafetyBackupPath,
        int FileCount);

    public sealed record BackupRetentionResult(
        int AutomaticBackupsDeleted,
        long BytesFreed);

    public static class ModBackupService
    {
        private const string ManifestName = "stage-backup.json";
        private const int BackupFormatVersion = 1;

        public static ModBackupResult CreateBackup(
            string modDirectory,
            string reason = "manual")
        {
            modDirectory = ValidateModDirectory(modDirectory);
            _ = PenumbraMeta.DetectFormat(modDirectory);

            string backupRoot = GetBackupRoot(modDirectory);
            Directory.CreateDirectory(backupRoot);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string safeReason = SanitizeName(reason);
            string backupPath = UniquePath(Path.Combine(
                backupRoot,
                $"{stamp}-{safeReason}.stage-backup.zip"));
            string temporaryPath = backupPath + ".stage-writing";

            int fileCount = 0;
            long totalBytes = 0;
            try
            {
                using var stream = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                foreach (string filePath in Directory.EnumerateFiles(
                             modDirectory, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(modDirectory, filePath);
                    if (ShouldExclude(relativePath))
                        continue;

                    var info = new FileInfo(filePath);
                    var entry = archive.CreateEntry(
                        ToArchivePath(relativePath),
                        CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(info.LastWriteTime);
                    using Stream input = File.OpenRead(filePath);
                    using Stream output = entry.Open();
                    input.CopyTo(output);
                    fileCount++;
                    totalBytes += info.Length;
                }

                var manifestEntry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using var manifestWriter = new StreamWriter(manifestEntry.Open());
                manifestWriter.Write(JsonConvert.SerializeObject(new
                {
                    FormatVersion = BackupFormatVersion,
                    CreatedAt = DateTime.Now,
                    ModName = Path.GetFileName(modDirectory),
                    SourceDirectory = modDirectory,
                    Reason = reason,
                    FileCount = fileCount,
                    TotalBytes = totalBytes
                }, Formatting.Indented));
            }
            catch
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
                throw;
            }

            File.Move(temporaryPath, backupPath);
            _ = CleanupExpiredAutomaticBackups(
                Settings.ManagedModFolders.Append(modDirectory),
                Settings.AutoBackupRetentionDays,
                backupPath);
            return new ModBackupResult(backupPath, fileCount, totalBytes);
        }

        public static BackupRetentionResult CleanupExpiredAutomaticBackups(
            IEnumerable<string> managedModDirectories,
            int retentionDays,
            string? protectedBackupPath = null)
        {
            retentionDays = Math.Clamp(retentionDays, 1, 3650);
            DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
            int deleted = 0;
            long bytesFreed = 0;

            string backupRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "STAGE",
                "Backups");
            if (Directory.Exists(backupRoot))
            {
                foreach (string backupPath in Directory.EnumerateFiles(
                             backupRoot,
                             "*.stage-backup.zip",
                             SearchOption.AllDirectories))
                {
                    if (!string.IsNullOrWhiteSpace(protectedBackupPath) &&
                        string.Equals(
                            Path.GetFullPath(backupPath),
                            Path.GetFullPath(protectedBackupPath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!TryReadBackupIdentity(
                            backupPath,
                            out string reason,
                            out DateTime createdAt) ||
                        string.Equals(reason, "manual", StringComparison.OrdinalIgnoreCase) ||
                        createdAt >= cutoff)
                    {
                        continue;
                    }

                    long length = new FileInfo(backupPath).Length;
                    File.Delete(backupPath);
                    deleted++;
                    bytesFreed += length;
                }
            }

            foreach (string modDirectory in managedModDirectories
                         .Where(Directory.Exists)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string legacyRoot = Path.Combine(modDirectory, ".stage-backups");
                if (!Directory.Exists(legacyRoot))
                    continue;

                foreach (string directory in Directory.EnumerateDirectories(
                             legacyRoot,
                             "filename-sync-*",
                             SearchOption.TopDirectoryOnly))
                {
                    string manifestPath = Path.Combine(directory, "manifest.json");
                    if (!File.Exists(manifestPath) ||
                        !TryReadFilenameSyncCreatedAt(manifestPath, out DateTime createdAt) ||
                        createdAt >= cutoff)
                    {
                        continue;
                    }

                    long length = Directory.EnumerateFiles(
                            directory, "*", SearchOption.AllDirectories)
                        .Sum(path => new FileInfo(path).Length);
                    Directory.Delete(directory, recursive: true);
                    deleted++;
                    bytesFreed += length;
                }
            }

            return new BackupRetentionResult(deleted, bytesFreed);
        }

        public static ModRestoreResult RestoreBackup(
            string modDirectory,
            string backupPath)
        {
            modDirectory = ValidateModDirectory(modDirectory);
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                throw new FileNotFoundException("The selected STAGE backup does not exist.", backupPath);

            var entries = ValidateArchive(backupPath);
            var safetyBackup = CreateBackup(modDirectory, "before-restore");
            string parent = Path.GetDirectoryName(modDirectory)
                ?? throw new InvalidOperationException("The mod folder has no parent directory.");
            string token = Guid.NewGuid().ToString("N");
            string extractionDirectory = Path.Combine(parent, $".stage-restore-{token}");
            string displacedDirectory = Path.Combine(parent, $".stage-displaced-{token}");
            Directory.CreateDirectory(extractionDirectory);
            Directory.CreateDirectory(displacedDirectory);

            try
            {
                using (var archive = ZipFile.OpenRead(backupPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) ||
                            string.Equals(entry.FullName, ManifestName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string destination = ResolveInsideRoot(
                            extractionDirectory,
                            entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        entry.ExtractToFile(destination, overwrite: false);
                        File.SetLastWriteTime(destination, entry.LastWriteTime.LocalDateTime);
                    }
                }

                _ = PenumbraMeta.DetectFormat(extractionDirectory);

                try
                {
                    MoveFilesPreservingStructure(
                        modDirectory,
                        displacedDirectory,
                        excludeStageBackups: true);
                    RemoveEmptyDirectories(modDirectory);
                }
                catch
                {
                    MoveFilesPreservingStructure(
                        displacedDirectory,
                        modDirectory,
                        excludeStageBackups: false);
                    throw;
                }

                try
                {
                    MoveFilesPreservingStructure(
                        extractionDirectory,
                        modDirectory,
                        excludeStageBackups: false);
                }
                catch
                {
                    RemoveRestoredEntries(modDirectory);
                    MoveFilesPreservingStructure(
                        displacedDirectory,
                        modDirectory,
                        excludeStageBackups: false);
                    throw;
                }

                Directory.Delete(extractionDirectory, recursive: true);
                Directory.Delete(displacedDirectory, recursive: true);
                PenumbraApi.ScheduleReloadModFolder(modDirectory);
                return new ModRestoreResult(backupPath, safetyBackup.BackupPath, entries);
            }
            catch
            {
                if (Directory.Exists(extractionDirectory))
                    Directory.Delete(extractionDirectory, recursive: true);
                if (Directory.Exists(displacedDirectory) &&
                    !Directory.EnumerateFileSystemEntries(displacedDirectory).Any())
                {
                    Directory.Delete(displacedDirectory);
                }
                throw;
            }
        }

        public static string GetBackupRoot(string modDirectory)
        {
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string modName = SanitizeName(Path.GetFileName(
                Path.GetFullPath(modDirectory).TrimEnd('\\', '/')));
            return Path.Combine(localAppData, "STAGE", "Backups", modName);
        }

        private static int ValidateArchive(string backupPath)
        {
            using var archive = ZipFile.OpenRead(backupPath);
            var manifest = archive.GetEntry(ManifestName)
                ?? throw new InvalidDataException("This is not a STAGE mod backup.");
            using (var reader = new StreamReader(manifest.Open()))
            {
                JObject? data = JsonConvert.DeserializeObject<JObject>(reader.ReadToEnd());
                if (data == null ||
                    data.Value<int?>("FormatVersion") != BackupFormatVersion)
                    throw new InvalidDataException("This STAGE backup version is not supported.");
            }

            bool hasMeta = false;
            int fileCount = 0;
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith("/", StringComparison.Ordinal) ||
                    name.Contains("../", StringComparison.Ordinal) ||
                    Path.IsPathRooted(name))
                {
                    throw new InvalidDataException($"The backup contains an unsafe path: {name}");
                }
                if (!destinations.Add(name))
                    throw new InvalidDataException($"The backup contains a duplicate path: {name}");
                if (string.Equals(name, "meta.json", StringComparison.OrdinalIgnoreCase))
                    hasMeta = true;
                if (!string.IsNullOrEmpty(entry.Name) &&
                    !string.Equals(name, ManifestName, StringComparison.OrdinalIgnoreCase))
                {
                    fileCount++;
                }
            }

            if (!hasMeta)
                throw new InvalidDataException("The backup does not contain a root meta.json.");
            return fileCount;
        }

        private static bool TryReadBackupIdentity(
            string backupPath,
            out string reason,
            out DateTime createdAt)
        {
            reason = "";
            createdAt = default;
            try
            {
                using var archive = ZipFile.OpenRead(backupPath);
                var entry = archive.GetEntry(ManifestName);
                if (entry == null)
                    return false;
                using var reader = new StreamReader(entry.Open());
                JObject? data = JsonConvert.DeserializeObject<JObject>(reader.ReadToEnd());
                reason = data?.Value<string>("Reason") ?? "";
                DateTime? timestamp = data?.Value<DateTime?>("CreatedAt");
                if (string.IsNullOrWhiteSpace(reason) || timestamp == null)
                    return false;
                createdAt = timestamp.Value;
                return true;
            }
            catch
            {
                // Unknown or damaged backups are retained rather than guessed to be automatic.
                return false;
            }
        }

        private static bool TryReadFilenameSyncCreatedAt(
            string manifestPath,
            out DateTime createdAt)
        {
            createdAt = default;
            try
            {
                JObject? data = JsonConvert.DeserializeObject<JObject>(
                    File.ReadAllText(manifestPath));
                DateTime? timestamp = data?.Value<DateTime?>("CreatedAt");
                if (timestamp == null)
                    return false;
                createdAt = timestamp.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ValidateModDirectory(string modDirectory)
        {
            if (string.IsNullOrWhiteSpace(modDirectory))
                throw new InvalidOperationException("Select an active mod folder first.");
            string fullPath = Path.GetFullPath(modDirectory).TrimEnd('\\', '/');
            if (!Directory.Exists(fullPath) ||
                !File.Exists(Path.Combine(fullPath, "meta.json")))
            {
                throw new DirectoryNotFoundException(
                    "The selected folder is not a valid Penumbra mod directory.");
            }
            return fullPath;
        }

        private static bool ShouldExclude(string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/');
            return normalized.StartsWith(".stage-backups/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith(".stage-writing", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("/.stage-writing", StringComparison.OrdinalIgnoreCase);
        }

        private static void RemoveRestoredEntries(string modDirectory)
        {
            foreach (string path in Directory.EnumerateFiles(
                         modDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(modDirectory, path);
                if (ShouldExclude(relativePath))
                    continue;

                MakeFileWritable(path);
                File.Delete(path);
            }
            RemoveEmptyDirectories(modDirectory);
        }

        private static void MoveFilesPreservingStructure(
            string sourceRoot,
            string destinationRoot,
            bool excludeStageBackups)
        {
            foreach (string sourcePath in Directory.EnumerateFiles(
                         sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
                if (excludeStageBackups && ShouldExclude(relativePath))
                    continue;

                string destinationPath = ResolveInsideRoot(destinationRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                if (File.Exists(destinationPath))
                {
                    MakeFileWritable(destinationPath);
                    File.Delete(destinationPath);
                }
                MakeFileWritable(sourcePath);
                File.Move(sourcePath, destinationPath);
            }
        }

        private static void RemoveEmptyDirectories(string root)
        {
            foreach (string directory in Directory
                         .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                string relativePath = Path.GetRelativePath(root, directory)
                    .Replace('\\', '/');
                if (relativePath.Equals(
                        ".stage-backups",
                        StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith(
                        ".stage-backups/",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch (UnauthorizedAccessException)
                {
                    // A watcher may hold the directory itself. Empty directories do
                    // not affect the restored mod and can safely remain in place.
                }
                catch (IOException)
                {
                    // Same as above for transient directory handles.
                }
            }
        }

        private static void MakeFileWritable(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        private static string ResolveInsideRoot(string root, string relativePath)
        {
            string safeRoot = Path.GetFullPath(root).TrimEnd('\\', '/') +
                              Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Backup path escapes the mod directory: {relativePath}");
            return fullPath;
        }

        private static string ToArchivePath(string path) =>
            path.Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

        private static string SanitizeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string result = new string((value ?? "")
                .Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray())
                .Trim()
                .TrimEnd('.');
            return string.IsNullOrWhiteSpace(result) ? "mod" : result;
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path))
                return path;
            string directory = Path.GetDirectoryName(path)!;
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int index = 2; ; index++)
            {
                string candidate = Path.Combine(directory, $"{name}_{index}{extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }
    }
}
