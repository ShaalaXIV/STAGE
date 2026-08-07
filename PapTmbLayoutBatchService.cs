using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STAGE
{
    public sealed record PapTmbLayoutBatchResult(
        int PapFilesFound,
        int PapFilesUpdated,
        IReadOnlyList<string> Errors,
        string BackupPath);

    public static class PapTmbLayoutBatchService
    {
        public static PapTmbLayoutBatchResult Apply(
            string modDirectory,
            string donorPapPath)
        {
            modDirectory = Path.GetFullPath(modDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            donorPapPath = Path.GetFullPath(donorPapPath);

            if (!Directory.Exists(modDirectory))
                throw new DirectoryNotFoundException($"The active mod folder was not found:\n{modDirectory}");
            if (!File.Exists(donorPapPath))
                throw new FileNotFoundException(
                    $"The configured donor PAP was not found:\n{donorPapPath}",
                    donorPapPath);

            string[] papFiles = Directory
                .EnumerateFiles(modDirectory, "*.pap", SearchOption.AllDirectories)
                .Where(path => !IsStageWorkingFile(path))
                .Where(path => !IsLegacyBackupPath(modDirectory, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (papFiles.Length == 0)
                throw new InvalidOperationException("The active mod does not contain any PAP files.");

            ModBackupResult backup = ModBackupService.CreateBackup(
                modDirectory,
                "before-donor-tmb-layout");

            var errors = new List<string>();
            int updated = 0;
            foreach (string papPath in papFiles)
            {
                try
                {
                    BeesKneesPapTmbPatcher.PatchImportedPapFromTemplate(
                        papPath,
                        donorPapPath);
                    updated++;
                }
                catch (Exception ex)
                {
                    string relativePath = Path.GetRelativePath(modDirectory, papPath);
                    errors.Add($"{relativePath}: {ex.Message}");
                }
            }

            return new PapTmbLayoutBatchResult(
                papFiles.Length,
                updated,
                errors,
                backup.BackupPath);
        }

        private static bool IsStageWorkingFile(string path) =>
            path.EndsWith(".stage-writing", StringComparison.OrdinalIgnoreCase);

        private static bool IsLegacyBackupPath(string modDirectory, string path)
        {
            string relativePath = Path.GetRelativePath(modDirectory, path);
            return relativePath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.Equals(".stage-backups", StringComparison.OrdinalIgnoreCase));
        }
    }
}
