using VfxEditor.ScdFormat;

namespace Pickles_Playlist_Editor
{
    public sealed class ScdParameterSettings
    {
        public float SoundVolume { get; set; }
        public int BusDuckingFadeTime { get; set; }
        public float BusDuckingVolume { get; set; }
        public int BusDuckingNumber { get; set; }
        public int AttributeVersion { get; set; }
        public int AttributeConditionFirst { get; set; }
        public bool LoopEnabled { get; set; }

        public static ScdParameterSettings FromScd(ScdFile scd)
        {
            return new ScdParameterSettings
            {
                SoundVolume = scd.Sounds.Count > 0 ? scd.Sounds[0].Volume.Value : 1f,
                BusDuckingFadeTime = scd.Sounds.Count > 0 ? scd.Sounds[0].BusDucking.FadeTime.Value : 1200,
                BusDuckingVolume = scd.Sounds.Count > 0 ? scd.Sounds[0].BusDucking.Volume.Value : 0f,
                BusDuckingNumber = scd.Sounds.Count > 0 ? scd.Sounds[0].BusDucking.Number.Value : 1,
                AttributeVersion = scd.Attributes.Count > 0 ? scd.Attributes[0].Version.Value : 1,
                AttributeConditionFirst = scd.Attributes.Count > 0 ? scd.Attributes[0].ConditionFirst.Value : 0,
                LoopEnabled = scd.Sounds.Count > 0 && scd.Sounds[0].Attributes.Value.HasFlag(VfxEditor.ScdFormat.SoundAttribute.Loop),
            };
        }

        public void ApplyTo(ScdFile scd)
        {
            if (scd.Sounds.Count > 0)
            {
                scd.Sounds[0].Volume.Value = SoundVolume;
                scd.Sounds[0].BusDucking.FadeTime.Value = BusDuckingFadeTime;
                scd.Sounds[0].BusDucking.Volume.Value = BusDuckingVolume;
                scd.Sounds[0].BusDucking.Number.Value = (byte)System.Math.Clamp(BusDuckingNumber, byte.MinValue, byte.MaxValue);

                if (LoopEnabled)
                    scd.Sounds[0].Attributes.Value |= VfxEditor.ScdFormat.SoundAttribute.Loop;
                else
                    scd.Sounds[0].Attributes.Value &= ~VfxEditor.ScdFormat.SoundAttribute.Loop;
            }

            if (scd.Attributes.Count > 0)
            {
                scd.Attributes[0].Version.Value = (byte)System.Math.Clamp(AttributeVersion, byte.MinValue, byte.MaxValue);
                scd.Attributes[0].ConditionFirst.Value = (byte)System.Math.Clamp(AttributeConditionFirst, byte.MinValue, byte.MaxValue);
            }
        }
    }
}
