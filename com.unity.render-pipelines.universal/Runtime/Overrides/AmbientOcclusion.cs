using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Post-processing/Ambient Occlusion")]
    public sealed class AmbientOcclusion : VolumeComponent, IPostProcessComponent
    {
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);

        public bool IsActive() => intensity.value > 0f;

        public bool IsTileCompatible() => false;
    }
}
