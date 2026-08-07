using System;
using System.IO;

namespace STAGE
{
    internal static class UserAssetStore
    {
        public static string DirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "STAGE",
            "UserAssets");

        public static string Preserve(
            string sourcePath,
            string storedName,
            string displayName,
            params string[] allowedExtensions)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return "";

            string source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    $"The configured {displayName} file could not be found:\n{source}",
                    source);

            string extension = Path.GetExtension(source);
            if (allowedExtensions.Length > 0 &&
                !Array.Exists(allowedExtensions, allowed =>
                    string.Equals(allowed, extension, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"{storedName} must use one of these file types: " +
                    string.Join(", ", allowedExtensions));
            }

            Directory.CreateDirectory(DirectoryPath);
            string destination = Path.Combine(DirectoryPath, storedName + extension.ToLowerInvariant());
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                return destination;

            string temporary = destination + ".stage-writing";
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }

        public static void MigrateConfiguredAssets()
        {
            string background = Settings.BackgroundImagePath;
            if (File.Exists(background))
            {
                Settings.BackgroundImagePath = Preserve(
                    background,
                    "background",
                    "background image",
                    ".png", ".jpg", ".jpeg", ".bmp", ".gif");
            }

            string donorScd = Settings.DefaultScdTemplateSourcePath;
            if (File.Exists(donorScd))
                Settings.DefaultScdTemplateSourcePath = Preserve(
                    donorScd, "donor", "donor SCD", ".scd");

            string donorPap = Settings.DefaultDonorPapPath;
            if (File.Exists(donorPap))
                Settings.DefaultDonorPapPath = Preserve(
                    donorPap, "donor", "donor PAP", ".pap");
        }
    }
}
