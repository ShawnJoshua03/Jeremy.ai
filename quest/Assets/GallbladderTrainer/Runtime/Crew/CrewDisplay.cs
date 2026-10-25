using System;
using System.Collections.Generic;
using UnityEngine;

namespace GallbladderTrainer.Crew
{
    /// <summary>
    /// Five service avatars in a gentle arc. Fallback, off, unavailable, and snapshot dim the body and put that word on the tag.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrewDisplay : MonoBehaviour
    {
        const int Count = 5;

        readonly CrewTextRegistry texts = new CrewTextRegistry();
        CrewAvatar[] avatars;
        bool built;
        int warmup;

        public static CrewDisplay Create(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("ApiCrew");
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var display = go.AddComponent<CrewDisplay>();
            display.Build();
            go.SetActive(true);
            return display;
        }

        void Awake()
        {
            if (!built) Build();
        }

        void OnEnable()
        {
            if (built) texts.Apply();
        }

        void Start()
        {
            if (built) texts.Apply();
        }

        void Update()
        {
            if (!built) return;
            if (warmup < 3)
            {
                warmup++;
                texts.Apply();
            }
            float dt = Time.deltaTime;
            for (int i = 0; i < avatars.Length; i++) avatars[i].Tick(dt);
        }

        public void SetState(Provider provider, ProviderState state)
        {
            CrewAvatar avatar = Get(provider);
            if (avatar != null) avatar.SetState(state);
        }

        /// <summary>Keys and values match the report providers field: claude, cartesia, apify, x402, convex.</summary>
        public void SetFromReport(IDictionary<string, string> providers)
        {
            if (!built || providers == null) return;
            ApplyKey(providers, "claude", Provider.Claude);
            ApplyKey(providers, "cartesia", Provider.Cartesia);
            ApplyKey(providers, "apify", Provider.Apify);
            ApplyKey(providers, "x402", Provider.X402);
            ApplyKey(providers, "convex", Provider.Convex);
        }

        public void Pulse(Provider provider, string action)
        {
            CrewAvatar avatar = Get(provider);
            if (avatar != null) avatar.Pulse(action);
        }

        public void Speak(Provider provider, float amplitude01)
        {
            CrewAvatar avatar = Get(provider);
            if (avatar != null) avatar.Speak(amplitude01);
        }

        void Build()
        {
            if (built) return;
            built = true;
            CrewLook.BuiltinFont();
            avatars = new CrewAvatar[Count];
            for (int i = 0; i < Count; i++)
            {
                var who = (Provider)i;
                CrewAvatar avatar = CrewAvatar.Build(who, transform, i, texts);
                float t = i - 2f;
                avatar.Root.localPosition = new Vector3(t * 0.26f, 0f, Mathf.Abs(t) * 0.04f);
                avatar.Root.localRotation = Quaternion.Euler(0f, t * 6.5f, 0f);
                avatars[i] = avatar;
            }
            texts.Apply();
        }

        CrewAvatar Get(Provider provider)
        {
            int index = (int)provider;
            if (!built || avatars == null || index < 0 || index >= avatars.Length) return null;
            return avatars[index];
        }

        void ApplyKey(IDictionary<string, string> providers, string key, Provider provider)
        {
            string value;
            if (!TryGet(providers, key, out value)) return;
            ProviderState state;
            if (!TryParse(value, out state)) return;
            SetState(provider, state);
        }

        static bool TryGet(IDictionary<string, string> providers, string key, out string value)
        {
            if (providers.TryGetValue(key, out value)) return true;
            var dict = providers as Dictionary<string, string>;
            if (dict != null)
            {
                foreach (KeyValuePair<string, string> pair in dict)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = pair.Value;
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        static bool TryParse(string value, out ProviderState state)
        {
            state = ProviderState.Unknown;
            if (string.IsNullOrEmpty(value)) return false;
            if (Is(value, "used")) { state = ProviderState.Used; return true; }
            if (Is(value, "fallback")) { state = ProviderState.Fallback; return true; }
            if (Is(value, "off")) { state = ProviderState.Off; return true; }
            if (Is(value, "unavailable")) { state = ProviderState.Unavailable; return true; }
            if (Is(value, "live")) { state = ProviderState.Live; return true; }
            if (Is(value, "snapshot")) { state = ProviderState.Snapshot; return true; }
            if (Is(value, "paid")) { state = ProviderState.Paid; return true; }
            if (Is(value, "unpaid")) { state = ProviderState.Unpaid; return true; }
            if (Is(value, "mirrored")) { state = ProviderState.Mirrored; return true; }
            return false;
        }

        static bool Is(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
