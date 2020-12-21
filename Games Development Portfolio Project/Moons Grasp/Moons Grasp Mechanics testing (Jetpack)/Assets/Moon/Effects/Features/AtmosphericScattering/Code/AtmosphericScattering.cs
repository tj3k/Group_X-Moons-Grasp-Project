﻿using UnityEngine;

[ExecuteInEditMode]
public class AtmosphericScattering : MonoBehaviour {
	public enum OcclusionDownscale { x1 = 1, x2 = 2, x4 = 4 }
	public enum OcclusionSamples { x64 = 0, x164 = 1, x244 = 2 }
	public enum ScatterDebugMode { None, Scattering, Occlusion, OccludedScattering, Rayleigh, Mie, Height }
	public enum DepthTexture { Enable, Disable, Ignore }

	[Header("World Components")]
	public Gradient	worldRayleighColorRamp				= null;
	public float	worldRayleighColorIntensity			= 1f;
	public float	worldRayleighDensity				= 10f;
	public float	worldRayleighExtinctionFactor		= 1.1f;
	public float	worldRayleighIndirectScatter		= 0.33f;
	public Gradient	worldMieColorRamp					= null;
	public float	worldMieColorIntensity				= 1f;
	public float	worldMieDensity						= 15f;
	public float	worldMieExtinctionFactor			= 0f;
	public float	worldMiePhaseAnisotropy				= 0.9f;
	public float	worldNearScatterPush				= 0f;
	public float	worldNormalDistance					= 1000f;

	[Header("Height Components")]
	public Color	heightRayleighColor					= Color.white;
	public float	heightRayleighIntensity				= 1f;
	public float	heightRayleighDensity				= 10f;
	public float	heightMieDensity					= 0f;
	public float	heightExtinctionFactor				= 1.1f;
	public float	heightSeaLevel						= 0f;
	public float	heightDistance						= 50f;
	public Vector3	heightPlaneShift					= Vector3.zero;
	public float	heightNearScatterPush				= 0f;
	public float	heightNormalDistance				= 1000f;

	[Header("Sky Dome")]
	public Vector3		skyDomeScale					= new Vector3(1f, 0.1f, 1f);
	public Vector3		skyDomeRotation					= Vector3.zero;
	public Transform	skyDomeTrackedYawRotation		= null;
	public bool			skyDomeVerticalFlip				= false;
	public Cubemap		skyDomeCube						= null;
	public float		skyDomeExposure					= 1f;
	public Color		skyDomeTint						= Color.white;
	[HideInInspector] public Vector3 skyDomeOffset		= Vector3.zero;

	[Header("Scatter Occlusion")]
	public bool					useOcclusion			= false;
	public float				occlusionBias			= 0f;
	public float				occlusionBiasIndirect	= 0.6f;
	public float				occlusionBiasClouds		= 0.3f;
	public OcclusionDownscale	occlusionDownscale		= OcclusionDownscale.x2;
	public OcclusionSamples		occlusionSamples		= OcclusionSamples.x64;
	public bool					occlusionDepthFixup		= true;
	public float				occlusionDepthThreshold	= 25f;
	public bool					occlusionFullSky		= false;
	public float				occlusionBiasSkyRayleigh= 0.2f;
	public float				occlusionBiasSkyMie		= 0.4f;
	
	[Header("Other")]
	public float			worldScaleExponent			= 1.0f;
	public bool				forcePerPixel				= false;
	public bool				forcePostEffect				= false;
	[Tooltip("Soft clouds need depth values. Ignore means externally controlled.")]
	public DepthTexture		depthTexture				= DepthTexture.Enable;
	public ScatterDebugMode	debugMode					= ScatterDebugMode.None;
	
	[HideInInspector] public Shader occlusionShader;

	bool			m_isAwake;

	Camera			m_currentCamera;
	Material		m_occlusionMaterial;
	
	Camera.CameraCallback m_precullCallback;

