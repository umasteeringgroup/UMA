using UnityEngine;
using System.Collections;
using UnityStandardAssets.ImageEffects;

[ExecuteInEditMode]
[RequireComponent (typeof(Camera))]
[AddComponentMenu ("Image Effects/Color Adjustments/Tonemapping LOG")]
public class TonemappingLog : PostEffectsBase {
	
	public bool enableAdaptive = false;
	public bool debugClamp = false;

	// adaptive parameters
	public float middleGrey = 0.18f;
	public float adaptionSpeed = 1.5f;

	public float adaptiveMin = -3.0f;
	public float adaptiveMax =  3.0f;

	//logOut = logMid + log2(linearIn / linearMid)  / DRinStops
	public float logMid = .5f;
	public float linearMid = .18f;
	public float dynamicRange = 12.0f;

	public AnimationCurve remapCurve = new AnimationCurve(new Keyframe(0, 0, 1.0f, 1.0f), new Keyframe(1, 1, 1.0f, 1.0f));
	private Texture2D curveTex = null;

	public Texture2D lutTex = null;
	public Texture3D converted3DLut = null;
	private string lutTexName;
	
	// usual & internal stuff
	public Shader tonemapperLog = null;
	public bool  validRenderTextureFormat = true;
	private Material tonemapMaterial = null;
	private RenderTexture rt = null;
	private RenderTextureFormat rtFormat =  RenderTextureFormat.ARGBHalf;

	private int curveLen = 256;
	private float [] curveData;

	public override bool CheckResources () {
		 CheckSupport (false, true);
	
		tonemapMaterial = CheckShaderAndCreateMaterial(tonemapperLog, tonemapMaterial);

		if (!curveTex)
		{
			curveTex = new Texture2D(256, 1, TextureFormat.ARGB32, false, true);
			curveTex.filterMode = FilterMode.Bilinear;
			curveTex.wrapMode = TextureWrapMode.Clamp;
			curveTex.hideFlags = HideFlags.DontSave;
		}

		if (!isSupported)
			ReportAutoDisable ();
		return isSupported;
	}

	public void UpdateCurve()
	{
		// initiailize data
		curveData = new float[curveLen];
		for (int i = 0; i < curveLen; i++)
		{
			float t = (float)(i) / (float)(curveLen - 1);
			curveData[i] = t;
		}

		if (remapCurve != null)
		{
			if (remapCurve.keys.Length < 1)
				remapCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));

			float range = 1.0f;
			if (remapCurve.length > 0)
				range = remapCurve[remapCurve.length - 1].time;

