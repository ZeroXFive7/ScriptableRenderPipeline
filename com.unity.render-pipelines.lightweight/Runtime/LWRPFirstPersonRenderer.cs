using UnityEngine;

namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    public class LWRPFirstPersonRenderer : MonoBehaviour
    {
        private Renderer[] renderers = null;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.renderingLayerMask = 2;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
