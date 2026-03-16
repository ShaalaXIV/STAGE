using Microsoft.UI.Xaml.Controls;

namespace Pickles_Playlist_Editor
{
    public sealed partial class ScdParametersDialog : ContentDialog
    {
        public ScdParameterSettings SelectedSettings { get; }

        public ScdParametersDialog(ScdParameterSettings initial)
        {
            this.InitializeComponent();
            SelectedSettings = initial;

            SoundVolumeBox.Value = initial.SoundVolume;
            BusDuckingNumberBox.Value = initial.BusDuckingNumber;
            BusDuckingFadeTimeBox.Value = initial.BusDuckingFadeTime;
            BusDuckingVolumeBox.Value = initial.BusDuckingVolume;
            AttributeVersionBox.Value = initial.AttributeVersion;
            AttributeConditionFirstBox.Value = initial.AttributeConditionFirst;
            LoopEnabledCheckBox.IsChecked = initial.LoopEnabled;

            PrimaryButtonClick += OnPrimaryButtonClick;
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            SelectedSettings.SoundVolume = (float)SoundVolumeBox.Value;
            SelectedSettings.BusDuckingNumber = (int)BusDuckingNumberBox.Value;
            SelectedSettings.BusDuckingFadeTime = (int)BusDuckingFadeTimeBox.Value;
            SelectedSettings.BusDuckingVolume = (float)BusDuckingVolumeBox.Value;
            SelectedSettings.AttributeVersion = (int)AttributeVersionBox.Value;
            SelectedSettings.AttributeConditionFirst = (int)AttributeConditionFirstBox.Value;
            SelectedSettings.LoopEnabled = LoopEnabledCheckBox.IsChecked == true;
        }
    }
}
