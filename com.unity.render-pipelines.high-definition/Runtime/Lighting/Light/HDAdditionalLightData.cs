using System;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.Rendering;
using UnityEditor.Experimental.Rendering.HDPipeline;
#endif
using UnityEngine.Serialization;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    // This enum extent the original LightType enum with new light type from HD
    public enum LightTypeExtent
    {
        Punctual, // Fallback on LightShape type
        Rectangle,
        Tube,
        // Sphere,
        // Disc,
    };

    public enum SpotLightShape { Cone, Pyramid, Box };

    public enum LightUnit
    {
        Lumen,      // lm = total power/flux emitted by the light
        Candela,    // lm/sr = flux per steradian
        Lux,        // lm/m² = flux per unit area
        Luminance,  // lm/m²/sr = flux per unit area and per steradian
        Ev100,      // ISO 100 Exposure Value (https://en.wikipedia.org/wiki/Exposure_value)
    }

    // Light layering
    public enum LightLayerEnum
    {
        Nothing = 0,   // Custom name for "Nothing" option
        LightLayerDefault = 1 << 0,
        LightLayer1 = 1 << 1,
        LightLayer2 = 1 << 2,
        LightLayer3 = 1 << 3,
        LightLayer4 = 1 << 4,
        LightLayer5 = 1 << 5,
        LightLayer6 = 1 << 6,
        LightLayer7 = 1 << 7,
        Everything = 0xFF, // Custom name for "Everything" option
    }

    // This structure contains all the old values for every recordable fields from the HD light editor
    // so we can force timeline to record changes on other fields from the LateUpdate function (editor only)
    struct TimelineWorkaround
    {
        public float oldDisplayLightIntensity;
        public float oldLuxAtDistance;
        public float oldSpotAngle;
        public bool oldEnableSpotReflector;
        public Color oldLightColor;
        public Vector3 oldLocalScale;
        public bool oldDisplayAreaLightEmissiveMesh;
        public LightTypeExtent oldLightTypeExtent;
        public float oldLightColorTemperature;
        public float oldIntensity;
        public bool lightEnabled;
    }


    [Serializable]
    public class ShadowResolutionSettingValue
    {
        [SerializeField]
        private int m_Override;
        [SerializeField]
        private bool m_UseOverride;
        [SerializeField]
        private int m_Level;

        public int level
        {
            get => m_Level;
            set => m_Level = value;
        }

        public bool useOverride
        {
            get => m_UseOverride;
            set => m_UseOverride = value;
        }

        public int @override
        {
            get => m_Override;
            set => m_Override = value;
        }

        public int Value(ShadowResolutionSetting source) => m_UseOverride ? m_Override : source[m_Level];

        public void CopyTo(ShadowResolutionSettingValue target)
        {
            target.m_Override = m_Override;
            target.m_UseOverride = m_UseOverride;
            target.m_Level = m_Level;
        }
    }


    [Serializable]
    public class ShadowResolutionSetting: ISerializationCallbackReceiver
    {
        [SerializeField] private int[] m_Values;

        public ShadowResolutionSetting(int[] values) => m_Values = values;

        public int this[int index] => m_Values != null && index >= 0 && index < m_Values.Length  ? m_Values[index] : 0;

        public void OnBeforeSerialize()
        {
            Array.Resize(ref m_Values, 4);
        }

        public void OnAfterDeserialize()
        {
        }
    }

    //@TODO: We should continuously move these values
    // into the engine when we can see them being generally useful
    [RequireComponent(typeof(Light))]
    [ExecuteAlways]
    public class HDAdditionalLightData : MonoBehaviour, ISerializationCallbackReceiver
    {
        // TODO: Use proper migration toolkit
        // 3. Added ShadowNearPlane to HDRP additional light data, we don't use Light.shadowNearPlane anymore
        // 4. Migrate HDAdditionalLightData.lightLayer to Light.renderingLayerMask
        private const int currentVersion = 4;

        [HideInInspector, SerializeField]
        [FormerlySerializedAs("m_Version")]
        [System.Obsolete("version is deprecated, use m_Version instead")]
        private float version = currentVersion;
        [SerializeField]
        private int m_Version = currentVersion;

        // To be able to have correct default values for our lights and to also control the conversion of intensity from the light editor (so it is compatible with GI)
        // we add intensity (for each type of light we want to manage).
        [System.Obsolete("directionalIntensity is deprecated, use intensity and lightUnit instead")]
        public float directionalIntensity = k_DefaultDirectionalLightIntensity;
        [System.Obsolete("punctualIntensity is deprecated, use intensity and lightUnit instead")]
        public float punctualIntensity = k_DefaultPunctualLightIntensity;
        [System.Obsolete("areaIntensity is deprecated, use intensity and lightUnit instead")]
        public float areaIntensity = k_DefaultAreaLightIntensity;

        public const float k_DefaultDirectionalLightIntensity = Mathf.PI; // In lux
        public const float k_DefaultPunctualLightIntensity = 600.0f;      // Light default to 600 lumen, i.e ~48 candela
        public const float k_DefaultAreaLightIntensity = 200.0f;          // Light default to 200 lumen to better match point light

        public float intensity
        {
            get { return displayLightIntensity; }
            set { SetLightIntensity(value); }
        }

        // Only for Spotlight, should be hide for other light
        public bool enableSpotReflector = false;
        // Lux unity for all light except directional require a distance
        public float luxAtDistance = 1.0f;

        [Range(0.0f, 100.0f)]
        public float m_InnerSpotPercent; // To display this field in the UI this need to be public

        public float GetInnerSpotPercent01()
        {
            return Mathf.Clamp(m_InnerSpotPercent, 0.0f, 100.0f) / 100.0f;
        }

        [Range(0.0f, 1.0f)]
        public float lightDimmer = 1.0f;

        [Range(0.0f, 1.0f), SerializeField, FormerlySerializedAs("volumetricDimmer")]
        private float m_VolumetricDimmer = 1.0f;

        public float volumetricDimmer
        {
            get { return useVolumetric ? m_VolumetricDimmer : 0f; }
            set {  m_VolumetricDimmer = value; }
        }

        // Used internally to convert any light unit input into light intensity
        public LightUnit lightUnit = LightUnit.Lumen;

                if (!IsValidLightUnitForType(legacyLight.type, m_LightTypeExtent, m_SpotLightShape, value))
                {
                    var supportedTypes = String.Join(", ", GetSupportedLightUnits(legacyLight.type, m_LightTypeExtent, m_SpotLightShape));
                    Debug.LogError($"Set Light Unit '{value}' to a {GetLightTypeName()} is not allowed, only {supportedTypes} are supported.");
                    return;
                }

        // Not used for directional lights.
        public float fadeDistance = 10000.0f;

        public bool affectDiffuse = true;
        public bool affectSpecular = true;

        // This property work only with shadow mask and allow to say we don't render any lightMapped object in the shadow map
        public bool nonLightmappedOnly = false;

        public LightTypeExtent lightTypeExtent = LightTypeExtent.Punctual;

        // Only for Spotlight, should be hide for other light
        public SpotLightShape spotLightShape { get { return m_SpotLightShape; } set { SetSpotLightShape(value); } }
        [SerializeField, FormerlySerializedAs("spotLightShape")]
        SpotLightShape m_SpotLightShape = SpotLightShape.Cone;

        // Only for Rectangle/Line/box projector lights
        public float shapeWidth = 0.5f;

        // Only for Rectangle/box projector lights
        public float shapeHeight = 0.5f;

        // Only for pyramid projector
        public float aspectRatio = 1.0f;

        // Only for Punctual/Sphere/Disc
        public float shapeRadius = 0.0f;

        // Only for Spot/Point - use to cheaply fake specular spherical area light
        // It is not 1 to make sure the highlight does not disappear.
        [Range(0.0f, 1.0f)]
        public float maxSmoothness = 0.99f;

        // If true, we apply the smooth attenuation factor on the range attenuation to get 0 value, else the attenuation is just inverse square and never reach 0
        public bool applyRangeAttenuation = true;

        // This is specific for the LightEditor GUI and not use at runtime
        public bool useOldInspector = false;
        public bool useVolumetric = true;
        public bool featuresFoldout = true;
        public byte showAdditionalSettings = 0;
        public float displayLightIntensity;

                var supportedUnits = GetSupportedLightUnits(legacyLight.type, value, m_SpotLightShape);
                // If the current light unit is not supported by the new light type, we change it
                if (!supportedUnits.Any(u => u == lightUnit))
                    lightUnit = supportedUnits.First();

        // Optional cookie for rectangular area lights
        public Texture areaLightCookie = null;

        [Range(0.0f, 179.0f)]
        public float areaLightShadowCone = 120.0f;

#if ENABLE_RAYTRACING
        public bool useRayTracedShadows = false;
#endif

        [Range(0.0f, 42.0f)]
        public float evsmExponent = 15.0f;
        [Range(0.0f, 1.0f)]
        public float evsmLightLeakBias = 0.0f;
        [Range(0.0f, 0.001f)]
        public float evsmVarianceBias = 1e-5f;
        [Range(0, 8)]
        public int evsmBlurPasses = 0;

        // Duplication of HDLightEditor.k_MinAreaWidth, maybe do something about that
        const float k_MinAreaWidth = 0.01f; // Provide a small size of 1cm for line light

        [Obsolete("Use Light.renderingLayerMask instead")]
        public LightLayerEnum lightLayers = LightLayerEnum.LightLayerDefault;

        // This function return a mask of light layers as uint and handle the case of Everything as being 0xFF and not -1
        public uint GetLightLayers()
        {
            int value = m_Light.renderingLayerMask;
            return value < 0 ? (uint)LightLayerEnum.Everything : (uint)value;
        }

        // Shadow Settings
        public float    shadowNearPlane = 0.1f;

        // PCSS settings
        [Range(0, 1.0f)]
        public float    shadowSoftness = .5f;
        [Range(1, 64)]
        public int      blockerSampleCount = 24;
        [Range(1, 64)]
        public int      filterSampleCount = 16;
        [Range(0, 0.001f)]
        public float minFilterSize = 0.00001f;

        // Improved Moment Shadows settings
        [Range(1, 32)]
        public int kernelSize = 5;
        [Range(0.0f, 9.0f)]
        public float lightAngle = 1.0f;
        [Range(0.0001f, 0.01f)]
        public float maxDepthBias = 0.001f;

        HDShadowRequest[]   shadowRequests;
        bool                m_WillRenderShadows;
        int[]               m_ShadowRequestIndices;


        #if ENABLE_RAYTRACING
        // Temporary index that stores the current shadow index for the light
        [System.NonSerialized] public int shadowIndex;
        #endif

        [System.NonSerialized] HDShadowSettings    _ShadowSettings = null;
        HDShadowSettings    m_ShadowSettings
        {
            get
            {
                if (_ShadowSettings == null)
                    _ShadowSettings = VolumeManager.instance.stack.GetComponent<HDShadowSettings>();
                return _ShadowSettings;
            }
        }

        AdditionalShadowData _ShadowData;
        AdditionalShadowData m_ShadowData
        {
            get
            {
                if (_ShadowData == null)
                    _ShadowData = GetComponent<AdditionalShadowData>();
                return _ShadowData;
            }
        }

        // Only for Punctual/Sphere/Disc. Default shape radius is not 0 so that specular highlight is visible by default, it matches the previous default of 0.99 for MaxSmoothness.
        [SerializeField, FormerlySerializedAs("shapeRadius")]
        float m_ShapeRadius = 0.025f;
        /// <summary>
        /// Get/Set the radius of a light
        /// </summary>
        public float shapeRadius
        {
            get => m_ShapeRadius;
            set
            {
                if (m_ShapeRadius == value)
                    return;

                m_ShapeRadius = Mathf.Clamp(value, 0, float.MaxValue);
                UpdateAllLightValues();
            }
        }

        [SerializeField, FormerlySerializedAs("useCustomSpotLightShadowCone")]
        bool m_UseCustomSpotLightShadowCone = false;
        // Custom spot angle for spotlight shadows
        /// <summary>
        /// Toggle the custom spot light shadow cone.
        /// </summary>
        public bool useCustomSpotLightShadowCone
        {
            get => m_UseCustomSpotLightShadowCone;
            set
            {
                if (m_UseCustomSpotLightShadowCone == value)
                    return;

                m_UseCustomSpotLightShadowCone = value;
            }
        }

        [SerializeField, FormerlySerializedAs("customSpotLightShadowCone")]
        float m_CustomSpotLightShadowCone = 30.0f;
        /// <summary>
        /// Get/Set the custom spot shadow cone value.
        /// </summary>
        /// <value></value>
        public float customSpotLightShadowCone
        {
            get => m_CustomSpotLightShadowCone;
            set
            {
                if (m_CustomSpotLightShadowCone == value)
                    return;

                m_CustomSpotLightShadowCone = value;
            }
        }

        // Only for Spot/Point/Directional - use to cheaply fake specular spherical area light
        // It is not 1 to make sure the highlight does not disappear.
        [Range(0.0f, 1.0f)]
        [SerializeField, FormerlySerializedAs("maxSmoothness")]
        float m_MaxSmoothness = 0.99f;
        /// <summary>
        /// Get/Set the maximum smoothness of a punctual or directional light.
        /// </summary>
        public float maxSmoothness
        {
            get => m_MaxSmoothness;
            set
            {
                if (m_MaxSmoothness == value)
                    return;

                m_MaxSmoothness = Mathf.Clamp01(value);
            }
        }

        // If true, we apply the smooth attenuation factor on the range attenuation to get 0 value, else the attenuation is just inverse square and never reach 0
        [SerializeField, FormerlySerializedAs("applyRangeAttenuation")]
        bool m_ApplyRangeAttenuation = true;
        /// <summary>
        /// If enabled, apply a smooth attenuation factor so at the end of the range, the attenuation is 0.
        /// Otherwise the inverse-square attenuation is used and the value never reaches 0.
        /// </summary>
        public bool applyRangeAttenuation
        {
            get => m_ApplyRangeAttenuation;
            set
            {
                if (m_ApplyRangeAttenuation == value)
                    return;

                m_ApplyRangeAttenuation = value;
                UpdateAllLightValues();
            }
        }

        // When true, a mesh will be display to represent the area light (Can only be change in editor, component is added in Editor)
        [SerializeField, FormerlySerializedAs("displayAreaLightEmissiveMesh")]
        bool m_DisplayAreaLightEmissiveMesh = false;
        /// <summary>
        /// If enabled, display an emissive mesh rect synchronized with the intensity and color of the light.
        /// </summary>
        internal bool displayAreaLightEmissiveMesh
        {
            get => m_DisplayAreaLightEmissiveMesh;
            set
            {
                if (m_DisplayAreaLightEmissiveMesh == value)
                    return;

                m_DisplayAreaLightEmissiveMesh = value;

                UpdateAllLightValues();
            }
        }

        // Optional cookie for rectangular area lights
        [SerializeField, FormerlySerializedAs("areaLightCookie")]
        Texture m_AreaLightCookie = null;
        /// <summary>
        /// Get/Set cookie texture for area lights.
        /// </summary>
        public Texture areaLightCookie
        {
            get => m_AreaLightCookie;
            set
            {
                if (m_AreaLightCookie == value)
                    return;

                m_AreaLightCookie = value;
                UpdateAllLightValues();
            }
        }

        [Range(k_MinAreaLightShadowCone, k_MaxAreaLightShadowCone)]
        [SerializeField, FormerlySerializedAs("areaLightShadowCone")]
        float m_AreaLightShadowCone = 120.0f;
        /// <summary>
        /// Get/Set area light shadow cone value.
        /// </summary>
        public float areaLightShadowCone
        {
            get => m_AreaLightShadowCone;
            set
            {
                if (m_AreaLightShadowCone == value)
                    return;

                m_AreaLightShadowCone = Mathf.Clamp(value, k_MinAreaLightShadowCone, k_MaxAreaLightShadowCone);
                UpdateAllLightValues();
            }
        }

        // Flag that tells us if the shadow should be screen space
        [SerializeField, FormerlySerializedAs("useScreenSpaceShadows")]
        bool m_UseScreenSpaceShadows = false;
        /// <summary>
        /// Controls if we resolve the directional light shadows in screen space (ray tracing only).
        /// </summary>
        public bool useScreenSpaceShadows
        {
            get => m_UseScreenSpaceShadows;
            set
            {
                if (m_UseScreenSpaceShadows == value)
                    return;

                m_UseScreenSpaceShadows = value;
            }
        }

        // Directional lights only.
        [SerializeField, FormerlySerializedAs("interactsWithSky")]
        bool m_InteractsWithSky = true;
        /// <summary>
        /// Controls if the directional light affect the Physically Based sky.
        /// This have no effect on other skies.
        /// </summary>
        public bool interactsWithSky
        {
            get => m_InteractsWithSky;
            set
            {
                if (m_InteractsWithSky == value)
                    return;

                m_InteractsWithSky = value;
            }
        }
        [SerializeField, FormerlySerializedAs("angularDiameter")]
        float m_AngularDiameter = 0;
        /// <summary>
        /// Angular diameter of the emissive celestial body represented by the light as seen from the camera (in degrees).
        /// Used to render the sun/moon disk.
        /// </summary>
        public float angularDiameter
        {
            get => m_AngularDiameter;
            set
            {
                if (m_AngularDiameter == value)
                    return;

                m_AngularDiameter = Mathf.Clamp(value, 0, 360);
            }
        }

        [SerializeField, FormerlySerializedAs("distance")]
        float m_Distance = 150000000.0f; // Sun to Earth
        /// <summary>
        /// Distance from the camera to the emissive celestial body represented by the light.
        /// </summary>
        public float distance
        {
            get => m_Distance;
            set
            {
                if (m_Distance == value)
                    return;

                m_Distance = value;
            }
        }

#if ENABLE_RAYTRACING
        [SerializeField, FormerlySerializedAs("useRayTracedShadows")]
        bool m_UseRayTracedShadows = false;
        /// <summary>
        /// Controls if we use ray traced shadows.
        /// </summary>
        public bool useRayTracedShadows
        {
            get => m_UseRayTracedShadows;
            set
            {
                if (m_UseRayTracedShadows == value)
                    return;

                m_UseRayTracedShadows = value;
            }
        }

        [Range(1, 32)]
        [SerializeField, FormerlySerializedAs("numRayTracingSamples")]
        int m_NumRayTracingSamples = 4;
        /// <summary>
        /// Controls the number of sample used for the ray traced shadows.
        /// </summary>
        public int numRayTracingSamples
        {
            get => m_NumRayTracingSamples;
            set
            {
                if (m_NumRayTracingSamples == value)
                    return;

                m_NumRayTracingSamples = Mathf.Clamp(value, 1, 32);
            }
        }

        [SerializeField, FormerlySerializedAs("filterTracedShadow")]
        bool m_FilterTracedShadow = true;
        /// <summary>
        /// Toggle the filtering of ray traced shadows.
        /// </summary>
        public bool filterTracedShadow
        {
            get => m_FilterTracedShadow;
            set
            {
                if (m_FilterTracedShadow == value)
                    return;

                m_FilterTracedShadow = value;
            }
        }

        [Range(1, 32)]
        [SerializeField, FormerlySerializedAs("filterSizeTraced")]
        int m_FilterSizeTraced = 16;
        /// <summary>
        /// Control the size of the filter used for ray traced shadows
        /// </summary>
        public int filterSizeTraced
        {
            get => m_FilterSizeTraced;
            set
            {
                if (m_FilterSizeTraced == value)
                    return;

                m_FilterSizeTraced = Mathf.Clamp(value, 1, 32);
            }
        }

        [Range(0.0f, 2.0f)]
        [SerializeField, FormerlySerializedAs("sunLightConeAngle")]
        float m_SunLightConeAngle = 0.5f;
        /// <summary>
        /// Angular size of the sun in degree.
        /// </summary>
        public float sunLightConeAngle
        {
            get => m_SunLightConeAngle;
            set
            {
                if (m_SunLightConeAngle == value)
                    return;

                m_SunLightConeAngle = Mathf.Clamp(value, 0.0f, 2.0f);
            }
        }

        [SerializeField, FormerlySerializedAs("lightShadowRadius")]
        float m_LightShadowRadius = 0.5f;
        /// <summary>
        /// Angular size of the sun in degree.
        /// </summary>
        public float lightShadowRadius
        {
            get => m_LightShadowRadius;
            set
            {
                if (m_LightShadowRadius == value)
                    return;

                m_LightShadowRadius = Mathf.Max(value, 0.001f);
            }
        }
#endif

        [Range(k_MinEvsmExponent, k_MaxEvsmExponent)]
        [SerializeField, FormerlySerializedAs("evsmExponent")]
        float m_EvsmExponent = 15.0f;
        /// <summary>
        /// Controls the exponent used for EVSM shadows.
        /// </summary>
        public float evsmExponent
        {
            get => m_EvsmExponent;
            set
            {
                if (m_EvsmExponent == value)
                    return;

                m_EvsmExponent = Mathf.Clamp(value, k_MinEvsmExponent, k_MaxEvsmExponent);
            }
        }

        [Range(k_MinEvsmLightLeakBias, k_MaxEvsmLightLeakBias)]
        [SerializeField, FormerlySerializedAs("evsmLightLeakBias")]
        float m_EvsmLightLeakBias = 0.0f;
        /// <summary>
        /// Controls the light leak bias value for EVSM shadows.
        /// </summary>
        public float evsmLightLeakBias
        {
            get => m_EvsmLightLeakBias;
            set
            {
                if (m_EvsmLightLeakBias == value)
                    return;

                m_EvsmLightLeakBias = Mathf.Clamp(value, k_MinEvsmLightLeakBias, k_MaxEvsmLightLeakBias);
            }
        }

        [Range(k_MinEvsmVarianceBias, k_MaxEvsmVarianceBias)]
        [SerializeField, FormerlySerializedAs("evsmVarianceBias")]
        float m_EvsmVarianceBias = 1e-5f;
        /// <summary>
        /// Controls the variance bias used for EVSM shadows.
        /// </summary>
        public float evsmVarianceBias
        {
            get => m_EvsmVarianceBias;
            set
            {
                if (m_EvsmVarianceBias == value)
                    return;

                m_EvsmVarianceBias = Mathf.Clamp(value, k_MinEvsmVarianceBias, k_MaxEvsmVarianceBias);
            }
        }

        [Range(k_MinEvsmBlurPasses, k_MaxEvsmBlurPasses)]
        [SerializeField, FormerlySerializedAs("evsmBlurPasses")]
        int m_EvsmBlurPasses = 0;
        /// <summary>
        /// Controls the number of blur passes used for EVSM shadows.
        /// </summary>
        public int evsmBlurPasses
        {
            get => m_EvsmBlurPasses;
            set
            {
                if (m_EvsmBlurPasses == value)
                    return;

                m_EvsmBlurPasses = Mathf.Clamp(value, k_MinEvsmBlurPasses, k_MaxEvsmBlurPasses);
            }
        }

        // Now the renderingLayerMask is used for shadow layers and not light layers
        [SerializeField, FormerlySerializedAs("lightlayersMask")]
        LightLayerEnum m_LightlayersMask = LightLayerEnum.LightLayerDefault;
        /// <summary>
        /// Controls which layer will be affected by this light
        /// </summary>
        /// <value></value>
        public LightLayerEnum lightlayersMask
        {
            get => linkShadowLayers ? (LightLayerEnum)RenderingLayerMaskToLightLayer(legacyLight.renderingLayerMask) : m_LightlayersMask;
            set
            {
                m_LightlayersMask = value;

                if (linkShadowLayers)
                    legacyLight.renderingLayerMask = LightLayerToRenderingLayerMask((int)m_LightlayersMask, legacyLight.renderingLayerMask);
            }
        }

        [SerializeField, FormerlySerializedAs("linkShadowLayers")]
        bool m_LinkShadowLayers = true;
        /// <summary>
        /// Controls if we want to synchronize shadow map light layers and light layers or not.
        /// </summary>
        public bool linkShadowLayers
        {
            get => m_LinkShadowLayers;
            set => m_LinkShadowLayers = value;
        }

        /// <summary>
        /// Returns a mask of light layers as uint and handle the case of Everything as being 0xFF and not -1
        /// </summary>
        /// <returns></returns>
        public uint GetLightLayers()
        {
            int value = (int)lightlayersMask;
            return value < 0 ? (uint)LightLayerEnum.Everything : (uint)value;
        }

        // Shadow Settings
        [SerializeField, FormerlySerializedAs("shadowNearPlane")]
        float    m_ShadowNearPlane = 0.1f;
        /// <summary>
        /// Controls the near plane distance of the shadows.
        /// </summary>
        public float shadowNearPlane
        {
            get => m_ShadowNearPlane;
            set
            {
                if (m_ShadowNearPlane == value)
                    return;

                m_ShadowNearPlane = Mathf.Clamp(value, HDShadowUtils.k_MinShadowNearPlane, HDShadowUtils.k_MaxShadowNearPlane);
            }
        }

        // PCSS settings
        [Range(0, 1.0f)]
        [SerializeField, FormerlySerializedAs("shadowSoftness")]
        float    m_ShadowSoftness = .5f;
        /// <summary>
        /// Controls how much softness you want for PCSS shadows.
        /// </summary>
        public float shadowSoftness
        {
            get => m_ShadowSoftness;
            set
            {
                if (m_ShadowSoftness == value)
                    return;

                m_ShadowSoftness = Mathf.Clamp01(value);
            }
        }

        [Range(1, 64)]
        [SerializeField, FormerlySerializedAs("blockerSampleCount")]
        int      m_BlockerSampleCount = 24;
        /// <summary>
        /// Controls the number of samples used to detect blockers for PCSS shadows.
        /// </summary>
        public int blockerSampleCount
        {
            get => m_BlockerSampleCount;
            set
            {
                if (m_BlockerSampleCount == value)
                    return;

                m_BlockerSampleCount = Mathf.Clamp(value, 1, 64);
            }
        }

        [Range(1, 64)]
        [SerializeField, FormerlySerializedAs("filterSampleCount")]
        int      m_FilterSampleCount = 16;
        /// <summary>
        /// Controls the number of samples used to filter for PCSS shadows.
        /// </summary>
        public int filterSampleCount
        {
            get => m_FilterSampleCount;
            set
            {
                if (m_FilterSampleCount == value)
                    return;

                m_FilterSampleCount = Mathf.Clamp(value, 1, 64);
            }
        }

        [Range(0, 0.001f)]
        [SerializeField, FormerlySerializedAs("minFilterSize")]
        float m_MinFilterSize = 0.00001f;
        /// <summary>
        /// Controls the minimum filter size of PCSS shadows.
        /// </summary>
        public float minFilterSize
        {
            get => m_MinFilterSize;
            set
            {
                if (m_MinFilterSize == value)
                    return;

                m_MinFilterSize = Mathf.Clamp(value, 0.0f, 0.001f);
            }
        }

        // Improved Moment Shadows settings
        [Range(1, 32)]
        [SerializeField, FormerlySerializedAs("kernelSize")]
        int m_KernelSize = 5;
        /// <summary>
        /// Controls the kernel size for IMSM shadows.
        /// </summary>
        public int kernelSize
        {
            get => m_KernelSize;
            set
            {
                if (m_KernelSize == value)
                    return;

                m_KernelSize = Mathf.Clamp(value, 1, 32);
            }
        }

        [Range(0.0f, 9.0f)]
        [SerializeField, FormerlySerializedAs("lightAngle")]
        float m_LightAngle = 1.0f;
        /// <summary>
        /// Controls the light angle for IMSM shadows.
        /// </summary>
        public float lightAngle
        {
            get => m_LightAngle;
            set
            {
                if (m_LightAngle == value)
                    return;

                m_LightAngle = Mathf.Clamp(value, 0.0f, 9.0f);
            }
        }

        [Range(0.0001f, 0.01f)]
        [SerializeField, FormerlySerializedAs("maxDepthBias")]
        float m_MaxDepthBias = 0.001f;
        /// <summary>
        /// Controls the max depth bias for IMSM shadows.
        /// </summary>
        public float maxDepthBias
        {
            get => m_MaxDepthBias;
            set
            {
                if (m_MaxDepthBias == value)
                    return;

                m_MaxDepthBias = Mathf.Clamp(value, 0.0001f, 0.01f);
            }
        }

        /// <summary>
        /// The range of the light.
        /// </summary>
        /// <value></value>
        public float range
        {
            get => legacyLight.range;
            set => legacyLight.range = value;
        }

        /// <summary>
        /// Color of the light.
        /// </summary>
        public Color color
        {
            get => legacyLight.color;
            set
            {
                legacyLight.color = value;

                // Update Area Light Emissive mesh color
                UpdateAreaLightEmissiveMesh();
            }
        }

        #endregion

        #region HDShadow Properties API (from AdditionalShadowData)
        [SerializeField] private ShadowResolutionSettingValue m_ShadowResolution = new ShadowResolutionSettingValue
        {
            @override = k_DefaultShadowResolution,
            useOverride = true,
        };
        public ShadowResolutionSettingValue shadowResolution => m_ShadowResolution;

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float m_ShadowDimmer = 1.0f;
        /// <summary>
        /// Get/Set the shadow dimmer.
        /// </summary>
        public float shadowDimmer
        {
            get => m_ShadowDimmer;
            set
            {
                if (m_ShadowDimmer == value)
                    return;

                m_ShadowDimmer = Mathf.Clamp01(value);
            }
        }

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float m_VolumetricShadowDimmer = 1.0f;
        /// <summary>
        /// Get/Set the volumetric shadow dimmer value, between 0 and 1.
        /// </summary>
        public float volumetricShadowDimmer
        {
            get => useVolumetric ? m_VolumetricShadowDimmer : 0.0f;
            set
            {
                if (m_VolumetricShadowDimmer == value)
                    return;

                m_VolumetricShadowDimmer = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        float m_ShadowFadeDistance = 10000.0f;
        /// <summary>
        /// Get/Set the shadow fade distance.
        /// </summary>
        public float shadowFadeDistance
        {
            get => m_ShadowFadeDistance;
            set
            {
                if (m_ShadowFadeDistance == value)
                    return;

                m_ShadowFadeDistance = Mathf.Clamp(value, 0, float.MaxValue);
            }
        }

        [SerializeField]
        BoolScalableSettingValue m_UseContactShadow = new BoolScalableSettingValue { useOverride = true };
        public BoolScalableSettingValue useContactShadow => m_UseContactShadow;

        [SerializeField]
        Color m_ShadowTint = Color.black;
        /// <summary>
        /// Controls the tint of the shadows.
        /// </summary>
        /// <value></value>
        public Color shadowTint
        {
            get => m_ShadowTint;
            set
            {
                if (m_ShadowTint == value)
                    return;

                m_ShadowTint = value;
            }
        }

        [SerializeField]
        float m_NormalBias = 0.75f;
        /// <summary>
        /// Get/Set the normal bias of the shadow maps.
        /// </summary>
        /// <value></value>
        public float normalBias
        {
            get => m_NormalBias;
            set
            {
                if (m_NormalBias == value)
                    return;

                m_NormalBias = value;
            }
        }

        [SerializeField]
        float m_ConstantBias = 0.15f;
        /// <summary>
        /// Get/Set the constant bias of the shadow maps.
        /// </summary>
        /// <value></value>
        public float constantBias
        {
            get => m_ConstantBias;
            set
            {
                if (m_ConstantBias == value)
                    return;

                m_ConstantBias = value;
            }
        }

        [SerializeField]
        ShadowUpdateMode m_ShadowUpdateMode = ShadowUpdateMode.EveryFrame;
        /// <summary>
        /// Get/Set the shadow update mode.
        /// </summary>
        /// <value></value>
        public ShadowUpdateMode shadowUpdateMode
        {
            get => m_ShadowUpdateMode;
            set
            {
                if (m_ShadowUpdateMode == value)
                    return;

                m_ShadowUpdateMode = value;
            }
        }

#endregion

#region Internal API for moving shadow datas from AdditionalShadowData to HDAdditionalLightData

        [SerializeField]
        float[] m_ShadowCascadeRatios = new float[3] { 0.05f, 0.2f, 0.3f };
        internal float[] shadowCascadeRatios
        {
            get => m_ShadowCascadeRatios;
            set => m_ShadowCascadeRatios = value;
        }

        [SerializeField]
        float[] m_ShadowCascadeBorders = new float[4] { 0.2f, 0.2f, 0.2f, 0.2f };
        internal float[] shadowCascadeBorders
        {
            get => m_ShadowCascadeBorders;
            set => m_ShadowCascadeBorders = value;
        }

        [SerializeField]
        int m_ShadowAlgorithm = 0;
        internal int shadowAlgorithm
        {
            get => m_ShadowAlgorithm;
            set => m_ShadowAlgorithm = value;
        }

        [SerializeField]
        int m_ShadowVariant = 0;
        internal int shadowVariant
        {
            get => m_ShadowVariant;
            set => m_ShadowVariant = value;
        }

        [SerializeField]
        int m_ShadowPrecision = 0;
        internal int shadowPrecision
        {
            get => m_ShadowPrecision;
            set => m_ShadowPrecision = value;
        }

#endregion

#pragma warning disable 0414 // The field '...' is assigned but its value is never used, these fields are used by the inspector
        // This is specific for the LightEditor GUI and not use at runtime
        [SerializeField, FormerlySerializedAs("useOldInspector")]
        bool useOldInspector = false;
        [SerializeField, FormerlySerializedAs("useVolumetric")]
        bool useVolumetric = true;
        [SerializeField, FormerlySerializedAs("featuresFoldout")]
        bool featuresFoldout = true;
        [SerializeField, FormerlySerializedAs("showAdditionalSettings")]
        byte showAdditionalSettings = 0;
#pragma warning restore 0414

        HDShadowRequest[]   shadowRequests;
        bool                m_WillRenderShadowMap;
        bool                m_WillRenderScreenSpaceShadow;
#if ENABLE_RAYTRACING
        bool                m_WillRenderRayTracedShadow;
#endif
        int[]               m_ShadowRequestIndices;
        bool                m_ShadowMapRenderedSinceLastRequest = false;

        // Data for cached shadow maps.
        Vector2             m_CachedShadowResolution = new Vector2(0,0);
        Vector3             m_CachedViewPos = new Vector3(0, 0, 0);

        int[]               m_CachedResolutionRequestIndices = new int[6];
        bool                m_CachedDataIsValid = true;
        // This is useful to detect whether the atlas has been repacked since the light was last seen
        int                 m_AtlasShapeID = 0;

        [System.NonSerialized]
        Plane[]             m_ShadowFrustumPlanes = new Plane[6];

        #if ENABLE_RAYTRACING
        // Temporary index that stores the current shadow index for the light
        [System.NonSerialized] internal int shadowIndex;
        #endif

        [System.NonSerialized] HDShadowSettings    _ShadowSettings = null;
        HDShadowSettings    m_ShadowSettings
        {
            get
            {
                if (_ShadowSettings == null)
                    _ShadowSettings = VolumeManager.instance.stack.GetComponent<HDShadowSettings>();
                return _ShadowSettings;
            }
        }

        // Runtime datas used to compute light intensity
        Light m_Light;
        internal Light legacyLight
        {
            get
            {
                TryGetComponent<Light>(out m_Light);
                return m_Light;
            }
        }
        
        MeshRenderer m_EmissiveMeshRenderer;
        internal MeshRenderer emissiveMeshRenderer
        {
            get
            {
                if (m_EmissiveMeshRenderer == null)
                {
                    TryGetComponent<MeshRenderer>(out m_EmissiveMeshRenderer);
                }
                
                return m_EmissiveMeshRenderer;
            }
        }

        MeshFilter m_EmissiveMeshFilter;
        internal MeshFilter emissiveMeshFilter
        {
            get
            {
                if (m_EmissiveMeshFilter == null)
                {
                    TryGetComponent<MeshFilter>(out m_EmissiveMeshFilter);
                }
                
                return m_EmissiveMeshFilter;
            }
        }

        private void DisableCachedShadowSlot()
        {
            ShadowMapType shadowMapType = (lightTypeExtent == LightTypeExtent.Rectangle) ? ShadowMapType.AreaLightAtlas :
                  (legacyLight.type != LightType.Directional) ? ShadowMapType.PunctualAtlas : ShadowMapType.CascadedDirectional;

            if (WillRenderShadowMap() && !ShadowIsUpdatedEveryFrame())
            {
                HDShadowManager.instance.MarkCachedShadowSlotsAsEmpty(shadowMapType, GetInstanceID());
            }
        }

        void OnDestroy()
        {
            DisableCachedShadowSlot();
        }

        void OnDisable()
        {
            DisableCachedShadowSlot();
            SetEmissiveMeshRendererEnabled(false);
        }

        void SetEmissiveMeshRendererEnabled(bool enabled)
        {
            if (displayAreaLightEmissiveMesh && emissiveMeshRenderer)
            {
                emissiveMeshRenderer.enabled = enabled;
            }
        }

        int GetShadowRequestCount()
        {
            return (m_Light.type == LightType.Point && lightTypeExtent == LightTypeExtent.Punctual) ? 6 : (m_Light.type == LightType.Directional) ? m_ShadowSettings.cascadeShadowSplitCount : 1;
        }

        public void ReserveShadows(Camera camera, HDShadowManager shadowManager, HDShadowInitParameters initParameters, CullingResults cullResults, FrameSettings frameSettings, int lightIndex)
        {
            Bounds bounds;
            float cameraDistance = Vector3.Distance(camera.transform.position, transform.position);

            m_WillRenderShadows = m_Light.shadows != LightShadows.None && frameSettings.IsEnabled(FrameSettingsField.Shadow);

            m_WillRenderShadows &= cullResults.GetShadowCasterBounds(lightIndex, out bounds);
            // When creating a new light, at the first frame, there is no AdditionalShadowData so we can't really render shadows
            m_WillRenderShadows &= m_ShadowData != null && m_ShadowData.shadowDimmer > 0;
            // If the shadow is too far away, we don't render it
            if (m_ShadowData != null)
                m_WillRenderShadows &= m_Light.type == LightType.Directional || cameraDistance < (m_ShadowData.shadowFadeDistance);

#if ENABLE_RAYTRACING
            m_WillRenderRayTracedShadow = false;
#endif

            // If this camera does not allow screen space shadows we are done, set the target parameters to false and leave the function
            if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.ScreenSpaceShadows) || !m_WillRenderShadowMap)
                return;

#if ENABLE_RAYTRACING
            LightCategory lightCategory = LightCategory.Count;
            GPULightType gpuLightType = GPULightType.Point;
            LightVolumeType lightVolumeType = LightVolumeType.Count;
            HDRenderPipeline.EvaluateGPULightType(legacyLight.type, lightTypeExtent, spotLightShape, ref lightCategory, ref gpuLightType, ref lightVolumeType);

            // Flag the ray tracing only shadows
            if (m_UseRayTracedShadows && (gpuLightType == GPULightType.Rectangle || gpuLightType == GPULightType.Point || (gpuLightType == GPULightType.Spot && lightVolumeType == LightVolumeType.Cone)))
            {
                m_WillRenderScreenSpaceShadow = true;
                m_WillRenderRayTracedShadow = true;
            }

            // Flag the directional shadow
            if (useScreenSpaceShadows && gpuLightType == GPULightType.Directional)
            {
                m_WillRenderScreenSpaceShadow = true;
                if (m_UseRayTracedShadows)
                {
                    m_WillRenderRayTracedShadow = true;
                }
            }
#endif

            if (!m_WillRenderShadows)
                return;

            // Create shadow requests array using the light type
            if (shadowRequests == null || m_ShadowRequestIndices == null)
            {
                const int maxLightShadowRequestsCount = 6;
                shadowRequests = new HDShadowRequest[maxLightShadowRequestsCount];
                m_ShadowRequestIndices = new int[maxLightShadowRequestsCount];

                for (int i = 0; i < maxLightShadowRequestsCount; i++)
                    shadowRequests[i] = new HDShadowRequest();
            }

            Vector2 viewportSize = new Vector2(m_ShadowData.shadowResolution, m_ShadowData.shadowResolution);

            // Compute dynamic shadow resolution
            if (initParameters.useDynamicViewportRescale && m_Light.type != LightType.Directional)
            {
                // resize viewport size by the normalized size of the light on screen
                // When we will have access to the non screen clamped bounding sphere light size, we could use it to scale the shadow map resolution
                // For the moment, this will be enough
                viewportSize *= Mathf.Lerp(64f / viewportSize.x, 1f, m_Light.range / (camera.transform.position - transform.position).magnitude);
                viewportSize = Vector2.Max(new Vector2(64f, 64f) / viewportSize, viewportSize);

                // Prevent flickering caused by the floating size of the viewport
                viewportSize.x = Mathf.Round(viewportSize.x);
                viewportSize.y = Mathf.Round(viewportSize.y);
            }

            viewportSize = Vector2.Max(viewportSize, new Vector2(16, 16));

            // Update the directional shadow atlas size
            if (m_Light.type == LightType.Directional)
                shadowManager.UpdateDirectionalShadowResolution((int)viewportSize.x, m_ShadowSettings.cascadeShadowSplitCount);

            int count = GetShadowRequestCount();
            bool needsCachedSlotsInAtlas = shadowsAreCached && !(ShadowIsUpdatedEveryFrame() || legacyLight.type == LightType.Directional);

            int count = GetShadowRequestCount();
            for (int index = 0; index < count; index++)
                m_ShadowRequestIndices[index] = shadowManager.ReserveShadowResolutions(viewportSize, shadowMapType);
        }

        public bool WillRenderShadows()
        {
            return m_WillRenderShadows;
        }

        // This offset shift the position of the spotlight used to approximate the area light shadows. The offset is the minimum such that the full
        // area light shape is included in the cone spanned by the spot light. 
        public static float GetAreaLightOffsetForShadows(Vector2 shapeSize, float coneAngle)
        {
            float rectangleDiagonal = shapeSize.magnitude;
            float halfAngle = coneAngle * 0.5f;
            float cotanHalfAngle = 1.0f / Mathf.Tan(halfAngle * Mathf.Deg2Rad);
            float offset = rectangleDiagonal * cotanHalfAngle;

            return -offset;
        }

        // Must return the first executed shadow request
        public int UpdateShadowRequest(HDCamera hdCamera, HDShadowManager manager, VisibleLight visibleLight, CullingResults cullResults, int lightIndex, out int shadowRequestCount)
        {
            int                 firstShadowRequestIndex = -1;
            Vector3             cameraPos = hdCamera.camera.transform.position;
            shadowRequestCount = 0;

            int count = GetShadowRequestCount();
            for (int index = 0; index < count; index++)
            {
                var         shadowRequest = shadowRequests[index];
                Matrix4x4   invViewProjection = Matrix4x4.identity;
                int         shadowRequestIndex = m_ShadowRequestIndices[index];

                ShadowMapType shadowMapType = (lightTypeExtent == LightTypeExtent.Rectangle) ? ShadowMapType.AreaLightAtlas :
                              (legacyLight.type != LightType.Directional) ? ShadowMapType.PunctualAtlas : ShadowMapType.CascadedDirectional;

                bool hasCachedSlotInAtlas = !(ShadowIsUpdatedEveryFrame() || legacyLight.type == LightType.Directional);

                bool shouldUseRequestFromCachedList = shadowIsCached && hasCachedSlotInAtlas && !manager.AtlasHasResized(shadowMapType);
                bool cachedDataIsValid = shadowIsCached && m_CachedDataIsValid && (manager.GetAtlasShapeID(shadowMapType) == m_AtlasShapeID) && manager.CachedDataIsValid(shadowMapType);
                HDShadowResolutionRequest resolutionRequest = manager.GetResolutionRequest(shadowMapType, shouldUseRequestFromCachedList, shouldUseRequestFromCachedList ? m_CachedResolutionRequestIndices[index] : shadowRequestIndex);

                if (resolutionRequest == null)
                    continue;

                Vector2 viewportSize = resolutionRequest.resolution;

                cachedDataIsValid = cachedDataIsValid || (legacyLight.type == LightType.Directional);
                shadowIsCached = shadowIsCached && (hasCachedSlotInAtlas && cachedDataIsValid || legacyLight.type == LightType.Directional);

                if (shadowRequestIndex == -1)
                    continue;

                if (lightTypeExtent == LightTypeExtent.Rectangle)
                {
                    Vector2 shapeSize = new Vector2(shapeWidth, shapeHeight);
                    float offset = GetAreaLightOffsetForShadows(shapeSize, areaLightShadowCone);
                    Vector3 shadowOffset = offset * visibleLight.GetForward();
                    HDShadowUtils.ExtractAreaLightData(hdCamera, visibleLight, lightTypeExtent, visibleLight.GetPosition() + shadowOffset, areaLightShadowCone, shadowNearPlane - offset, shapeSize, viewportSize, m_ShadowData.normalBiasMax, out shadowRequest.view, out invViewProjection, out shadowRequest.projection, out shadowRequest.deviceProjection, out shadowRequest.splitData);
                }
                else
                {
                    // Write per light type matrices, splitDatas and culling parameters
                    switch (m_Light.type)
                    {
                        case LightType.Point:
                            HDShadowUtils.ExtractPointLightData(
                                hdCamera, m_Light.type, visibleLight, viewportSize, shadowNearPlane,
                                m_ShadowData.normalBiasMax, (uint)index, out shadowRequest.view,
                                out invViewProjection, out shadowRequest.projection,
                                out shadowRequest.deviceProjection, out shadowRequest.splitData
                            );
                            break;
                        case LightType.Spot:
                            HDShadowUtils.ExtractSpotLightData(
                                hdCamera, m_Light.type, spotLightShape, shadowNearPlane, aspectRatio, shapeWidth,
                                shapeHeight, visibleLight, viewportSize, m_ShadowData.normalBiasMax,
                                out shadowRequest.view, out invViewProjection, out shadowRequest.projection,
                                out shadowRequest.deviceProjection, out shadowRequest.splitData
                            );
                            break;
                        case LightType.Directional:
                            Vector4 cullingSphere;
                            float nearPlaneOffset = QualitySettings.shadowNearPlaneOffset;

                            HDShadowUtils.ExtractDirectionalLightData(
                                visibleLight, viewportSize, (uint)index, m_ShadowSettings.cascadeShadowSplitCount,
                                m_ShadowSettings.cascadeShadowSplits, nearPlaneOffset, cullResults, lightIndex,
                                out shadowRequest.view, out invViewProjection, out shadowRequest.projection,
                                out shadowRequest.deviceProjection, out shadowRequest.splitData
                            );

                            cullingSphere = shadowRequest.splitData.cullingSphere;

                            // Camera relative for directional light culling sphere
                            if (ShaderConfig.s_CameraRelativeRendering != 0)
                            {
                                cullingSphere.x -= cameraPos.x;
                                cullingSphere.y -= cameraPos.y;
                                cullingSphere.z -= cameraPos.z;
                            }
                            manager.UpdateCascade(index, cullingSphere, m_ShadowSettings.cascadeShadowBorders[index]);
                            break;
                    }
                }

                // Assign all setting common to every lights
                SetCommonShadowRequestSettings(shadowRequest, cameraPos, invViewProjection, viewportSize, lightIndex);

                manager.UpdateShadowRequest(shadowRequestIndex, shadowRequest);

                // Store the first shadow request id to return it
                if (firstShadowRequestIndex == -1)
                    firstShadowRequestIndex = shadowRequestIndex;

                shadowRequestCount++;
            }

            return firstShadowRequestIndex;
        }

        void SetCommonShadowRequestSettings(HDShadowRequest shadowRequest, Vector3 cameraPos, Matrix4x4 invViewProjection, Vector2 viewportSize, int lightIndex)
        {
            // zBuffer param to reconstruct depth position (for transmission)
            float f = m_Light.range;
            float n = shadowNearPlane;
            shadowRequest.zBufferParam = new Vector4((f-n)/n, 1.0f, (f-n)/n*f, 1.0f/f);
            shadowRequest.viewBias = new Vector4(m_ShadowData.viewBiasMin, m_ShadowData.viewBiasMax, m_ShadowData.viewBiasScale, 2.0f / shadowRequest.projection.m00 / viewportSize.x * 1.4142135623730950488016887242097f);
            shadowRequest.normalBias = new Vector3(m_ShadowData.normalBiasMin, m_ShadowData.normalBiasMax, m_ShadowData.normalBiasScale);
            shadowRequest.flags = 0;
            shadowRequest.flags |= m_ShadowData.sampleBiasScale     ? (int)HDShadowFlag.SampleBiasScale : 0;
            shadowRequest.flags |= m_ShadowData.edgeLeakFixup       ? (int)HDShadowFlag.EdgeLeakFixup : 0;
            shadowRequest.flags |= m_ShadowData.edgeToleranceNormal ? (int)HDShadowFlag.EdgeToleranceNormal : 0;
            shadowRequest.edgeTolerance = m_ShadowData.edgeTolerance;

            // Make light position camera relative:
            // TODO: think about VR (use different camera position for each eye)
            if (ShaderConfig.s_CameraRelativeRendering != 0)
            {
                var translation = Matrix4x4.Translate(cameraPos);
                shadowRequest.view *= translation;
                translation.SetColumn(3, -cameraPos);
                translation[15] = 1.0f;
                invViewProjection = translation * invViewProjection;
            }

            if (m_Light.type == LightType.Directional || (m_Light.type == LightType.Spot && spotLightShape == SpotLightShape.Box))
                shadowRequest.position = new Vector3(shadowRequest.view.m03, shadowRequest.view.m13, shadowRequest.view.m23);
            else
                shadowRequest.position = (ShaderConfig.s_CameraRelativeRendering != 0) ? transform.position - cameraPos : transform.position;

            shadowRequest.shadowToWorld = invViewProjection.transpose;
            shadowRequest.zClip = (m_Light.type != LightType.Directional);
            shadowRequest.lightIndex = lightIndex;
            // We don't allow shadow resize for directional cascade shadow
            if (m_Light.type == LightType.Directional)
            {
                shadowRequest.shadowMapType = ShadowMapType.CascadedDirectional;
            }
            else if (lightTypeExtent == LightTypeExtent.Rectangle)
            {
                shadowRequest.shadowMapType = ShadowMapType.AreaLightAtlas;
            }
            else
            {
                shadowRequest.shadowMapType = ShadowMapType.PunctualAtlas;
            }

            shadowRequest.lightType = (int) m_Light.type;

            // Shadow algorithm parameters
            shadowRequest.shadowSoftness = shadowSoftness / 100f;
            shadowRequest.blockerSampleCount = blockerSampleCount;
            shadowRequest.filterSampleCount = filterSampleCount;
            shadowRequest.minFilterSize = minFilterSize;

            shadowRequest.kernelSize = (uint)kernelSize;
            shadowRequest.lightAngle = (lightAngle * Mathf.PI / 180.0f);
            shadowRequest.maxDepthBias = maxDepthBias;
            // We transform it to base two for faster computation.
            // So e^x = 2^y where y = x * log2 (e)
            const float log2e = 1.44269504089f;
            shadowRequest.evsmParams.x = evsmExponent * log2e;
            shadowRequest.evsmParams.y = evsmLightLeakBias;
            shadowRequest.evsmParams.z = evsmVarianceBias;
            shadowRequest.evsmParams.w = evsmBlurPasses;
        }

#if UNITY_EDITOR
        // We need these old states to make timeline and the animator record the intensity value and the emissive mesh changes (editor-only)
        [System.NonSerialized]
        TimelineWorkaround timelineWorkaround = new TimelineWorkaround();
#endif

        // For light that used the old intensity system we update them
        [System.NonSerialized]
        bool needsIntensityUpdate_1_0 = false;

        // Runtime datas used to compute light intensity
        Light _light;
        Light m_Light
        {
// We force the animation in the editor and in play mode when there is an animator component attached to the light
#if !UNITY_EDITOR
            if (!m_Animated)
                return;
#endif

            Vector3 shape = new Vector3(shapeWidth, m_ShapeHeight, shapeRadius);

            if (legacyLight.enabled != timelineWorkaround.lightEnabled)
            {
                SetEmissiveMeshRendererEnabled(legacyLight.enabled);
                timelineWorkaround.lightEnabled = legacyLight.enabled;
            }

            // Check if the intensity have been changed by the inspector or an animator
            if (timelineWorkaround.oldLossyScale != transform.lossyScale
                || intensity != timelineWorkaround.oldIntensity
                || legacyLight.colorTemperature != timelineWorkaround.oldLightColorTemperature)
            {
                UpdateLightIntensity();
                UpdateAreaLightEmissiveMesh();
                timelineWorkaround.oldLossyScale = transform.lossyScale;
                timelineWorkaround.oldIntensity = intensity;
                timelineWorkaround.oldLightColorTemperature = legacyLight.colorTemperature;
            }

            // Same check for light angle to update intensity using spot angle
            if (legacyLight.type == LightType.Spot && (timelineWorkaround.oldSpotAngle != legacyLight.spotAngle))
            {
                UpdateLightIntensity();
                timelineWorkaround.oldSpotAngle = legacyLight.spotAngle;
            }

            if (legacyLight.color != timelineWorkaround.oldLightColor
                || timelineWorkaround.oldLossyScale != transform.lossyScale
                || displayAreaLightEmissiveMesh != timelineWorkaround.oldDisplayAreaLightEmissiveMesh
                || legacyLight.colorTemperature != timelineWorkaround.oldLightColorTemperature)
            {
                if (_light == null)
                    _light = GetComponent<Light>();
                return _light;
            }
        }

        void SetLightIntensity(float intensity)
        {
            displayLightIntensity = intensity;

            if (lightUnit == LightUnit.Lumen)
            {
                if (lightTypeExtent == LightTypeExtent.Punctual)
                    SetLightIntensityPunctual(intensity);
                else
                    m_Light.intensity = LightUtils.ConvertAreaLightLumenToLuminance(lightTypeExtent, intensity, shapeWidth, shapeHeight);
            }
            else if (lightUnit == LightUnit.Ev100)
            {
                m_Light.intensity = LightUtils.ConvertEvToLuminance(intensity);
            }
            else if ((m_Light.type == LightType.Spot || m_Light.type == LightType.Point) && lightUnit == LightUnit.Lux)
            {
                // Box are local directional light with lux unity without at distance
                if ((m_Light.type == LightType.Spot) && (spotLightShape == SpotLightShape.Box))
                    m_Light.intensity = intensity;
                else
                    m_Light.intensity = LightUtils.ConvertLuxToCandela(intensity, luxAtDistance);
            }
            else
                m_Light.intensity = intensity;

#if UNITY_EDITOR
            m_Light.SetLightDirty(); // Should be apply only to parameter that's affect GI, but make the code cleaner
#endif
            }

            // We don't use the global settings of shadow mask by default
            light.lightShadowCasterMode = LightShadowCasterMode.Everything;

            lightData.constantBias         = 0.15f;
            lightData.normalBias           = 0.75f;
        }

        void OnValidate()
        {
            UpdateBounds();
            DisableCachedShadowSlot();
            m_ShadowMapRenderedSinceLastRequest = false;
        }

        void SetLightIntensityPunctual(float intensity)
        {
            switch (m_Light.type)
            {
                case LightType.Directional:
                    m_Light.intensity = intensity; // Always in lux
                    break;
                case LightType.Point:
                    if (lightUnit == LightUnit.Candela)
                        m_Light.intensity = intensity;
                    else
                        m_Light.intensity = LightUtils.ConvertPointLightLumenToCandela(intensity);
                    break;
                case LightType.Spot:
                    if (lightUnit == LightUnit.Candela)
                    {
                        // When using candela, reflector don't have any effect. Our intensity is candela = lumens/steradian and the user
                        // provide desired value for an angle of 1 steradian.
                        m_Light.intensity = intensity;
                    }
                    else  // lumen
                    {
                        if (enableSpotReflector)
                        {
                            // If reflector is enabled all the lighting from the sphere is focus inside the solid angle of current shape
                            if (spotLightShape == SpotLightShape.Cone)
                            {
                                m_Light.intensity = LightUtils.ConvertSpotLightLumenToCandela(intensity, m_Light.spotAngle * Mathf.Deg2Rad, true);
                            }
                            else if (spotLightShape == SpotLightShape.Pyramid)
                            {
                                float angleA, angleB;
                                LightUtils.CalculateAnglesForPyramid(aspectRatio, m_Light.spotAngle * Mathf.Deg2Rad, out angleA, out angleB);

                                m_Light.intensity = LightUtils.ConvertFrustrumLightLumenToCandela(intensity, angleA, angleB);
                            }
                            else // Box shape, fallback to punctual light.
                            {
                                m_Light.intensity = LightUtils.ConvertPointLightLumenToCandela(intensity);
                            }
                        }
                        else
                        {
                            // No reflector, angle act as occlusion of point light.
                            m_Light.intensity = LightUtils.ConvertPointLightLumenToCandela(intensity);
                        }
                    }
                    break;
            }
        }

        public static bool IsAreaLight(LightTypeExtent lightType)
        {
            return lightType != LightTypeExtent.Punctual;
        }

