using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [VolumeComponentEditor(typeof(AmbientOcclusion))]
    sealed class AmbientOcclusionEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Intensity;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AmbientOcclusion>(serializedObject);

            m_Intensity = Unpack(o.Find(x => x.intensity));
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Ambient Occlusion", EditorStyles.miniLabel);

            PropertyField(m_Intensity);
        }
    }
}
