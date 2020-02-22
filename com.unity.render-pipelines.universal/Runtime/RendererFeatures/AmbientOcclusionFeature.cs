using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.Universal
{
    public class AmbientOcclusionFeature : ScriptableRendererFeature
    {
        [System.Serializable, ReloadGroup]
        public class AmbientOcclusionSettings
        {
            public Shader ambientOcclusionPS;

            public float intensity = 1.0f;

            public float sampleRadius = 0.5f;

            public float downsample = 1.0f;

            public int sampleCount = 16;

        }

        public AmbientOcclusionSettings settings = new AmbientOcclusionSettings();

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
            ambientOcclusionPass.sampleRadius = settings.sampleRadius;
            ambientOcclusionPass.downsample = settings.downsample;
            ambientOcclusionPass.sampleCount = settings.sampleCount;

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
