namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    public class LWRPFirstPersonRenderer : MonoBehaviour
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

            foreach (var renderer in renderers)
            {
                renderer.renderingLayerMask = 2;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
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
            var visibleInThisCamera = this.camera == null || this.camera == camera;

            foreach (var renderer in renderers)
            {
                renderer.enabled = visibleInThisCamera;
            }
        }

        #endregion
    }
}
