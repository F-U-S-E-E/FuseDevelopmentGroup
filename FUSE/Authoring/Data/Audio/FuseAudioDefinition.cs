using System;
using System.Collections.Generic;

namespace FUSE.Data
{
    public sealed class FuseAudioRoot
    {
        public Dictionary<string, FuseWhistleAudio> Whistles { get; set; } = new Dictionary<string, FuseWhistleAudio>();
        public Dictionary<string, FuseHornAudio> Horns { get; set; } = new Dictionary<string, FuseHornAudio>();
        public Dictionary<string, FuseBellAudio> Bells { get; set; } = new Dictionary<string, FuseBellAudio>();
    }

    public sealed class FuseWhistleAudio
    {
        public string Name { get; set; }
        public string Clip { get; set; }
        public FuseAudioAssetReference Model { get; set; }
        public float? RampUpPitch { get; set; }
        public float? LerpSpeed { get; set; }
        public float? AirLerpSpeed { get; set; }
    }

    public sealed class FuseHornAudio
    {
        public string Name { get; set; }
        public FuseHornLayer[] Layers { get; set; } = Array.Empty<FuseHornLayer>();
    }

    public sealed class FuseHornLayer
    {
        public string File { get; set; }
        public FuseAudioKeyframe[] Keyframes { get; set; } = Array.Empty<FuseAudioKeyframe>();
    }

    public sealed class FuseBellAudio
    {
        public string Name { get; set; }
        public string File { get; set; }
        public float[] IndexTimes { get; set; } = Array.Empty<float>();
    }

    public sealed class FuseAudioAssetReference
    {
        public string AssetPackIdentifier { get; set; }
        public string AssetIdentifier { get; set; }
    }

    public sealed class FuseAudioKeyframe
    {
        public float T { get; set; }
        public float Value { get; set; }
    }
}
