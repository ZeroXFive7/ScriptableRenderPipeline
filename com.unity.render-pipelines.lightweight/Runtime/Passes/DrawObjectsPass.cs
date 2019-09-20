using System.Collections.Generic;

namespace UnityEngine.Rendering.LWRP
{
    /// <summary>
    /// Draw  objects into the given color and depth target
    ///
    /// You can use this pass to render objects that have a material and/or shader
    /// with the pass names LightweightForward or SRPDefaultUnlit.
    /// </summary>
    internal class DrawObjectsPass : BaseForwardPass
    {
        public DrawObjectsPass(string profilerTag, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask)
            : base(profilerTag, opaque, evt, renderQueueRange, layerMask)
        {
            m_ShaderTagIdList.Add(new ShaderTagId("LightweightForward"));
            m_ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
        }

        protected override void RenderFiltered(ScriptableRenderContext context, Camera camera, ref RenderingData renderingData, ref DrawingSettings drawSettings, ref FilteringSettings filteringSettings, ref RenderStateBlock renderStateBlock)
        {
            context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings, ref renderStateBlock);

            // Render objects that did not match any shader pass with error shader
            RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filteringSettings, SortingCriteria.None);
        }
    }
}
