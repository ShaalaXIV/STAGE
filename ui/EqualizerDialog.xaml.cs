using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Globalization;
using System.Linq;

namespace STAGE
{
    public sealed partial class EqualizerDialog : ContentDialog
    {
        public EqualizerSettings SelectedSettings { get; } = new EqualizerSettings();
        private bool _applyingPreset;
        private bool _initialized;

        public EqualizerDialog()
        {
            InitializeComponent();
            foreach (string presetName in EqualizerSettings.PresetNames.Append("Custom"))
                PresetComboBox.Items.Add(presetName);

            VolumeSlider.Value = Settings.ScdAudioVolume;
            _initialized = true;
            PresetComboBox.SelectedIndex = (int)Settings.EqualizerPreset;

            if (PresetComboBox.SelectedIndex < 0)
            {
                PresetComboBox.SelectedIndex = (int)EqualizerPreset.Neutral;
            }
        }

        private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_initialized)
                return;

            UpdateSettingsFromSliders();
            if (!_applyingPreset && PresetComboBox != null)
                PresetComboBox.SelectedIndex = EqualizerSettings.PresetNames.Length;
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized)
                return;

            int index = PresetComboBox.SelectedIndex;
            if (index < 0 || index >= EqualizerSettings.PresetNames.Length)
                return;

            _applyingPreset = true;
            SelectedSettings.ApplyPreset((EqualizerPreset)index);
            var gains = SelectedSettings.GetGains();
            BassSlider.Value = gains[0] * 10;
            LowMidSlider.Value = gains[1] * 10;
            MidSlider.Value = gains[2] * 10;
            HighMidSlider.Value = gains[3] * 10;
            TrebleSlider.Value = gains[4] * 10;
            _applyingPreset = false;
            UpdateSettingsFromSliders();
        }

        private void UpdateSettingsFromSliders()
        {
            SelectedSettings.BassGain = (float)(BassSlider.Value / 10.0);
            SelectedSettings.LowMidGain = (float)(LowMidSlider.Value / 10.0);
            SelectedSettings.MidGain = (float)(MidSlider.Value / 10.0);
            SelectedSettings.HighMidGain = (float)(HighMidSlider.Value / 10.0);
            SelectedSettings.TrebleGain = (float)(TrebleSlider.Value / 10.0);
            SelectedSettings.VolumeLevel = (float)VolumeSlider.Value;

            BassLabel.Text = FormatGain(BassSlider.Value);
            LowMidLabel.Text = FormatGain(LowMidSlider.Value);
            MidLabel.Text = FormatGain(MidSlider.Value);
            HighMidLabel.Text = FormatGain(HighMidSlider.Value);
            TrebleLabel.Text = FormatGain(TrebleSlider.Value);
            VolumeLabel.Text = VolumeSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);

            Player.ApplyRealtimeEqualizer(SelectedSettings);
        }

        private void EqualizerDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            if (args.Result == ContentDialogResult.Primary &&
                PresetComboBox.SelectedIndex >= 0 &&
                PresetComboBox.SelectedIndex < EqualizerSettings.PresetNames.Length)
            {
                Settings.EqualizerPreset = (EqualizerPreset)PresetComboBox.SelectedIndex;
            }
            if (args.Result == ContentDialogResult.Primary)
                Settings.ScdAudioVolume = (float)VolumeSlider.Value;

            Player.DisableRealtimeEqualizer();
        }

        private static string FormatGain(double value) =>
            $"{(value / 10.0).ToString("0.0", CultureInfo.InvariantCulture)} dB";
    }
}
