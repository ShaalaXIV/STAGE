using Microsoft.Windows.ApplicationModel.Resources;

namespace STAGE
{
    internal static class AppStrings
    {
        private static readonly ResourceLoader _r = new ResourceLoader();
        private static string Get(string key, string fallback = "")
        {
            try
            {
                var value = _r.GetString(key);
                return string.IsNullOrWhiteSpace(value) ? (string.IsNullOrWhiteSpace(fallback) ? key : fallback) : value;
            }
            catch
            {
                return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
            }
        }

        public static string Btn_Yes => Get("Btn_Yes", "Yes");
        public static string Btn_No => Get("Btn_No", "No");
        public static string Btn_Install => Get("Btn_Install", "Install");
        public static string Btn_Later => Get("Btn_Later", "Later");
        public static string Dlg_Error => Get("Dlg_Error", "Error");
        public static string Dlg_NoPlaylist_Title => Get("Dlg_NoPlaylist_Title");
        public static string Dlg_NoPlaylist_Content => Get("Dlg_NoPlaylist_Content");
        public static string Dlg_BPMDetection_Title => Get("Dlg_BPMDetection_Title");
        public static string Dlg_BPMDetection_Content => Get("Dlg_BPMDetection_Content");
        public static string Dlg_ConfirmDelete_Title => Get("Dlg_ConfirmDelete_Title");
        public static string Dlg_ConfirmDelete_Content => Get("Dlg_ConfirmDelete_Content");
        public static string Dlg_NonEmptyPlaylist_Title => Get("Dlg_NonEmptyPlaylist_Title");
        public static string Dlg_NonEmptyPlaylist_Content => Get("Dlg_NonEmptyPlaylist_Content");
        public static string Dlg_UpdateAvailable_Title => Get("Dlg_UpdateAvailable_Title");
        public static string Dlg_ExtractAudio_Title => Get("Dlg_ExtractAudio_Title");
        public static string Dlg_ExtractAudio_NoSongs => Get("Dlg_ExtractAudio_NoSongs");
        public static string Summary_ExtractAudio => Get("Summary_ExtractAudio");
        public static string Dlg_NoSongs => Get("Dlg_NoSongs");
        public static string Dlg_Equalizer_Title => Get("Dlg_Equalizer_Title");
        public static string Dlg_ApplyEQ_Title => Get("Dlg_ApplyEQ_Title");
        public static string Dlg_ScdParameters_Title => Get("Dlg_ScdParameters_Title");
        public static string Dlg_ApplyScdParameters_Title => Get("Dlg_ApplyScdParameters_Title");
        public static string Summary_ApplyEQ => Get("Summary_ApplyEQ");
        public static string Summary_ApplyCurrentVolume => Get("Summary_ApplyCurrentVolume", "Apply current SCD audio volume finished");
        public static string Summary_ApplyScdParameters => Get("Summary_ApplyScdParameters");
        public static string Summary_RepackageScdFromDefault => Get("Summary_RepackageScdFromDefault", "Repackaged with default.scd");
        public static string Prog_ImportingSongs => Get("Prog_ImportingSongs");
        public static string Prog_ComputingDurations => Get("Prog_ComputingDurations");
        public static string Dlg_FileNotFound_Title => Get("Dlg_FileNotFound_Title");
        public static string Dlg_EnterYouTubeUrl => Get("Dlg_EnterYouTubeUrl");
        public static string Prog_PreparingDownload => Get("Prog_PreparingDownload");
        public static string Prog_PostProcessingAudio => Get("Prog_PostProcessingAudio");
        public static string Prog_Done => Get("Prog_Done");
        public static string YT_DefaultPlaylist => Get("YT_DefaultPlaylist");
        public static string Prog_ExtractingAudio => Get("Prog_ExtractingAudio");
        public static string Prog_ApplyingEQSettings => Get("Prog_ApplyingEQSettings");
        public static string Prog_ApplyingCurrentVolume => Get("Prog_ApplyingCurrentVolume", "Applying current SCD audio volume...");
        public static string Prog_ApplyingScdParameters => Get("Prog_ApplyingScdParameters");
        public static string Prog_RepackagingScdFromDefault => Get("Prog_RepackagingScdFromDefault", "Repackaging SCDs from default.scd");
        public static string Menu_ExtractAudio => Get("Menu_ExtractAudio");
        public static string Menu_Rename => Get("Menu_Rename");
        public static string Dlg_OrganizeLibrary_Title => Get("Dlg_OrganizeLibrary_Title");
        public static string Dlg_OrganizeLibrary_Content => Get("Dlg_OrganizeLibrary_Content");
        public static string Menu_ManageEQ => Get("Menu_ManageEQ", "Manage EQ Settings");
        public static string Menu_ApplyCurrentVolume => Get("Menu_ApplyCurrentVolume", "Apply Current SCD Audio Volume");
        public static string Menu_ManageScdParameters => Get("Menu_ManageScdParameters", "Edit SCD Playback Parameters (Volume, Ducking, Attributes, Loop)");
        public static string Menu_RepackageScdFromDefault => Get("Menu_RepackageScdFromDefault", "Repackage Song(s) Using Current default.scd");

