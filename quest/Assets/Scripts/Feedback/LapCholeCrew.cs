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
    // Large enough to read (the avatars are scaled up), placed to the trainee's left and below eye level, and re-placed
    // when the trainee has turned well away from it. With no backend reachable the tags say "unavailable".
    public sealed class LapCholeCrew : MonoBehaviour
    {
        [Serializable] class Entry { public string id, state; }
        [Serializable] class Reply { public Entry[] providers; }

        const float PollSeconds = 5f;
        const float Scale = 1.7f, Distance = 1.5f, Below = 0.2f, SideDegrees = 40f, ReplaceDegrees = 70f;

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
            if (rig.head.position.y > 0.8f && (!placed || TurnedAway())) Place();
            Face();
            if (!polling && Time.unscaledTime >= nextPoll) StartCoroutine(Poll());
        }

        void OnDestroy() { if (crewRoot != null) Destroy(crewRoot.gameObject); }

        void SetVisible(bool on)
        {
            if (crewRoot != null && crewRoot.gameObject.activeSelf != on) crewRoot.gameObject.SetActive(on);
        }

        Vector3 Bearing(float extraDegrees)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(rig.head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) return Vector3.zero;
            return Quaternion.AngleAxis(extraDegrees, Vector3.up) * fwd.normalized;
        }

        bool TurnedAway()
        {
            Vector3 gaze = Bearing(0f);
            Vector3 toCrew = Vector3.ProjectOnPlane(crewRoot.position - rig.head.position, Vector3.up);
            return gaze != Vector3.zero && toCrew.sqrMagnitude > 1e-4f && Vector3.Angle(gaze, toCrew) > ReplaceDegrees;
        }

        void Place()
        {
            Vector3 dir = Bearing(-SideDegrees); // left of where the trainee is looking
            if (dir == Vector3.zero) return;
            crewRoot.localScale = Vector3.one * Scale;
            crewRoot.position = rig.head.position + dir * Distance + Vector3.down * Below;
            placed = true;
        }

        void Face()
        {
            Vector3 away = crewRoot.position - rig.head.position; away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) crewRoot.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        void SetAll(ProviderState state)
        {
            foreach (Provider p in (Provider[])Enum.GetValues(typeof(Provider))) crew.SetState(p, state);
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
                if ((result == null || !result.Success) && crew != null) SetAll(ProviderState.Unavailable);
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
