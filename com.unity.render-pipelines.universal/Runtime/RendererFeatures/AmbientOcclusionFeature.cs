using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.Universal
{
    public class AmbientOcclusionFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// Quality settings for <see cref="AmbientOcclusionMode.ScalableAmbientObscurance"/>.
        /// </summary>
        public enum AmbientOcclusionQuality
        {
            /// <summary>
            /// 4 samples + downsampling.
            /// </summary>
            Lowest,

            /// <summary>
            /// 6 samples + downsampling.
            /// </summary>
            Low,

            /// <summary>
            /// 10 samples + downsampling.
            /// </summary>
            Medium,

            /// <summary>
            /// 8 samples.
            /// </summary>
            High,

            /// <summary>
            /// 12 samples.
            /// </summary>
            Ultra
        }

        [System.Serializable]
        public class AmbientOcclusionSettings
        {
            public Shader ambientOcclusionPS;

            /// <summary>
            /// The degree of darkness added by ambient occlusion.
            /// </summary>
            [Range(0f, 4f), Tooltip("The degree of darkness added by ambient occlusion. Higher values produce darker areas.")]
            public float intensity = 1.0f;

            /// <summary>
            /// Radius of sample points, which affects extent of darkened areas.
            /// </summary>
            [Tooltip("The radius of sample points. This affects the size of darkened areas.")]
            public float radius = 0.25f;

            /// <summary>
            /// The number of sample points, which affects quality and performance. Lowest, Low and Medium
            /// passes are downsampled. High and Ultra are not and should only be used on high-end
            /// hardware.
            /// </summary>
            [Tooltip("The number of sample points. This affects both quality and performance. For \"Lowest\", \"Low\", and \"Medium\", passes are downsampled. For \"High\" and \"Ultra\", they are not and therefore you should only \"High\" and \"Ultra\" on high-end hardware.")]
            public AmbientOcclusionQuality quality = AmbientOcclusionQuality.Medium;
        }

        public AmbientOcclusionSettings settings = new AmbientOcclusionSettings();

        private readonly int[] SAMPLE_COUNTS = { 4, 6, 10, 8, 12 };

        private AmbientOcclusionPass ambientOcclusionPass;
        private Material material = null;

        public override void Create()
        {
            ambientOcclusionPass = new AmbientOcclusionPass();
            ambientOcclusionPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null && settings.ambientOcclusionPS != null)
            {
                material = LoadMaterial(settings.ambientOcclusionPS);
            }

            if (material == null || settings.intensity == 0.0f)
            {
                // Do not run ambient occlusion without a material or any visible impact.
                return;
            }

            ambientOcclusionPass.Setup(material, ref renderingData);
            ambientOcclusionPass.intensity = settings.intensity;
            ambientOcclusionPass.sampleRadius = settings.radius;
            ambientOcclusionPass.sampleCount = SAMPLE_COUNTS[(int)settings.quality];
            ambientOcclusionPass.downsample = (int)settings.quality < (int)AmbientOcclusionQuality.High;

            renderer.EnqueuePass(ambientOcclusionPass);
        }

        private Material LoadMaterial(Shader shader)
        {
            if (shader == null)
            {
                Debug.LogErrorFormat($"Missing shader. {GetType().DeclaringType.Name} render pass will not execute. Check for missing reference in the renderer resources.");
                return null;
            }

            return CoreUtils.CreateEngineMaterial(shader);
        }
    }
}
