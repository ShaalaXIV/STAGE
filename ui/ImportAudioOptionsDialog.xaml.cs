using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Globalization;

namespace STAGE
{
    public sealed partial class ImportAudioOptionsDialog : ContentDialog
    {
        private bool _initialized;

        public ImportAudioOptionsDialog()
        {
            InitializeComponent();
            foreach (string presetName in EqualizerSettings.PresetNames)
                PresetComboBox.Items.Add(presetName);

            PresetComboBox.SelectedIndex = (int)Settings.EqualizerPreset;
            VolumeSlider.Value = Settings.ScdAudioVolume;
            _initialized = true;
            UpdateVolumeLabel();
        }

        public EqualizerSettings SelectedSettings
        {
            get
            {
                int presetIndex = PresetComboBox.SelectedIndex;
                if (presetIndex < 0 || presetIndex >= EqualizerSettings.PresetNames.Length)
                    presetIndex = (int)EqualizerPreset.Neutral;

                var settings = new EqualizerSettings
                {
                    VolumeLevel = (float)VolumeSlider.Value
                };
                settings.ApplyPreset((EqualizerPreset)presetIndex);
                return settings;
            }
        }

        public void SaveSelectionAsDefault()
        {
            int presetIndex = PresetComboBox.SelectedIndex;
            if (presetIndex >= 0 && presetIndex < EqualizerSettings.PresetNames.Length)
                Settings.EqualizerPreset = (EqualizerPreset)presetIndex;
            Settings.ScdAudioVolume = (float)VolumeSlider.Value;
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_initialized)
                UpdateVolumeLabel();
        }

        private void UpdateVolumeLabel()
        {
            VolumeValueLabel.Text = VolumeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