			for (int i = 0; i < curveLen; i++)
			{
				float t = (float)(i) / (float)(curveLen - 1);
				float c = remapCurve.Evaluate(t * range);
				curveData[i] = c;
			}
		}

		{
			for (int i = 0; i < 256; i++ )
			{
				float c = curveData[i];
				curveTex.SetPixel(i, 0, new Color(c, c, c));
			}
			curveTex.Apply();
		}
	}

	public bool ValidDimensions(Texture2D tex2d)
	{
		if (!tex2d) return false;
		int h = tex2d.height;
		if (h != Mathf.FloorToInt(Mathf.Sqrt(tex2d.width)))
		{
			return false;
		}
		return true;
	}

	public void SetIdentityLut()
	{
		int dim = 16;
		Color[] newC = new Color[dim * dim * dim];
		float oneOverDim = 1.0f / (1.0f * dim - 1.0f);

		for (int i = 0; i < dim; i++)
		{
			for (int j = 0; j < dim; j++)
			{
				for (int k = 0; k < dim; k++)
				{
					newC[i + (j * dim) + (k * dim * dim)] = new Color((i * 1.0f) * oneOverDim, (j * 1.0f) * oneOverDim, (k * 1.0f) * oneOverDim, 1.0f);
				}
			}
		}

		if (converted3DLut)
			DestroyImmediate(converted3DLut);
		converted3DLut = new Texture3D(dim, dim, dim, TextureFormat.ARGB32, false);
		converted3DLut.SetPixels(newC);
		converted3DLut.Apply();
	}

	public void Convert(Texture2D temp2DTex)
	{
		// conversion fun: the given 2D texture needs to be of the format
		//  w * h, wheras h is the 'depth' (or 3d dimension 'dim') and w = dim * dim

		if (temp2DTex)
		{
			int dim = temp2DTex.width * temp2DTex.height;
			dim = temp2DTex.height;

			if (!ValidDimensions(temp2DTex))
			{
				Debug.LogWarning("The given 2D texture " + temp2DTex.name + " cannot be used as a 3D LUT.");
				//basedOnTempTex = "";
				return;
			}

			Color[] c = temp2DTex.GetPixels();
			Color[] newC = new Color[c.Length];

			for (int i = 0; i < dim; i++)
			{
				for (int j = 0; j < dim; j++)
				{
					for (int k = 0; k < dim; k++)
					{
						int j_ = dim - j - 1;
						newC[i + (j * dim) + (k * dim * dim)] = c[k * dim + i + j_ * dim * dim];
					}
				}
			}

			if (converted3DLut)
				DestroyImmediate(converted3DLut);
			converted3DLut = new Texture3D(dim, dim, dim, TextureFormat.ARGB32, false);
			converted3DLut.SetPixels(newC);
			converted3DLut.Apply();
			lutTexName = temp2DTex.name;
		}
		else
		{
			// error, something went terribly wrong
			//Debug.LogError("Couldn't color correct with 3D LUT texture. Image Effect will be disabled.");
			SetIdentityLut();
			lutTexName = "";
		}
	}
	

	void OnDisable () {
		if (rt) {
			DestroyImmediate (rt);
			rt = null;
		}
		if (tonemapMaterial) {
			DestroyImmediate (tonemapMaterial);
			tonemapMaterial = null;
		}

		if (curveTex)
		{
			DestroyImmediate(curveTex);
			curveTex = null;
		}

		if (converted3DLut)
		{
			DestroyImmediate(converted3DLut);
			converted3DLut = null;
		}
	}
	
	bool CreateInternalRenderTexture () {
		 if (rt) {
			return false;
		}
		rtFormat = SystemInfo.SupportsRenderTextureFormat (RenderTextureFormat.RGHalf) ? RenderTextureFormat.RGHalf : RenderTextureFormat.ARGBHalf;
		rt = new RenderTexture(1,1, 0, rtFormat);
		rt.hideFlags = HideFlags.DontSave;
		return true;
	}
	
	// attribute indicates that the image filter chain will continue in LDR
	[ImageEffectTransformsToLDR]
	void OnRenderImage (RenderTexture source, RenderTexture destination) {
		if (CheckResources() == false) {
			Graphics.Blit (source, destination);
			return;
		}

		UpdateCurve();

		if (converted3DLut == null || (lutTex != null && lutTex.name != lutTexName))
		{
			Convert(lutTex);
			//Debug.Log("Building tex: " + lutTexName + "\n");
		}

		#if UNITY_EDITOR
		validRenderTextureFormat = true;
		if (source.format != RenderTextureFormat.ARGBHalf) {
			validRenderTextureFormat = false;
		}
		#endif
		
		bool  freshlyBrewedInternalRt = CreateInternalRenderTexture (); // this retrieves rtFormat, so should happen before rt allocations

		int srcSize = source.width < source.height ? source.width : source.height;

		int adaptiveSize = 1;
		while (adaptiveSize * 2 < srcSize)
		{
			adaptiveSize *= 2;
		}

		RenderTexture rtSquared = RenderTexture.GetTemporary((int)adaptiveSize, (int)adaptiveSize, 0, rtFormat);
		
		Graphics.Blit(source, rtSquared);
		
		int downsample = (int) Mathf.Log(rtSquared.width * 1.0f, 2);
		
		int div = 2;
		RenderTexture[] rts = new RenderTexture[downsample];
		for (int i = 0; i < downsample; i++) {
			rts[i] = RenderTexture.GetTemporary(rtSquared.width / div, rtSquared.width / div, 0, rtFormat);
			div *= 2;
		}

		// downsample pyramid
		var lumRt= rts[downsample-1];
		Graphics.Blit(rtSquared, rts[0], tonemapMaterial, 1);
		if (true) {
			for(int i = 0; i < downsample-1; i++) {
				Graphics.Blit(rts[i], rts[i+1]);
				lumRt = rts[i+1];
			}
		}
		
		// we have the needed values, let's apply adaptive tonemapping
		adaptionSpeed = adaptionSpeed < 0.001f ? 0.001f : adaptionSpeed;
		tonemapMaterial.SetFloat ("_AdaptionSpeed", adaptionSpeed);

		rt.MarkRestoreExpected(); // keeping luminance values between frames, RT restore expected
		
		#if UNITY_EDITOR
			if (Application.isPlaying && !freshlyBrewedInternalRt)
				Graphics.Blit (lumRt, rt, tonemapMaterial, 2);
			else
				Graphics.Blit (lumRt, rt, tonemapMaterial, 3);
		#else
			Graphics.Blit (lumRt, rt, tonemapMaterial, freshlyBrewedInternalRt ? 3 : 2);
		#endif

		// lut data
		int lutSize = converted3DLut.width;
		converted3DLut.wrapMode = TextureWrapMode.Clamp;
		tonemapMaterial.SetFloat("_Scale", (lutSize - 1) / (1.0f * lutSize));
		tonemapMaterial.SetFloat("_Offset", 1.0f / (2.0f * lutSize));
		tonemapMaterial.SetTexture("_ClutTex", converted3DLut);


		// log data
		middleGrey = middleGrey < 0.001f ? 0.001f : middleGrey;
		tonemapMaterial.SetFloat("_HdrParams", middleGrey);
		tonemapMaterial.SetTexture ("_SmallTex", rt);

		tonemapMaterial.SetFloat("_AdaptiveMin", Mathf.Pow(2.0f, adaptiveMin));
		tonemapMaterial.SetFloat("_AdaptiveMax", Mathf.Pow(2.0f, adaptiveMax));

		//logOut = logMid + log2(linearIn / linearMid)  / DRinStops
		tonemapMaterial.SetFloat("_LogMid", logMid);
		tonemapMaterial.SetFloat("_LinearMid", linearMid);
		tonemapMaterial.SetFloat("_DynamicRange", dynamicRange);
		
		tonemapMaterial.SetFloat("_AdaptionEnabled", enableAdaptive ? 1.0f : 0.0f);
		tonemapMaterial.SetTexture("_Curve", curveTex);

		if (debugClamp)
		{
			Graphics.Blit(source, destination, tonemapMaterial, 4);
		}
		else
		{
			Graphics.Blit (source, destination, tonemapMaterial, 0);
		}
		// cleanup for adaptive
		
		for(int i = 0; i < downsample; i++) {
			RenderTexture.ReleaseTemporary (rts[i]);
		}
		RenderTexture.ReleaseTemporary (rtSquared);
	}
}
