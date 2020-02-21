using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Post-processing/Ambient Occlusion")]
    public sealed class AmbientOcclusion : VolumeComponent, IPostProcessComponent
    {
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);

        public MinFloatParameter sampleRadius = new MinFloatParameter(0f, 0f);

        public MaxFloatParameter downsample = new MaxFloatParameter(1f, 1f);

        public ClampedIntParameter sampleCount = new ClampedIntParameter(3, 1, 16);

        public ColorParameter color = new ColorParameter(Color.gray, false, false, true);

        public bool IsActive() => intensity.value > 0f;

        public bool IsTileCompatible() => false;
    }
}
