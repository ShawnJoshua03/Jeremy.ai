using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Jeremy
{
    public sealed class ApiResult
    {
        public bool Success;
        public long Status;
        public string Body, Error;
        public bool Retryable => Status == 0 || Status == 408 || Status == 429 || Status >= 500;
    }
    public sealed class ApiClient : MonoBehaviour
    {
        [Tooltip("HTTPS origin, without /api/v1. Empty keeps all attempts offline.")]
        public string baseUrl = "";
        [Tooltip("Editor-only HTTP for a local development server.")]
        public bool allowEditorHttp;
        [NonSerialized] public string sessionToken;
        public int timeoutSeconds = 15;

        public bool IsConfigured => TryUrl("/api/v1/sessions", out _);
        public bool TryUrl(string path, out string url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(baseUrl) || !path.StartsWith("/api/v1/")) return false;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri origin) || !string.IsNullOrEmpty(origin.UserInfo)
                || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/") return false;
            bool validScheme = origin.Scheme == "https";
#if UNITY_EDITOR
            validScheme |= allowEditorHttp && origin.Scheme == "http" && origin.IsLoopback;
#endif
            if (!validScheme) return false;
            url = baseUrl.TrimEnd('/') + path;
            return true;
        }
        public IEnumerator Send(string method, string path, string json, Action<ApiResult> callback)
        {
            if (!TryUrl(path, out string url))
            {
                callback(new ApiResult { Error = "Configure an HTTPS backend origin." });
                yield break;
            }
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Clamp(timeoutSeconds, 1, 120);
                request.redirectLimit = 0;
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (!string.IsNullOrWhiteSpace(sessionToken)) request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
                yield return request.SendWebRequest();
                callback(new ApiResult { Success = request.result == UnityWebRequest.Result.Success,
                    Status = request.responseCode, Body = request.downloadHandler.text, Error = request.error });
            }
        }
        public static string SessionPath(string sessionId) => "/api/v1/sessions/" + Uri.EscapeDataString(sessionId);
    }
}