        public static string ErrorAddingSongs(string msg) => string.Format(Get("Dlg_ErrorAddingSongs"), msg);
        public static string ErrorDeletion(string msg) => string.Format(Get("Dlg_ErrorDeletion"), msg);
        public static string UpdateAvailableContent(string version) => string.Format(Get("Dlg_UpdateAvailable_Content"), version);
        public static string ApplyEQConfirm(int count) => string.Format(Get("Dlg_ApplyEQConfirm"), count);
        public static string ApplyCurrentVolumeConfirm(int count, float level) => string.Format(
            Get("Dlg_ApplyCurrentVolumeConfirm",
                "Apply the current SCD audio volume setting ({1:0.0}) to {0} song(s)? This rebuilds the embedded audio from its saved baseline."),
            count, level);
        public static string ApplyScdParametersConfirm(int count) => string.Format(Get("Dlg_ApplyScdParametersConfirm", "Apply SCD parameters to {0} song(s)? This will overwrite existing SCD files."), count);
        public static string RepackageScdFromDefaultConfirm(int count) => string.Format(Get("Dlg_RepackageScdFromDefaultConfirm", "Repackage {0} song(s) using the current default.scd settings? This rewrites each SCD while preserving the current audio."), count);
        public static string ApplyingEQ(int current, int total) => string.Format(Get("Prog_ApplyingEQ"), current, total);
        public static string ApplyingCurrentVolume(int current, int total) => string.Format(
            Get("Prog_ApplyingCurrentVolumeOne", "Applying current SCD audio volume ({0}/{1})"),
            current, total);
        public static string ApplyingScdParameters(int current, int total) => string.Format(Get("Prog_ApplyingScdParametersOne", "Applying SCD parameters ({0}/{1})"), current, total);
        public static string RepackagingScdFromDefault(int current, int total) => string.Format(Get("Prog_RepackagingScdFromDefaultOne", "Repackaging SCDs from default.scd ({0}/{1})"), current, total);
        public static string FileNotFoundContent(string path) => string.Format(Get("Dlg_FileNotFound_Content"), path);
        public static string ErrorFileDrop(string msg) => string.Format(Get("Dlg_ErrorFileDrop"), msg);
        public static string ErrorDragDrop(string msg) => string.Format(Get("Dlg_ErrorDragDrop"), msg);
        public static string PostProcessingFile(string file) => string.Format(Get("Prog_PostProcessingFile"), file);
        public static string YTDownloadFailed(string msg) => string.Format(Get("Dlg_YTDownloadFailed"), msg);
        public static string YTAddFailed(string msg) => string.Format(Get("Dlg_YTAddFailed"), msg);
        public static string ErrorLoadingSong(string song, string playlist, string msg) => string.Format(Get("Dlg_ErrorLoadingSong"), song, playlist, msg);
        public static string ErrorLoadingPlaylists(string msg) => string.Format(Get("Dlg_ErrorLoadingPlaylists"), msg);
        public static string Processed(int success, int total) => string.Format(Get("Dlg_Processed"), success, total);
        public static string ProcessedErrors(string errors) => string.Format(Get("Dlg_ProcessedErrors"), errors);
        public static string AndMore(int count) => string.Format(Get("Dlg_AndMore"), count);
    }
}
