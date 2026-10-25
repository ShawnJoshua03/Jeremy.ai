using System;
using System.Collections;
using System.Collections.Generic;
using Jeremy.Crew;
using UnityEngine;

namespace Jeremy
{
    // The API crew (Claude, Cartesia, Apify, Convex), shown only when a service really did new work for this session.
    // GET /sessions/{id}/providers returns a per-service work count; when a count rises, that one avatar pops up small,
    // low and to the right of the trainee's view with what it just did, then hides again. Nothing shows otherwise,
    // including when the backend is unreachable.
    public sealed class LapCholeCrew : MonoBehaviour
    {
        [Serializable] class Entry { public string id, state, last_action; public int count; }
        [Serializable] class Reply { public Entry[] providers; }

        const float PollSeconds = 3f, ShowSeconds = 4f;
        const float Scale = 0.35f, Ahead = 0.85f, Right = 0.38f, Below = 0.32f;

        struct Pop { public Provider who; public ProviderState state; public string action; }

        PegTransferRig rig;
        CrewDisplay crew;
        Transform crewRoot;
        float nextPoll, hideAt;
        bool polling, showing;
        string session;
        readonly Dictionary<string, int> seen = new Dictionary<string, int>();
        readonly Queue<Pop> queue = new Queue<Pop>();

        void Update()
        {
            if (rig == null) rig = GetComponent<PegTransferRig>();
            if (rig == null || !rig.LapChole || rig.head == null) { Hide(); return; }
            if (crew == null)
            {
                crew = CrewDisplay.Create(null, Vector3.zero);
                crewRoot = crew != null ? crew.transform : null;
                if (crew != null) { crewRoot.localScale = Vector3.one * Scale; crew.ShowOnly(null); }
            }
            if (crew == null) return;
            if (showing && Time.unscaledTime >= hideAt) Hide();
            if (!showing && queue.Count > 0) Show(queue.Dequeue());
            if (showing) Face();
            if (!polling && Time.unscaledTime >= nextPoll) StartCoroutine(Poll());
        }

        void OnDestroy() { if (crewRoot != null) Destroy(crewRoot.gameObject); }

        void Show(Pop pop)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(rig.head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            crewRoot.position = rig.head.position + fwd * Ahead + right * Right + Vector3.down * Below;
            crew.ShowOnly(pop.who);
            crew.SetState(pop.who, pop.state);
            crew.Pulse(pop.who, pop.action);
            crewRoot.gameObject.SetActive(true);
            showing = true;
            hideAt = Time.unscaledTime + ShowSeconds;
        }

        void Hide()
        {
            showing = false;
            if (crew != null) crew.ShowOnly(null);
            if (crewRoot != null && crewRoot.gameObject.activeSelf) crewRoot.gameObject.SetActive(false);
        }

        void Face()
        {
            Vector3 away = crewRoot.position - rig.head.position; away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) crewRoot.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        static bool TryProvider(string id, out Provider who)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "claude": who = Provider.Claude; return true;
                case "cartesia": who = Provider.Cartesia; return true;
                case "apify": who = Provider.Apify; return true;
                case "convex": who = Provider.Convex; return true;
                default: who = Provider.Claude; return false;
            }
        }

        static ProviderState StateOf(string state)
        {
            switch ((state ?? "").ToLowerInvariant())
            {
                case "used": return ProviderState.Used;
                case "fallback": return ProviderState.Fallback;
                case "off": return ProviderState.Off;
                case "live": return ProviderState.Live;
                case "snapshot": return ProviderState.Snapshot;
                case "mirrored": return ProviderState.Mirrored;
                case "unavailable": return ProviderState.Unavailable;
                default: return ProviderState.Unknown;
            }
        }

        IEnumerator Poll()
        {
            polling = true;
            nextPoll = Time.unscaledTime + PollSeconds;
            string current = rig.Uploads != null ? rig.Uploads.LatestStarted()?.sessionId : null;
            if (current != session) { session = current; seen.Clear(); queue.Clear(); }
            if (!string.IsNullOrEmpty(session) && rig.Uploads.api != null)
            {
                ApiResult result = null;
                yield return rig.Uploads.api.Send("GET", ApiClient.SessionPath(session) + "/providers", null, value => result = value);
                Reply reply = null;
                if (result != null && result.Success)
                    try { reply = JsonUtility.FromJson<Reply>(result.Body); } catch (ArgumentException) { }
                if (reply != null && reply.providers != null)
                {
                    foreach (Entry e in reply.providers)
                    {
                        if (e == null || !TryProvider(e.id, out Provider who)) continue;
                        seen.TryGetValue(e.id, out int before);
                        if (e.count > before && queue.Count < 4)
                            queue.Enqueue(new Pop { who = who, state = StateOf(e.state), action = e.last_action });
                        seen[e.id] = e.count;
                    }
                }
            }
            polling = false;
        }
    }
}