#if UNITY_EDITOR

        // Force to retrieve color light's m_UseColorTemperature because it's private
        [System.NonSerialized]
        SerializedProperty useColorTemperatureProperty;
        [System.NonSerialized]
        SerializedObject lightSerializedObject;
        public bool useColorTemperature
        {
            get
            {
                if (useColorTemperatureProperty == null)
                {
                    lightSerializedObject = new SerializedObject(m_Light);
                    useColorTemperatureProperty = lightSerializedObject.FindProperty("m_UseColorTemperature");
                }

                lightSerializedObject.Update();

                return useColorTemperatureProperty.boolValue;
            }
        }

        // TODO: There are a lot of old != current checks and assignation in this function, maybe think about using another system ?
        void LateUpdate()
        {
            Vector3 shape = new Vector3(shapeWidth, shapeHeight, shapeRadius);

            // Check if the intensity have been changed by the inspector or an animator
            if (displayLightIntensity != timelineWorkaround.oldDisplayLightIntensity
                || luxAtDistance != timelineWorkaround.oldLuxAtDistance
                || lightTypeExtent != timelineWorkaround.oldLightTypeExtent
                || transform.localScale != timelineWorkaround.oldLocalScale
                || shape != timelineWorkaround.oldShape
                || m_Light.colorTemperature != timelineWorkaround.oldLightColorTemperature)
            {
                RefreshLightIntensity();
                UpdateAreaLightEmissiveMesh();
                timelineWorkaround.oldDisplayLightIntensity = displayLightIntensity;
                timelineWorkaround.oldLuxAtDistance = luxAtDistance;
                timelineWorkaround.oldLocalScale = transform.localScale;
                timelineWorkaround.oldLightTypeExtent = lightTypeExtent;
                timelineWorkaround.oldLightColorTemperature = m_Light.colorTemperature;
                timelineWorkaround.oldShape = shape;
            }

            // Same check for light angle to update intensity using spot angle
            if (m_Light.type == LightType.Spot && (timelineWorkaround.oldSpotAngle != m_Light.spotAngle || timelineWorkaround.oldEnableSpotReflector != enableSpotReflector))
            {
                RefreshLightIntensity();
                timelineWorkaround.oldSpotAngle = m_Light.spotAngle;
                timelineWorkaround.oldEnableSpotReflector = enableSpotReflector;
            }

            if (m_Light.color != timelineWorkaround.oldLightColor
                || transform.localScale != timelineWorkaround.oldLocalScale
                || displayAreaLightEmissiveMesh != timelineWorkaround.oldDisplayAreaLightEmissiveMesh
                || lightTypeExtent != timelineWorkaround.oldLightTypeExtent
                || m_Light.colorTemperature != timelineWorkaround.oldLightColorTemperature
                || lightDimmer != timelineWorkaround.lightDimmer)
            {
                UpdateAreaLightEmissiveMesh();
                timelineWorkaround.lightDimmer = lightDimmer;
                timelineWorkaround.oldLightColor = m_Light.color;
                timelineWorkaround.oldLocalScale = transform.localScale;
                timelineWorkaround.oldDisplayAreaLightEmissiveMesh = displayAreaLightEmissiveMesh;
                timelineWorkaround.oldLightTypeExtent = lightTypeExtent;
                timelineWorkaround.oldLightColorTemperature = m_Light.colorTemperature;
            }
        }

        // The editor can only access displayLightIntensity (because of SerializedProperties) so we update the intensity to get the real value
        void RefreshLightIntensity()
        {
            intensity = displayLightIntensity;
        }

        public static bool IsAreaLight(SerializedProperty lightType)
        {
            return IsAreaLight((LightTypeExtent)lightType.enumValueIndex);
        }

        public void UpdateAreaLightEmissiveMesh()
        {
            bool displayEmissiveMesh = IsAreaLight(lightTypeExtent) && displayAreaLightEmissiveMesh;

            // Ensure that the emissive mesh components are here
            if (displayEmissiveMesh)
            {
                if (emissiveMeshRenderer == null)
                    m_EmissiveMeshRenderer = gameObject.AddComponent<MeshRenderer>();
                if (emissiveMeshFilter == null)
                    m_EmissiveMeshFilter = gameObject.AddComponent<MeshFilter>();
            }
            else // Or remove them if the option is disabled
            {
                if (emissiveMeshRenderer != null)
                    DestroyImmediate(emissiveMeshRenderer);
                if (emissiveMeshFilter != null)
                    DestroyImmediate(emissiveMeshFilter);

                // We don't have anything to do left if the dislay emissive mesh option is disabled
                return;
            }

            Vector3 lightSize;

            // Update light area size from GameObject transform scale if the transform have changed
            // else we update the light size from the shape fields
            if (timelineWorkaround.oldLocalScale != transform.localScale)
                lightSize = transform.localScale;
            else
                lightSize = new Vector3(shapeWidth, shapeHeight, transform.localScale.z);

            if (lightTypeExtent == LightTypeExtent.Tube)
                lightSize.y = k_MinAreaWidth;
            lightSize.z = k_MinAreaWidth;

            lightSize = Vector3.Max(Vector3.one * k_MinAreaWidth, lightSize);
            m_Light.transform.localScale = lightSize;
            m_Light.areaSize = lightSize;

            switch (lightTypeExtent)
            {
                case LightTypeExtent.Rectangle:
                    shapeWidth = lightSize.x;
                    shapeHeight = lightSize.y;
                    break;
                case LightTypeExtent.Tube:
                    shapeWidth = lightSize.x;
                    break;
                default:
                    break;
            }

            // NOTE: When the user duplicates a light in the editor, the material is not duplicated and when changing the properties of one of them (source or duplication)
            // It either overrides both or is overriden. Given that when we duplicate an object the name changes, this approach works. When the name of the game object is then changed again
            // the material is not re-created until one of the light properties is changed again.
            if (emissiveMeshRenderer.sharedMaterial == null || emissiveMeshRenderer.sharedMaterial.name != gameObject.name)
            {
                emissiveMeshRenderer.sharedMaterial = new Material(Shader.Find("HDRP/Unlit"));
                emissiveMeshRenderer.sharedMaterial.SetFloat("_IncludeIndirectLighting", 0.0f);
                emissiveMeshRenderer.sharedMaterial.name = gameObject.name;
            }

            // Update Mesh emissive properties
            emissiveMeshRenderer.sharedMaterial.SetColor("_UnlitColor", Color.black);

            // m_Light.intensity is in luminance which is the value we need for emissive color
            Color value = m_Light.color.linear * m_Light.intensity;
            if (useColorTemperature)
                value *= Mathf.CorrelatedColorTemperatureToRGB(m_Light.colorTemperature);

            value *= lightDimmer;

            emissiveMeshRenderer.sharedMaterial.SetColor("_EmissiveColor", value);

            // Set the cookie (if there is one) and raise or remove the shader feature
            emissiveMeshRenderer.sharedMaterial.SetTexture("_EmissiveColorMap", areaLightCookie);
            CoreUtils.SetKeyword(emissiveMeshRenderer.sharedMaterial, "_EMISSIVE_COLOR_MAP", areaLightCookie != null);
        }

