using System;
using System.Collections;
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
        private void Awake() { source = GetComponent<AudioSource>(); source.playOnAwake = false; }
        // Backend must supply a tested HTTPS WAV/OGG/MP3 URL; no provider credentials on the headset.
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
