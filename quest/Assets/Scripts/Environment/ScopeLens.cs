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
        [Tooltip("Shallow depth of field: focus distance (m), in-focus range (m), strength 0..1.")]
        public float focus = 0.08f, focusRange = 0.05f, dof = 0f;
        private Material material;

        public void Configure(LaparoscopeMonitor.CavityLook l)
        {
            bloom = l.bloom; vignette = l.vignette; warmth = l.warmth; contrast = l.contrast; saturation = l.saturation;
            grain = l.grain; aberration = l.aberration; focus = l.focus; focusRange = l.focusRange; dof = l.dof;
            if (dof > 0f) GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;
        }

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
            // Defocused copy for depth of field (half resolution, blurred twice).
            RenderTexture c = null, d = null;
            material.SetFloat("_Dof", dof);
            if (dof > 0f)
            {
                c = RenderTexture.GetTemporary(source.width / 2, source.height / 2, 0, source.format);
                d = RenderTexture.GetTemporary(source.width / 2, source.height / 2, 0, source.format);
                Graphics.Blit(source, c, material, 3);
                for (int k = 1; k <= 2; k++)
                {
                    material.SetVector("_BlurDir", new Vector4(k * 1.5f, 0f, 0f, 0f)); Graphics.Blit(c, d, material, 1);
                    material.SetVector("_BlurDir", new Vector4(0f, k * 1.5f, 0f, 0f)); Graphics.Blit(d, c, material, 1);
                }
                material.SetTexture("_Defocus", c);
                material.SetFloat("_Focus", focus); material.SetFloat("_FocusRange", focusRange);
            }
            Graphics.Blit(source, destination, material, 2);
            RenderTexture.ReleaseTemporary(a); RenderTexture.ReleaseTemporary(b);
            if (c != null) { RenderTexture.ReleaseTemporary(c); RenderTexture.ReleaseTemporary(d); }
        }

        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