#endif

        public void CopyTo(HDAdditionalLightData data)
        {
#pragma warning disable 618
            data.directionalIntensity = directionalIntensity;
            data.punctualIntensity = punctualIntensity;
            data.areaIntensity = areaIntensity;
#pragma warning restore 618
            data.enableSpotReflector = enableSpotReflector;
            data.luxAtDistance = luxAtDistance;
            data.m_InnerSpotPercent = m_InnerSpotPercent;
            data.lightDimmer = lightDimmer;
            data.volumetricDimmer = volumetricDimmer;
            data.lightUnit = lightUnit;
            data.fadeDistance = fadeDistance;
            data.affectDiffuse = affectDiffuse;
            data.affectSpecular = affectSpecular;
            data.nonLightmappedOnly = nonLightmappedOnly;
            data.lightTypeExtent = lightTypeExtent;
            data.spotLightShape = spotLightShape;
            data.shapeWidth = shapeWidth;
            data.shapeHeight = shapeHeight;
            data.aspectRatio = aspectRatio;
            data.shapeRadius = shapeRadius;
            data.maxSmoothness = maxSmoothness;
            data.applyRangeAttenuation = applyRangeAttenuation;
            data.useOldInspector = useOldInspector;
            data.featuresFoldout = featuresFoldout;
            data.showAdditionalSettings = showAdditionalSettings;
            data.displayLightIntensity = displayLightIntensity;
            data.displayAreaLightEmissiveMesh = displayAreaLightEmissiveMesh;
            data.needsIntensityUpdate_1_0 = needsIntensityUpdate_1_0;

#if UNITY_EDITOR
            data.timelineWorkaround = timelineWorkaround;
#endif
        }

        void SetSpotLightShape(SpotLightShape shape)
        {
            m_SpotLightShape = shape;
            UpdateBounds();
        }

        void UpdateAreaLightBounds()
        {
            m_Light.useShadowMatrixOverride = false;
            m_Light.useBoundingSphereOverride = true;
            m_Light.boundingSphereOverride = new Vector4(0.0f, 0.0f, 0.0f, m_Light.range);
        }

        void UpdateBoxLightBounds()
        {
            m_Light.useShadowMatrixOverride = true;
            m_Light.useBoundingSphereOverride = true;

            // Need to inverse scale because culling != rendering convention apparently
            Matrix4x4 scaleMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));
            m_Light.shadowMatrixOverride = HDShadowUtils.ExtractBoxLightProjectionMatrix(m_Light.range, shapeWidth, shapeHeight, shadowNearPlane) * scaleMatrix;

            // Very conservative bounding sphere taking the diagonal of the shape as the radius
            float diag = new Vector3(shapeWidth * 0.5f, shapeHeight * 0.5f, m_Light.range * 0.5f).magnitude;
            m_Light.boundingSphereOverride = new Vector4(0.0f, 0.0f, m_Light.range * 0.5f, diag);
        }

        void UpdatePyramidLightBounds()
        {
            m_Light.useShadowMatrixOverride = true;
            m_Light.useBoundingSphereOverride = true;

            // Need to inverse scale because culling != rendering convention apparently
            Matrix4x4 scaleMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));
            m_Light.shadowMatrixOverride = HDShadowUtils.ExtractSpotLightProjectionMatrix(m_Light.range, m_Light.spotAngle, shadowNearPlane, aspectRatio, 0.0f) * scaleMatrix;

            // Very conservative bounding sphere taking the diagonal of the shape as the radius
            float diag = new Vector3(shapeWidth * 0.5f, shapeHeight * 0.5f, m_Light.range * 0.5f).magnitude;
            m_Light.boundingSphereOverride = new Vector4(0.0f, 0.0f, m_Light.range * 0.5f, diag);
        }

        void UpdateBounds()
        {
            if (lightTypeExtent == LightTypeExtent.Punctual && m_Light.type == LightType.Spot)
            {
                switch (spotLightShape)
                {
                    case SpotLightShape.Box:
                        UpdateBoxLightBounds();
                        break;
                    case SpotLightShape.Pyramid:
                        UpdatePyramidLightBounds();
                        break;
                    default: // Cone
                        m_Light.useBoundingSphereOverride = false;
                        m_Light.useShadowMatrixOverride = false;
                        break;
                }
            }
            else if (lightTypeExtent == LightTypeExtent.Rectangle || lightTypeExtent == LightTypeExtent.Tube)
            {
                UpdateAreaLightBounds();
            }
            else
            {
                m_Light.useBoundingSphereOverride = false;
                m_Light.useShadowMatrixOverride = false;
            }
        }

        // As we have our own default value, we need to initialize the light intensity correctly
        public static void InitDefaultHDAdditionalLightData(HDAdditionalLightData lightData)
        {
            lightUnit = LightUnit.Lux;
            luxAtDistance = distance;
            intensity = luxIntensity;
        }

        /// <summary>
        /// Set the type of the light.
        /// Note: this will also change the unit of the light if the current one is not supported by the new light type.
        /// </summary>
        /// <param name="type"></param>
        public void SetLightType(HDLightType type)
        {
            switch (type)
            {
                case LightType.Directional:
                    lightData.lightUnit = LightUnit.Lux;
                    lightData.intensity = k_DefaultDirectionalLightIntensity;
                    break;
                case LightType.Rectangle: // Rectangle by default when light is created
                    lightData.lightUnit = LightUnit.Lumen;
                    lightData.intensity = k_DefaultAreaLightIntensity;
                    light.shadows = LightShadows.None;
                    break;
                case LightType.Point:
                case LightType.Spot:
                    lightData.lightUnit = LightUnit.Lumen;
                    lightData.intensity = k_DefaultPunctualLightIntensity;
                    break;
            }

            // Sanity check: lightData.lightTypeExtent is init to LightTypeExtent.Punctual (in case for unknow reasons we recreate additional data on an existing line)
            if (light.type == LightType.Rectangle && lightData.lightTypeExtent == LightTypeExtent.Punctual)
            {
                lightData.lightTypeExtent = LightTypeExtent.Rectangle;
                light.type = LightType.Point; // Same as in HDLightEditor
#if UNITY_EDITOR
                light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
            }

            // We don't use the global settings of shadow mask by default
            light.lightShadowCasterMode = LightShadowCasterMode.Everything;
        }

        void OnValidate()
        {
            UpdateBounds();
        }

        public void OnBeforeSerialize()
        {
            UpdateBounds();
        }

        public void OnAfterDeserialize()
        {
            // Note: the field version is deprecated but we keep it for retro-compatibility reasons, you should use m_Version instead
#pragma warning disable 618
            if (version <= 1.0f)
#pragma warning restore 618
            {
                // Note: We can't access to the light component in OnAfterSerialize as it is not init() yet,
                // so instead we use a boolean to do the upgrade in OnEnable().
                // However OnEnable is not call when the light is disabled, so the HDLightEditor also call
                // the UpgradeLight() code in this case
                needsIntensityUpdate_1_0 = true;
            }
        }

        /// <summary>
        /// Set the light layer and shadow map light layer masks. The feature must be enabled in the HDRP asset in norder to work.
        /// </summary>
        /// <param name="lightLayerMask"></param>
        /// <param name="shadowLightLayerMask"></param>
        public void SetLightLayer(LightLayerEnum lightLayerMask, LightLayerEnum shadowLayerMask)
        {
            // disable the shadow / light layer link
            linkShadowLayers = false;
            legacyLight.renderingLayerMask = LightLayerToRenderingLayerMask((int)shadowLayerMask, (int)legacyLight.renderingLayerMask);
            lightlayersMask = lightLayerMask;
        }

        public void UpgradeLight()
        {
// Disable the warning generated by deprecated fields (areaIntensity, directionalIntensity, ...)
#pragma warning disable 618

        /// <summary>
        /// Set the shadow update mode.
        /// </summary>
        /// <param name="updateMode"></param>
        public void SetShadowUpdateMode(ShadowUpdateMode updateMode) => shadowUpdateMode = updateMode;

        // A bunch of function that changes stuff on the legacy light so users don't have to get the
        // light component which would lead to synchronization problem with ou HD datas.
        
        /// <summary>
        /// Set the range of the light.
        /// </summary>
        /// <param name="range"></param>
        public void SetRange(float range) => legacyLight.range = range;

        /// <summary>
        /// Set the shadow map light layer masks. The feature must be enabled in the HDRP asset in norder to work.
        /// </summary>
        /// <param name="lightLayerMask"></param>
        public void SetShadowLightLayer(LightLayerEnum shadowLayerMask) => legacyLight.renderingLayerMask = LightLayerToRenderingLayerMask((int)shadowLayerMask, (int)legacyLight.renderingLayerMask);

        /// <summary>
        /// Set the light culling mask.
        /// </summary>
        /// <param name="cullingMask"></param>
        public void SetCullingMask(int cullingMask) => legacyLight.cullingMask = cullingMask;

        /// <summary>
        /// Set the light layer shadow cull distances.
        /// </summary>
        /// <param name="layerShadowCullDistances"></param>
        /// <returns></returns>
        public float[] SetLayerShadowCullDistances(float[] layerShadowCullDistances) => legacyLight.layerShadowCullDistances = layerShadowCullDistances;

        /// <summary>
        /// Get the list of supported light units depending on the current light type.
        /// </summary>
        /// <returns></returns>
        public LightUnit[] GetSupportedLightUnits() => GetSupportedLightUnits(legacyLight.type, m_LightTypeExtent, m_SpotLightShape);

        /// <summary>
        /// Set the area light size.
        /// </summary>
        /// <param name="size"></param>
        public void SetAreaLightSize(Vector2 size)
        {
            if (IsAreaLight(lightTypeExtent))
            {
                // ShadowNearPlane have been move to HDRP as default legacy unity clamp it to 0.1 and we need to be able to go below that
                shadowNearPlane = m_Light.shadowNearPlane;
            }
            if (m_Version <= 3)
            {
                m_Light.renderingLayerMask = LightLayerToRenderingLayerMask((int)lightLayers, m_Light.renderingLayerMask);
            }
        }

#endregion

#region Utils

        bool IsValidLightUnitForType(LightType type, LightTypeExtent typeExtent, SpotLightShape spotLightShape, LightUnit unit)
        {
            LightUnit[] allowedUnits = GetSupportedLightUnits(type, typeExtent, spotLightShape);

            return allowedUnits.Any(u => u == unit);
        }

        [System.NonSerialized]
        static Dictionary<int, LightUnit[]>  supportedLightTypeCache = new Dictionary<int, LightUnit[]>();
        static LightUnit[] GetSupportedLightUnits(LightType type, LightTypeExtent typeExtent, SpotLightShape spotLightShape)
        {
            LightUnit[]     supportedTypes;

            // Combine the two light types to access the dictionary
            int cacheKey = ((int)type & 0xFF) << 0;
            cacheKey |= ((int)typeExtent & 0xFF) << 8;
            cacheKey |= ((int)spotLightShape & 0xFF) << 16;

            // We cache the result once they are computed, it avoid garbage generated by Enum.GetValues and Linq.
            if (supportedLightTypeCache.TryGetValue(cacheKey, out supportedTypes))
                return supportedTypes;

            if (IsAreaLight(typeExtent))
                supportedTypes = Enum.GetValues(typeof(AreaLightUnit)).Cast<LightUnit>().ToArray();
            else if (type == LightType.Directional || (type == LightType.Spot && spotLightShape == SpotLightShape.Box))
                supportedTypes = Enum.GetValues(typeof(DirectionalLightUnit)).Cast<LightUnit>().ToArray();
            else
                supportedTypes = Enum.GetValues(typeof(PunctualLightUnit)).Cast<LightUnit>().ToArray();
            
            supportedLightTypeCache[cacheKey] = supportedTypes;

            return supportedTypes;
        }

        string GetLightTypeName()
        {
            if (IsAreaLight(lightTypeExtent))
                return lightTypeExtent.ToString();
            else
                return legacyLight.type.ToString();
        }

            m_Version = currentVersion;
            version = currentVersion;

#pragma warning restore 0618
        }

        /// <summary>
        /// Converts a light layer into a rendering layer mask.
        ///
        /// Light layer is stored in the first 8 bit of the rendering layer mask.
        ///
        /// NOTE: light layers are obsolete, use directly renderingLayerMask.
        /// </summary>
        /// <param name="lightLayer">The light layer, only the first 8 bits will be used.</param>
        /// <param name="renderingLayerMask">Current renderingLayerMask, only the last 24 bits will be used.</param>
        /// <returns></returns>
        internal static int LightLayerToRenderingLayerMask(int lightLayer, int renderingLayerMask)
        {
            var renderingLayerMask_u32 = (uint)renderingLayerMask;
            var lightLayer_u8 = (byte)lightLayer;
            return (int)((renderingLayerMask_u32 & 0xFFFFFF00) | lightLayer_u8);
        }

        /// <summary>
        /// Converts a renderingLayerMask into a lightLayer.
        ///
        /// NOTE: light layers are obsolete, use directly renderingLayerMask.
        /// </summary>
        /// <param name="renderingLayerMask"></param>
        /// <returns></returns>
        internal static int RenderingLayerMaskToLightLayer(int renderingLayerMask)
            => (byte)renderingLayerMask;
    }
}
