using UnityEngine;

namespace Jeremy
{
    // Desktop preview only: image effect for the laparoscope cameras (built-in pipeline OnRenderImage).
    // Bloom on wet highlights, warm grade, lateral chromatic aberration, rounded vignette, faint noise.
    [RequireComponent(typeof(Camera))]
    public sealed class ScopeLens : MonoBehaviour
    {
        public float threshold = 0.72f, bloom = 0.55f, aberration = 0.012f, vignette = 1f, warmth = 1f;
        public float contrast = 0.35f, saturation = 1.12f, exposure = 1.05f, grain = 0.018f;
        private Material material;

        private void OnEnable()
        {
            Shader shader = Shader.Find("Hidden/Jeremy/LaparoscopeLens");
            if (shader != null && shader.isSupported && material == null) material = new Material(shader) { hideFlags = HideFlags.DontSave };
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null) { Graphics.Blit(source, destination); return; }
            material.SetFloat("_Threshold", threshold); material.SetFloat("_BloomIntensity", bloom);
            material.SetFloat("_Aberration", aberration); material.SetFloat("_Vignette", vignette);
            material.SetFloat("_Warmth", warmth); material.SetFloat("_Contrast", contrast);
            material.SetFloat("_Saturation", saturation); material.SetFloat("_Exposure", exposure); material.SetFloat("_Grain", grain);
            int w = Mathf.Max(1, source.width / 4), h = Mathf.Max(1, source.height / 4);
            RenderTexture a = RenderTexture.GetTemporary(w, h, 0, source.format), b = RenderTexture.GetTemporary(w, h, 0, source.format);
            Graphics.Blit(source, a, material, 0);
            material.SetVector("_BlurDir", new Vector4(1f, 0f, 0f, 0f)); Graphics.Blit(a, b, material, 1);
            material.SetVector("_BlurDir", new Vector4(0f, 1f, 0f, 0f)); Graphics.Blit(b, a, material, 1);
            material.SetVector("_BlurDir", new Vector4(2f, 0f, 0f, 0f)); Graphics.Blit(a, b, material, 1);
            material.SetVector("_BlurDir", new Vector4(0f, 2f, 0f, 0f)); Graphics.Blit(b, a, material, 1);
            material.SetTexture("_Bloom", a);
            Graphics.Blit(source, destination, material, 2);
            RenderTexture.ReleaseTemporary(a); RenderTexture.ReleaseTemporary(b);
        }

        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
