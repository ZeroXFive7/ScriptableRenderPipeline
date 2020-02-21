using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [VolumeComponentEditor(typeof(AmbientOcclusion))]
    sealed class AmbientOcclusionEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Intensity;
        SerializedDataParameter m_SampleRadius;
        SerializedDataParameter m_Downsample;
        SerializedDataParameter m_SampleCount;
        SerializedDataParameter m_Color;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AmbientOcclusion>(serializedObject);

            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_SampleRadius = Unpack(o.Find(x => x.sampleRadius));
            m_Downsample = Unpack(o.Find(x => x.downsample));
            m_SampleCount = Unpack(o.Find(x => x.sampleCount));
            m_Color = Unpack(o.Find(x => x.color));
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Ambient Occlusion", EditorStyles.miniLabel);

            PropertyField(m_Intensity);
            PropertyField(m_SampleRadius);
            PropertyField(m_Downsample);
            PropertyField(m_SampleCount);
            PropertyField(m_Color);
        }
    }
}
