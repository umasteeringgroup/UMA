using UnityEngine;

namespace UMA.Examples
{
	[ExecuteInEditMode]
	public class BRDFLookupTexture : MonoBehaviour
	{
		public float intensity = 1.0f;
		
		public float diffuseIntensity = 1.0f;
		public Color keyColor = ColorRGB (188, 158, 118);
		public Color fillColor = ColorRGB (86, 91, 108);
		public Color backColor = ColorRGB (44, 54, 57);
		public float wrapAround = 0.0f;
		public float metalic = 0.0f;
		
		public float specularIntensity = 1.0f;
		public float specularShininess = 0.078125f;
		
		public float translucency = 0.0f; // skin
		public Color translucentColor = ColorRGB (255, 82, 82);
		
		public int lookupTextureWidth = 128;
		public int lookupTextureHeight = 128;
		
		public bool fastPreview = true;
		
		public Texture2D lookupTexture;
		
		void Awake () {
			if (!lookupTexture)
				Bake ();
		}
		
		static Color ColorRGB (int r, int g, int b) {
			return new Color ((float)r / 255.0f, (float)g / 255.0f, (float)b / 255.0f, 0.0f);
		}
		
		void CheckConsistency () {
			intensity = Mathf.Max (0.0f, intensity);
		
			wrapAround = Mathf.Clamp (wrapAround, -1.0f, 1.0f);
			metalic = Mathf.Clamp (metalic, 0.0f, 12.0f);
			
			diffuseIntensity = Mathf.Max (0.0f, diffuseIntensity);
			specularIntensity = Mathf.Max (0.0f, specularIntensity);
			specularShininess = Mathf.Clamp (specularShininess, 0.01f, 1.0f);
					
			translucency = Mathf.Clamp01 (translucency);
		}
		
		Color PixelFunc (float ndotl, float ndoth)
		{
			// pseudo metalic falloff
			ndotl *= Mathf.Pow (ndoth, metalic);
			float modDiffuseIntensity = (1f + metalic * 0.25f) * Mathf.Max (0f, diffuseIntensity - (1f-ndoth) * metalic);
		
			// diffuse tri-light
			float t0 = Mathf.Clamp01 (Mathf.InverseLerp (-wrapAround, 1f, ndotl * 2f - 1f));
			float t1 = Mathf.Clamp01 (Mathf.InverseLerp (-1f, Mathf.Max(-0.99f,-wrapAround), ndotl * 2f - 1f));
			Color diffuse = modDiffuseIntensity * Color.Lerp (backColor, Color.Lerp (fillColor, keyColor, t0), t1);
			diffuse += backColor * (1f - modDiffuseIntensity) * Mathf.Clamp01 (diffuseIntensity);
			
			// Blinn-Phong specular (with energy conservation)
			float n = specularShininess * 128f;
			float energyConservationTerm = ((n + 2f)*(n + 4f)) / (8f * Mathf.PI * (Mathf.Pow (2f, -n/2f) + n)); // by ryg
			//float energyConservationTerm = (n + 8f) / (8f * Mathf.PI); // from Real-Time Rendering
			float specular = specularIntensity * energyConservationTerm * Mathf.Pow (ndoth, n);
			
			// pseudo translucency (view dependent)
			float a = ndotl + 0.1f;
			float t = 0.5f * translucency * Mathf.Clamp01 (1f-a*ndoth) * Mathf.Clamp01 (1f-ndotl);
		
			Color c = diffuse * intensity + translucentColor * t + new Color(0f,0f,0f, specular);
			return c * intensity;
			
		}
		
		void TextureFunc (Texture2D tex)
		{
			for (int y = 0; y < tex.height; ++y)
				for (int x = 0; x < tex.width; ++x)
				{
					float w = tex.width;
					float h = tex.height;
					float vx = x / w;
					float vy = y / h;
					
					float NdotL = vx;
					float NdotH = vy;
					Color c = PixelFunc (NdotL, NdotH);
					tex.SetPixel(x, y, c);
				}
		}
		
		void GenerateLookupTexture (int width, int height) {
			Texture2D tex;
			if (lookupTexture && lookupTexture.width == width && lookupTexture.height == height)
				tex = lookupTexture;
			else
				tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
			
			CheckConsistency ();
			TextureFunc (tex);
			tex.Apply();
			tex.wrapMode = TextureWrapMode.Clamp;
		
			if (lookupTexture != tex)
				DestroyImmediate (lookupTexture);
			lookupTexture = tex;
		}

		public void Preview () {
			GenerateLookupTexture (32, 64);
		}
		
		public void Bake () {
			GenerateLookupTexture (lookupTextureWidth, lookupTextureHeight);
		}
	}
}
