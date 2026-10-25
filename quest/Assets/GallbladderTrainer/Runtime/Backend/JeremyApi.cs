using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GallbladderTrainer.Backend
{
    public sealed class ApiResponse
    {
        public long Code;
        public string Body;
        public byte[] Data;
        public string Error;
        public Dictionary<string, string> Headers;

        public bool Ok { get { return Code >= 200 && Code < 300; } }
        /// <summary>True when the request never reached the backend (DNS, refused, timeout).</summary>
        public bool NetworkError { get { return Code == 0; } }
        /// <summary>Server-side or transient failures worth retrying; other 4xx are permanent.</summary>
        public bool Retryable { get { return Code == 0 || Code == 408 || Code == 429 || Code >= 500; } }

        public string Header(string name)
        {
            if (Headers == null) return null;
            foreach (var kv in Headers) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }
    }

    /// <summary>
    /// Coroutine HTTP client for the Jeremy.ai /api/v1 session API (UnityWebRequest; never blocks the main thread).
    /// Callers run the returned IEnumerators with StartCoroutine.
    /// </summary>
    public sealed class JeremyApi
    {
        public const string ApiPrefix = "/api/v1";
        public string BaseUrl { get; private set; }
        public string ApiKey { get; private set; }
        public int TimeoutSeconds { get; private set; }
        public bool IsConfigured { get { return !string.IsNullOrEmpty(BaseUrl); } }

        public JeremyApi(string baseUrl, string apiKey, int timeoutSeconds)
        {
            BaseUrl = string.IsNullOrEmpty(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
            if (BaseUrl != null && BaseUrl.EndsWith(ApiPrefix)) BaseUrl = BaseUrl.Substring(0, BaseUrl.Length - ApiPrefix.Length);
            ApiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey.Trim();
            TimeoutSeconds = Mathf.Clamp(timeoutSeconds, 2, 120);
        }

        public static string SessionPath(string sid) { return "/sessions/" + UnityWebRequest.EscapeURL(sid); }

        public string Url(string path) { return BaseUrl + ApiPrefix + path; }

        public IEnumerator Send(string method, string path, string json, Action<ApiResponse> done)
        {
            return Send(method, path, json, null, done);
        }

        /// <summary>Sends a request; Data holds raw bytes (used for audio/wav), Body the UTF-8 text.</summary>
        public IEnumerator Send(string method, string path, string json, string accept, Action<ApiResponse> done)
        {
            if (!IsConfigured)
            {
                if (done != null) done(new ApiResponse { Code = 0, Error = "backend not configured" });
                yield break;
            }
            using (var req = new UnityWebRequest(Url(path), method))
            {
                if (json != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Accept", accept ?? "application/json");
                if (ApiKey != null) req.SetRequestHeader("X-API-Key", ApiKey);
                req.timeout = TimeoutSeconds;
                req.redirectLimit = 0;
                yield return req.SendWebRequest();

                var r = new ApiResponse
                {
                    Code = req.result == UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode,
                    Error = req.error,
                    Headers = req.GetResponseHeaders()
                };
                if (req.downloadHandler != null)
                {
                    r.Data = req.downloadHandler.data;
                    if (r.Data != null && (accept == null || !accept.StartsWith("audio"))) r.Body = req.downloadHandler.text;
                    else if (r.Data != null && !r.Ok) r.Body = SafeText(r.Data);
                }
                if (done != null) done(r);
            }
        }

        static string SafeText(byte[] data)
        {
            try { return Encoding.UTF8.GetString(data); } catch (Exception) { return null; }
        }
    }
}
