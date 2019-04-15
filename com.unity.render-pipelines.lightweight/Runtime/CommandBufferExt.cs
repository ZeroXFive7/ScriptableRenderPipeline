using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.LightweightPipeline
{
    internal static class CommandBufferExt
    {
        internal static void SetStencilState(this CommandBuffer cmd, int value, CompareFunction comp, StencilOp pass, StencilOp fail)
        {
            cmd.SetGlobalInt("_GlobalStencilRef", value);
            cmd.SetGlobalInt("_GlobalStencilComp", (int)comp);
            cmd.SetGlobalInt("_GlobalStencilPass", (int)pass);
            cmd.SetGlobalInt("_GlobalStencilFail", (int)fail);
        }
    }
}
