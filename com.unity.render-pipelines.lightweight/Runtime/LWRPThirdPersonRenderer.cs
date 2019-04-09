using UnityEngine;

namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    public class LWRPThirdPersonRenderer : MonoBehaviour
    {
        private Renderer[] renderers = null;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }
    }
}
