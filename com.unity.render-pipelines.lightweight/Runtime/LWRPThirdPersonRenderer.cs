namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    public class LWRPThirdPersonRenderer : MonoBehaviour
    {
        #region Fields

        private new Camera camera = null;
        private Renderer[] renderers = null;

        #endregion

        #region Properties

        public Camera Camera { set { camera = value; } }

        #endregion

        #region Mono Messages

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
        }

        private void OnEnable()
        {
            LightweightRenderPipeline.BeforeCameraRenderEvent += OnBeforeCameraRender;
        }

        private void OnDisable()
        {
            LightweightRenderPipeline.BeforeCameraRenderEvent -= OnBeforeCameraRender;
        }

        #endregion

        #region Camera Callbacks

        private void OnBeforeCameraRender(Camera camera)
        {
            var shadowOnlyInThisCamera = this.camera == null || this.camera == camera;

            foreach (var renderer in renderers)
            {
                renderer.shadowCastingMode = shadowOnlyInThisCamera ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        #endregion
    }
}
