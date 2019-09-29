using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Rendering.Universal
{
    public static class RenderingExtensions
    {
        public static void SetObliqueness(this ref Matrix4x4 matrix, float obliqueness)
        {
            matrix[1, 2] = obliqueness;
        }

        public static void SetObliqueness(this Camera camera, float obliqueness)
        {
            // Recalculate the base projection matrix using the current fov, etc settings on the camera.
            camera.ResetProjectionMatrix();

            // Modify the newly calculated matrix to be oblique.
            var projectionMatrix = camera.projectionMatrix;
            projectionMatrix.SetObliqueness(obliqueness);
            camera.projectionMatrix = projectionMatrix;
        }
    }
}
