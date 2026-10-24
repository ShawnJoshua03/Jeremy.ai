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
        [Tooltip("Testing only: allow plain http:// to a private LAN address (10.x, 172.16-31.x, 192.168.x), e.g. a PC running uvicorn.")]
        public bool allowLanHttp;
        [Tooltip("Backend API_KEY, sent as X-API-Key. Leave empty when the backend has no API_KEY.")]
        public string apiKey = "";
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
            validScheme |= allowLanHttp && origin.Scheme == "http" && IsPrivateLan(origin.Host);
            if (!validScheme) return false;
            url = baseUrl.TrimEnd('/') + path;
            return true;
        }
        public void ApplyAuth(UnityWebRequest request)
        {
            if (!string.IsNullOrWhiteSpace(apiKey)) request.SetRequestHeader("X-API-Key", apiKey.Trim());
            if (!string.IsNullOrWhiteSpace(sessionToken)) request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
        }
        public static bool IsPrivateLan(string host)
        {
            if (!System.Net.IPAddress.TryParse(host, out System.Net.IPAddress address)) return false;
            if (System.Net.IPAddress.IsLoopback(address)) return true;
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            byte[] b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
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
                ApplyAuth(request);
                yield return request.SendWebRequest();
                callback(new ApiResult { Success = request.result == UnityWebRequest.Result.Success,
                    Status = request.responseCode, Body = request.downloadHandler.text, Error = request.error });
            }
        }
        public static string SessionPath(string sessionId) => "/api/v1/sessions/" + Uri.EscapeDataString(sessionId);
    }
}
