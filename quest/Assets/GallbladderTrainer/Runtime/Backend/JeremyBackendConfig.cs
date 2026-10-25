using UnityEngine;

namespace GallbladderTrainer.Backend
{
    /// <summary>
    /// Backend origin and optional X-API-Key for the Jeremy.ai session API. Only the session API key belongs here:
    /// Gemini, Cartesia, Apify and x402 credentials stay on the backend and never ship on the headset.
    /// Create via Assets > Create > Gallbladder Trainer > Backend Config, or place one named
    /// "JeremyBackendConfig" in a Resources folder to be picked up automatically.
    /// </summary>
    [CreateAssetMenu(menuName = "Gallbladder Trainer/Backend Config", fileName = "JeremyBackendConfig")]
    public sealed class JeremyBackendConfig : ScriptableObject
    {
        [Tooltip("Backend origin without the /api/v1 suffix, e.g. http://192.168.1.20:8000")]
        public string baseUrl = "http://127.0.0.1:8000";
        [Tooltip("Optional. Sent as X-API-Key when the backend has API_KEY set.")]
        public string apiKey = "";
        public string traineeId = "quest-trainee";
        [Range(2, 60)] public int timeoutSeconds = 10;

        public const string ResourceName = "JeremyBackendConfig";
    }
}