	public static AtmosphericScattering instance { get; private set; }

#if UNITY_EDITOR
	//[UnityEditor.MenuItem("Tools/Fix Camera Callback Proxies")]
	static void FixCallbacks() {
		Mesh m = null;
		foreach(var c in Resources.FindObjectsOfTypeAll<Camera>()) {
			if(c.tag != "MainCamera")
				continue;

			if(!string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(c.gameObject)))
				continue;

			var a = c.GetComponent<AtmosphericScattering>();
			if(a) {
				var mf = c.GetComponent<MeshFilter>();
				if(mf)
					Object.DestroyImmediate(mf.sharedMesh);
				else
					mf = c.gameObject.AddComponent<MeshFilter>();

				if(m == null) {
					m = new Mesh();
					m.name = "Callback Proxy";
#if WAITING_FOR_ENGINE_REGRESSION_FIX
					m.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
					m.SetTriangles((int[])null, 0);
#else
					m.vertices = new[] { new Vector3(0f, -100f, 0f) };
					m.SetIndices(new[] { 0 }, MeshTopology.Points, 0);
					m.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
#endif
				}

				mf.sharedMesh = m;
			}
		}
	}
#endif

	void Awake() {
		var mf = GetComponent<MeshFilter>();
		if(!mf || !mf.sharedMesh) {
			if(!mf)
				mf = gameObject.AddComponent<MeshFilter>();
			mf.sharedMesh = new Mesh();
			mf.sharedMesh.name = "Callbackproxy";
#if WAITING_FOR_ENGINE_REGRESSION_FIX
			mf.sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
			mf.sharedMesh.SetTriangles((int[])null, 0);
#else
			mf.sharedMesh.vertices = new[] { new Vector3(0f, -100f, 0f) };
			mf.sharedMesh.SetIndices(new[] { 0 }, MeshTopology.Points, 0);
			mf.sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
#endif
		}

		if(!GetComponent<MeshRenderer>()) {
			var mr = gameObject.AddComponent<MeshRenderer>();
			mr.receiveShadows = false;
			mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
		}

		if(occlusionShader == null)
			occlusionShader = Shader.Find("Hidden/AtmosphericScattering_Occlusion");

		m_occlusionMaterial = new Material(occlusionShader);
		m_occlusionMaterial.hideFlags = HideFlags.HideAndDontSave;

		if(worldRayleighColorRamp == null) {
			worldRayleighColorRamp = new Gradient();
			worldRayleighColorRamp.SetKeys(
				new[]{ new GradientColorKey(new Color(0.3f, 0.4f, 0.6f), 0f), new GradientColorKey(new Color(0.5f, 0.6f, 0.8f), 1f) },
			new[]{ new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
			);
		}
		if(worldMieColorRamp == null) {
			worldMieColorRamp = new Gradient();
			worldMieColorRamp.SetKeys(
				new[]{ new GradientColorKey(new Color(0.95f, 0.75f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.9f, 8.0f), 1f) },
			new[]{ new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
			);
		}

		m_precullCallback = new Camera.CameraCallback(CBOnPreCull);

		m_isAwake = true;
	}


	void OnEnable() {
		Camera.onPreCull += m_precullCallback;

		if(!m_isAwake)
			return;

		UpdateKeywords(true);
		UpdateStaticUniforms();
		UpdateDynamicUniforms();

		m_currentCamera = null;

		if(instance && instance != this)
			Debug.LogErrorFormat("Unexpected: AtmosphericScattering.instance already set (to: {0}). Still overriding with: {1}.", instance.name, name);
		
		instance = this;
	}

	void OnDisable() {
		Camera.onPreCull -= m_precullCallback;

		UpdateKeywords(false);

		if(instance != this) {
			if(instance)
				Debug.LogErrorFormat("Unexpected: AtmosphericScattering.instance set to: {0}, not to: {1}. Leaving alone.", instance.name, name);
		} else {
			instance = null;
		}
	}

	void UpdateKeywords(bool enable) {
		Shader.DisableKeyword("ATMOSPHERICS");
		Shader.DisableKeyword("ATMOSPHERICS_PER_PIXEL");
		Shader.DisableKeyword("ATMOSPHERICS_OCCLUSION");
		Shader.DisableKeyword("ATMOSPHERICS_OCCLUSION_FULLSKY");
		Shader.DisableKeyword("ATMOSPHERICS_OCCLUSION_EDGE_FIXUP");
		Shader.DisableKeyword("ATMOSPHERICS_SUNRAYS");
		Shader.DisableKeyword("ATMOSPHERICS_DEBUG");

		if(enable) {
			if (!forcePerPixel)
				Shader.EnableKeyword("ATMOSPHERICS");
			else
				Shader.EnableKeyword("ATMOSPHERICS_PER_PIXEL");
			
			if(useOcclusion) {
				Shader.EnableKeyword("ATMOSPHERICS_OCCLUSION");

				if(occlusionDepthFixup && occlusionDownscale != OcclusionDownscale.x1)
					Shader.EnableKeyword("ATMOSPHERICS_OCCLUSION_EDGE_FIXUP");

				if(occlusionFullSky)
					Shader.EnableKeyword("ATMOSPHERICS_OCCLUSION_FULLSKY");
			}

			if(debugMode != ScatterDebugMode.None)
				Shader.EnableKeyword("ATMOSPHERICS_DEBUG");
		}
	}

	public void OnValidate() {
		if(!m_isAwake)
			return;

		occlusionBias = Mathf.Clamp01(occlusionBias);
		occlusionBiasIndirect = Mathf.Clamp01(occlusionBiasIndirect);
		occlusionBiasClouds = Mathf.Clamp01(occlusionBiasClouds);
		occlusionBiasSkyRayleigh = Mathf.Clamp01(occlusionBiasSkyRayleigh);
		occlusionBiasSkyMie = Mathf.Clamp01(occlusionBiasSkyMie);
		worldScaleExponent = Mathf.Clamp(worldScaleExponent, 1f, 2f);
		worldNormalDistance = Mathf.Clamp(worldNormalDistance, 1f, 10000f);
		worldNearScatterPush = Mathf.Clamp(worldNearScatterPush, -200f, 300f);
		worldRayleighDensity = Mathf.Clamp(worldRayleighDensity, 0, 1000f);
		worldMieDensity = Mathf.Clamp(worldMieDensity, 0f, 1000f);
		worldRayleighIndirectScatter = Mathf.Clamp(worldRayleighIndirectScatter, 0, 1f);

		heightNormalDistance = Mathf.Clamp(heightNormalDistance, 1f, 10000f);
		heightNearScatterPush = Mathf.Clamp(heightNearScatterPush, -200f, 300f);
		heightRayleighDensity = Mathf.Clamp(heightRayleighDensity, 0, 1000f);
		heightMieDensity = Mathf.Clamp(heightMieDensity, 0, 1000f);
		
		worldMiePhaseAnisotropy = Mathf.Clamp01(worldMiePhaseAnisotropy);
		skyDomeExposure = Mathf.Clamp(skyDomeExposure, 0f, 8f);

		if(instance == this) {
			OnDisable();
			OnEnable();
		}

#if UNITY_EDITOR
		UnityEditor.SceneView.RepaintAll();
#endif
	}

	void CBOnPreCull(Camera cam) {}
	void OnWillRenderObject() {
	//void CBOnPreCull(Camera cam) {
		var cam = Camera.current;
		//Debug.LogFormat("PreCull: {0} / {1}", cam.name, Camera.current ? Camera.current.name : "<none>");
		if(!m_isAwake)
			return;

		// Don't do recursive occlusion rendering (should probably disable
		// occlusion on nested cameras)
		if(m_currentCamera)
			return;

		var activeSun = AtmosphericScatteringSun.instance;
		if(!activeSun) {
			// When there's no primary light, mie scattering and occlusion will be disabled, so there's
			// nothing for us to update.
			UpdateDynamicUniforms();
			return;
		}

		m_currentCamera = cam /*Camera.current*/;

		if((SystemInfo.graphicsShaderLevel >= 40 || depthTexture == DepthTexture.Enable) && m_currentCamera.depthTextureMode == DepthTextureMode.None)
			m_currentCamera.depthTextureMode = DepthTextureMode.Depth;
		else if(depthTexture == DepthTexture.Disable && m_currentCamera.depthTextureMode != DepthTextureMode.None)
			m_currentCamera.depthTextureMode = DepthTextureMode.None;

// TEMP TO ALLOW KEYFRAMED
UpdateStaticUniforms();

		UpdateDynamicUniforms();

		if(useOcclusion) {
			var camRgt = m_currentCamera.transform.right;
			var camUp = m_currentCamera.transform.up;
			var camFwd = m_currentCamera.transform.forward;
				
			var dy = Mathf.Tan(m_currentCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
			var dx = dy * m_currentCamera.aspect;
				
			var vpCenter = camFwd * m_currentCamera.farClipPlane;
			var vpRight = camRgt * dx * m_currentCamera.farClipPlane;
			var vpUp = camUp * dy * m_currentCamera.farClipPlane;

			m_occlusionMaterial.SetVector("u_CameraPosition", m_currentCamera.transform.position);
			m_occlusionMaterial.SetVector("u_ViewportCorner", vpCenter - vpRight - vpUp);
			m_occlusionMaterial.SetVector("u_ViewportRight", vpRight * 2f);
			m_occlusionMaterial.SetVector("u_ViewportUp", vpUp * 2f);
			var farDist = m_currentCamera ? m_currentCamera.farClipPlane : 1000f;
			var refDist = (Mathf.Min(farDist, QualitySettings.shadowDistance) - 1f) / farDist;
			m_occlusionMaterial.SetFloat("u_OcclusionSkyRefDistance", refDist);

			var srcRect = m_currentCamera.pixelRect;
			var downscale = 1f / (float)(int)occlusionDownscale;
			var occWidth = Mathf.RoundToInt(srcRect.width * downscale);
			var occHeight = Mathf.RoundToInt(srcRect.height * downscale);
			var occlusionId = Shader.PropertyToID("u_OcclusionTexture");

			var occlusionCmdBeforeScreenSpace = activeSun.occlusionCmdBeforeScreenSpace;
			occlusionCmdBeforeScreenSpace.Clear(); 
			occlusionCmdBeforeScreenSpace.GetTemporaryRT(occlusionId, occWidth, occHeight, 0, FilterMode.Bilinear, RenderTextureFormat.R8, RenderTextureReadWrite.sRGB);
			occlusionCmdBeforeScreenSpace.Blit(
				null, 
				occlusionId,
				m_occlusionMaterial,
				(int)occlusionSamples
			);
			occlusionCmdBeforeScreenSpace.SetGlobalTexture(occlusionId, occlusionId);
		}
	}

	void OnRenderObject() {
		//Debug.LogFormat("PreCull: {0} / {1} / {2}", "_", Camera.current ? Camera.current.name : "<none>", m_currentCamera ? m_currentCamera.name : "<none>");

		if(m_currentCamera == Camera.current)
			m_currentCamera = null;
	}

	void UpdateStaticUniforms() {
		Shader.SetGlobalVector("u_SkyDomeOffset", skyDomeOffset);
		Shader.SetGlobalVector("u_SkyDomeScale", skyDomeScale);
		Shader.SetGlobalTexture("u_SkyDomeCube", skyDomeCube);
		Shader.SetGlobalFloat("u_SkyDomeExposure", skyDomeExposure);
		Shader.SetGlobalColor("u_SkyDomeTint", skyDomeTint);

		Shader.SetGlobalFloat("u_ShadowBias", useOcclusion ? occlusionBias : 1f);
		Shader.SetGlobalFloat("u_ShadowBiasIndirect", useOcclusion ? occlusionBiasIndirect : 1f);
		Shader.SetGlobalFloat("u_ShadowBiasClouds", useOcclusion ? occlusionBiasClouds : 1f);
		Shader.SetGlobalVector("u_ShadowBiasSkyRayleighMie", useOcclusion ? new Vector4(occlusionBiasSkyRayleigh, occlusionBiasSkyMie, 0f, 0f) : Vector4.zero);
		Shader.SetGlobalFloat("u_OcclusionDepthThreshold", occlusionDepthThreshold);

		Shader.SetGlobalFloat("u_WorldScaleExponent", worldScaleExponent);
		
		Shader.SetGlobalFloat("u_WorldNormalDistanceRcp", 1f/worldNormalDistance);
		Shader.SetGlobalFloat("u_WorldNearScatterPush", -Mathf.Pow(Mathf.Abs(worldNearScatterPush), worldScaleExponent) * Mathf.Sign(worldNearScatterPush));
		
		Shader.SetGlobalFloat("u_WorldRayleighDensity", -worldRayleighDensity / 100000f);
		Shader.SetGlobalFloat("u_MiePhaseAnisotropy", worldMiePhaseAnisotropy);
		Shader.SetGlobalVector("u_RayleighInScatterPct", new Vector4(1f - worldRayleighIndirectScatter, worldRayleighIndirectScatter, 0f, 0f));
		
		Shader.SetGlobalFloat("u_HeightNormalDistanceRcp", 1f/heightNormalDistance);
		Shader.SetGlobalFloat("u_HeightNearScatterPush", -Mathf.Pow(Mathf.Abs(heightNearScatterPush), worldScaleExponent) * Mathf.Sign(heightNearScatterPush));
		Shader.SetGlobalFloat("u_HeightRayleighDensity", -heightRayleighDensity / 100000f);
		
		Shader.SetGlobalFloat("u_HeightSeaLevel", heightSeaLevel);
		Shader.SetGlobalFloat("u_HeightDistanceRcp", 1f/heightDistance);
		Shader.SetGlobalVector("u_HeightPlaneShift", heightPlaneShift);
		Shader.SetGlobalVector("u_HeightRayleighColor", (Vector4)heightRayleighColor * heightRayleighIntensity);
		Shader.SetGlobalFloat("u_HeightExtinctionFactor", heightExtinctionFactor);
		Shader.SetGlobalFloat("u_RayleighExtinctionFactor", worldRayleighExtinctionFactor);
		Shader.SetGlobalFloat("u_MieExtinctionFactor", worldMieExtinctionFactor);
		
		var rayleighColorM20 = worldRayleighColorRamp.Evaluate(0.00f);
		var rayleighColorM10 = worldRayleighColorRamp.Evaluate(0.25f);
		var rayleighColorO00 = worldRayleighColorRamp.Evaluate(0.50f);
		var rayleighColorP10 = worldRayleighColorRamp.Evaluate(0.75f);
		var rayleighColorP20 = worldRayleighColorRamp.Evaluate(1.00f);
		
		var mieColorM20 = worldMieColorRamp.Evaluate(0.00f);
		var mieColorO00 = worldMieColorRamp.Evaluate(0.50f);
		var mieColorP20 = worldMieColorRamp.Evaluate(1.00f);
		
		Shader.SetGlobalVector("u_RayleighColorM20", (Vector4)rayleighColorM20 * worldRayleighColorIntensity);
		Shader.SetGlobalVector("u_RayleighColorM10", (Vector4)rayleighColorM10 * worldRayleighColorIntensity);
		Shader.SetGlobalVector("u_RayleighColorO00", (Vector4)rayleighColorO00 * worldRayleighColorIntensity);
		Shader.SetGlobalVector("u_RayleighColorP10", (Vector4)rayleighColorP10 * worldRayleighColorIntensity);
		Shader.SetGlobalVector("u_RayleighColorP20", (Vector4)rayleighColorP20 * worldRayleighColorIntensity);
		
		Shader.SetGlobalVector("u_MieColorM20", (Vector4)mieColorM20 * worldMieColorIntensity);
		Shader.SetGlobalVector("u_MieColorO00", (Vector4)mieColorO00 * worldMieColorIntensity);
		Shader.SetGlobalVector("u_MieColorP20", (Vector4)mieColorP20 * worldMieColorIntensity);

		Shader.SetGlobalFloat("u_AtmosphericsDebugMode", (int)debugMode);
	}

	void UpdateDynamicUniforms() {
		var activeSun = AtmosphericScatteringSun.instance;
		bool hasSun = !!activeSun;

		var trackedYaw = skyDomeTrackedYawRotation ? skyDomeTrackedYawRotation.eulerAngles.y : 0f;
		Shader.SetGlobalMatrix("u_SkyDomeRotation",
           Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(skyDomeRotation.x, 0f, 0f), Vector3.one)
           * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, skyDomeRotation.y - trackedYaw, 0f), Vector3.one)
           * Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, skyDomeVerticalFlip ? -1f : 1f, 1f))                   
		);

		Shader.SetGlobalVector("u_SunDirection", hasSun ? -activeSun.transform.forward : Vector3.down);	
		Shader.SetGlobalFloat("u_WorldMieDensity", hasSun ? -worldMieDensity / 100000f : 0f);
		Shader.SetGlobalFloat("u_HeightMieDensity", hasSun ? -heightMieDensity / 100000f : 0f);

		var pixelRect = m_currentCamera ? m_currentCamera.pixelRect : new Rect(0f, 0f, Screen.width, Screen.height);
		var scale = (float)(int)occlusionDownscale;
		var depthTextureScaledTexelSize = new Vector4(scale / pixelRect.width, scale / pixelRect.height, -scale / pixelRect.width, -scale / pixelRect.height);
		Shader.SetGlobalVector("u_DepthTextureScaledTexelSize", depthTextureScaledTexelSize);
	}
}