using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    [Serializable, VolumeComponentMenu("Lighting/Screen Space Reflection")]
    public class ScreenSpaceReflection : VolumeComponent
    {

        public ClampedFloatParameter depthBufferThickness = new ClampedFloatParameter(0.01f, 0, 1);
        public ClampedFloatParameter screenFadeDistance = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);
        public ClampedFloatParameter minSmoothness = new ClampedFloatParameter(0.9f, 0.0f, 1.0f);
        public ClampedFloatParameter smoothnessFadeStart = new ClampedFloatParameter(0.9f, 0.0f, 1.0f);
        public BoolParameter         reflectSky          = new BoolParameter(true);

        public IntParameter rayMaxIterations = new IntParameter(32);

        public LayerMaskParameter layerMask = new LayerMaskParameter(-1);
        public ClampedFloatParameter rayLength = new ClampedFloatParameter(10f, 0.001f, 50f);
        public ClampedFloatParameter clampValue = new ClampedFloatParameter(1.0f, 0.001f, 10.0f);
        public BoolParameter denoise = new BoolParameter(false);
        public ClampedIntParameter denoiserRadius = new ClampedIntParameter(16, 1, 32);

        static ScreenSpaceReflection s_Default = null;
        public static ScreenSpaceReflection @default
        {
            get
            {
                if (s_Default == null)
                {
                    s_Default = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
                    s_Default.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_Default;
            }
        }

    }
}
