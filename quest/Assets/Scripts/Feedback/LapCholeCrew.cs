using System;
using System.Collections;
using System.Collections.Generic;
using Jeremy.Crew;
using UnityEngine;

namespace Jeremy
{
    // The API crew: four small avatars (Claude, Cartesia, Apify, Convex) that light up only when that service really did
    // work for this session. States come from GET /sessions/{id}/providers (the backend's provider log), so a service that
    // is off, falling back to a template or serving a snapshot says so on its tag instead of pretending.
    // Placed once above the instrument card, then pinned; it only turns to face the trainee.
    public sealed class LapCholeCrew : MonoBehaviour
    {
        [Serializable] class Entry { public string id, state; }
        [Serializable] class Reply { public Entry[] providers; }

        const float PollSeconds = 5f;

        PegTransferRig rig;
        CrewDisplay crew;
        Transform crewRoot;
        float nextPoll;
        bool polling, placed;
        string lastSession;

        void Update()
        {
            if (rig == null) rig = GetComponent<PegTransferRig>();
            if (rig == null || !rig.LapChole || rig.head == null) { SetVisible(false); return; }
            if (crew == null)
            {
                crew = CrewDisplay.Create(null, Vector3.zero);
                crewRoot = crew != null ? crew.transform : null;
                placed = false;
            }
            if (crew == null) return;
            SetVisible(true);
            if (!placed && rig.head.position.y > 0.8f) Place();
            Face();
            if (!polling && Time.unscaledTime >= nextPoll) StartCoroutine(Poll());
        }

        void OnDestroy() { if (crewRoot != null) Destroy(crewRoot.gameObject); }

        void SetVisible(bool on)
        {
            if (crewRoot != null && crewRoot.gameObject.activeSelf != on) crewRoot.gameObject.SetActive(on);
        }

        void Place()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(rig.head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) return;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            crewRoot.position = rig.head.position + fwd * 0.8f + right * 0.9f + Vector3.up * 0.42f;
            placed = true;
        }

        void Face()
        {
            Vector3 away = crewRoot.position - rig.head.position; away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) crewRoot.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        IEnumerator Poll()
        {
            polling = true;
            nextPoll = Time.unscaledTime + PollSeconds;
            string session = rig.Uploads != null ? rig.Uploads.LatestStarted()?.sessionId : null;
            if (!string.IsNullOrEmpty(session) && rig.Uploads.api != null)
            {
                ApiResult result = null;
                yield return rig.Uploads.api.Send("GET", ApiClient.SessionPath(session) + "/providers", null, value => result = value);
                if (result != null && result.Success && crew != null)
                {
                    Reply reply = null;
                    try { reply = JsonUtility.FromJson<Reply>(result.Body); } catch (ArgumentException) { }
                    if (reply != null && reply.providers != null)
                    {
                        var map = new Dictionary<string, string>();
                        foreach (Entry e in reply.providers) if (e != null && !string.IsNullOrEmpty(e.id)) map[e.id] = e.state ?? "";
                        crew.SetFromReport(map);
                    }
                }
            }
            polling = false;
        }
    }
}
