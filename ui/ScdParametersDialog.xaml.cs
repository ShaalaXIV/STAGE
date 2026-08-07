using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace STAGE
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
            TrackLoopEnabledCheckBox.IsChecked = initial.TrackLoopEnabled;

            LayoutMinRangeBox.Value = initial.LayoutMinRange;
            LayoutMaxRangeBox.Value = initial.LayoutMaxRange;
            LayoutHeightLowBox.Value = initial.LayoutHeightLow;
            LayoutHeightHighBox.Value = initial.LayoutHeightHigh;
            LayoutRangeVolumeBox.Value = initial.LayoutRangeVolume;

            SetLayoutControlsEnabled(initial.LayoutParametersAvailable);

            PrimaryButtonClick += OnPrimaryButtonClick;
        }

        private void SetLayoutControlsEnabled(bool enabled)
        {
            var visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            LayoutUnavailableText.Visibility = visibility;

            LayoutMinRangeBox.IsEnabled = enabled;
            LayoutMaxRangeBox.IsEnabled = enabled;
            LayoutHeightLowBox.IsEnabled = enabled;
            LayoutHeightHighBox.IsEnabled = enabled;
            LayoutRangeVolumeBox.IsEnabled = enabled;
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
            SelectedSettings.TrackLoopEnabled = TrackLoopEnabledCheckBox.IsChecked == true;

            if (SelectedSettings.LayoutParametersAvailable)
            {
                SelectedSettings.LayoutMinRange = (float)LayoutMinRangeBox.Value;
                SelectedSettings.LayoutMaxRange = (float)LayoutMaxRangeBox.Value;
                SelectedSettings.LayoutHeightLow = (float)LayoutHeightLowBox.Value;
                SelectedSettings.LayoutHeightHigh = (float)LayoutHeightHighBox.Value;
                SelectedSettings.LayoutRangeVolume = (float)LayoutRangeVolumeBox.Value;
            }
        }
    }
}
