using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Jeremy
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class CoachAudioPlayer : MonoBehaviour
    {
        public string Status { get; private set; } = "Text feedback available";
        private AudioSource source;
        private AudioClip ownedClip;
        private Coroutine loading;
        private UnityWebRequest activeRequest;
        [Serializable] private sealed class SpeechBody { public string text; }
        private void Awake() { source = GetComponent<AudioSource>(); source.playOnAwake = false; }
        // POST displayed coaching text. The speech route returns audio/wav bytes, or 503 when voice is unavailable.
        public void PlaySession(ApiClient api, string sessionId, string text)
        {
            StopSpeaking();
            if (api == null || string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(text)) return;
            string spoken = text.Trim();
            if (spoken.Length > 2000) spoken = spoken.Substring(0, 2000);
            if (!api.TryUrl(ApiClient.SessionPath(sessionId) + "/coach/audio", out string url))
            {
                Status = "Voice unavailable; read the coaching text";
                return;
            }
            loading = StartCoroutine(PostWav(api, url, spoken));
        }
        private IEnumerator PostWav(ApiClient api, string url, string spoken)
        {
            using (var request = new UnityWebRequest(url, "POST"))
            {
                activeRequest = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new SpeechBody { text = spoken })));
                request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.WAV);
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrWhiteSpace(api.sessionToken)) request.SetRequestHeader("Authorization", "Bearer " + api.sessionToken);
                request.timeout = 35;
                request.redirectLimit = 0;
                Status = "Loading voice feedback";
                yield return request.SendWebRequest();
                activeRequest = null;
                if (request.result != UnityWebRequest.Result.Success) { Status = "Voice unavailable; read the coaching text"; yield break; }
                ownedClip = DownloadHandlerAudioClip.GetContent(request);
                if (ownedClip == null) { Status = "Voice unavailable; read the coaching text"; yield break; }
                source.clip = ownedClip;
                source.Play();
                Status = "Playing voice feedback";
            }
            loading = null;
        }
        // Direct clip URL. HTTPS only; no provider credentials on the headset.
        public void PlayUrl(string url, AudioType type = AudioType.WAV)
        {
            StopSpeaking();
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) || parsed.Scheme != "https" || !string.IsNullOrEmpty(parsed.UserInfo)) return;
            loading = StartCoroutine(Download(url, type));
        }
        private IEnumerator Download(string url, AudioType type)
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                activeRequest = request;
                request.timeout = 20;
                request.redirectLimit = 0;
                Status = "Loading voice feedback";
                yield return request.SendWebRequest();
                activeRequest = null;
                if (request.result != UnityWebRequest.Result.Success) { Status = "Voice unavailable; read the coaching text"; yield break; }
                ownedClip = DownloadHandlerAudioClip.GetContent(request);
                source.clip = ownedClip;
                source.Play();
                Status = "Playing voice feedback";
            }
            loading = null;
        }
        public void StopSpeaking()
        {
            if (activeRequest != null) { activeRequest.Abort(); activeRequest.Dispose(); activeRequest = null; }
            if (loading != null) { StopCoroutine(loading); loading = null; }
            if (source != null) { source.Stop(); source.clip = null; }
            if (ownedClip != null) { Destroy(ownedClip); ownedClip = null; }
            Status = "Text feedback available";
        }
        private void OnDisable() => StopSpeaking();
    }
}
