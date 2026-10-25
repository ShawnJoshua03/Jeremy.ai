using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GallbladderTrainer.Backend
{
    /// <summary>
    /// In-memory FIFO of serialized events/samples waiting for upload, mirrored to a small text file under
    /// Application.persistentDataPath so a crash or app switch does not lose an offline session.
    /// Items carry their session id; items queued before the session exists are bound when it does.
    /// </summary>
    public sealed class Outbox
    {
        public const string Events = "events";
        public const string Samples = "samples";

        public sealed class Item
        {
            public string SessionId; // null until the backend session exists
            public string Kind;      // Events or Samples
            public string Json;      // one serialized event or sample object
        }

        readonly List<Item> items = new List<Item>();
        readonly string path;
        readonly int maxItems;
        bool dirty;
        float nextSave;

        public int Count { get { return items.Count; } }
        public int Dropped { get; private set; }

        public Outbox(string persistPath, int maxItems = 20000)
        {
            path = persistPath;
            this.maxItems = maxItems;
        }

        public void Add(string sessionId, string kind, string json)
        {
            if (items.Count >= maxItems)
            {
                // Keep events (decisions matter most); shed the oldest samples first.
                int idx = items.FindIndex(i => i.Kind == Samples);
                items.RemoveAt(idx >= 0 ? idx : 0);
                Dropped++;
            }
            items.Add(new Item { SessionId = sessionId, Kind = kind, Json = json });
            dirty = true;
        }

        public void BindSession(string sessionId)
        {
            for (int i = 0; i < items.Count; i++) if (items[i].SessionId == null) items[i].SessionId = sessionId;
            dirty = true;
        }

        public int CountKind(string kind, string sessionId)
        {
            int n = 0;
            for (int i = 0; i < items.Count; i++) if (items[i].Kind == kind && items[i].SessionId == sessionId) n++;
            return n;
        }

        /// <summary>The oldest bound session that has pending items of this kind, or null.</summary>
        public string FirstSession(string kind)
        {
            for (int i = 0; i < items.Count; i++) if (items[i].Kind == kind && items[i].SessionId != null) return items[i].SessionId;
            return null;
        }

        /// <summary>Takes up to max items of one kind for one session, in order (not removed until Commit).</summary>
        public List<Item> Peek(string kind, string sessionId, int max)
        {
            var batch = new List<Item>();
            for (int i = 0; i < items.Count && batch.Count < max; i++)
                if (items[i].Kind == kind && items[i].SessionId == sessionId) batch.Add(items[i]);
            return batch;
        }

        public void Commit(List<Item> sent)
        {
            var set = new HashSet<Item>(sent);
            items.RemoveAll(set.Contains);
            dirty = true;
        }

        public void SaveIfDirty(bool force)
        {
            if (string.IsNullOrEmpty(path) || !dirty) return;
            if (!force && Time.unscaledTime < nextSave) return;
            nextSave = Time.unscaledTime + 2f;
            dirty = false;
            try
            {
                var sb = new StringBuilder(items.Count * 120);
                foreach (var it in items)
                {
                    if (it.SessionId == null) continue; // unbound items belong to this run only
                    sb.Append(it.SessionId).Append('\t').Append(it.Kind).Append('\t').Append(it.Json).Append('\n');
                }
                if (sb.Length == 0) { if (File.Exists(path)) File.Delete(path); return; }
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[Jeremy] Could not persist outbox: " + e.Message); }
        }

        public int LoadPersisted()
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int n = 0;
            try
            {
                if (!File.Exists(path)) return 0;
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split(new[] { '\t' }, 3);
                    if (parts.Length != 3 || parts[0].Length == 0) continue;
                    items.Add(new Item { SessionId = parts[0], Kind = parts[1], Json = parts[2] });
                    n++;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Jeremy] Could not read persisted outbox: " + e.Message); }
            return n;
        }

        /// <summary>Drops everything for a session the backend rejected permanently (e.g. 404 or 409 completed).</summary>
        public void DropSession(string sessionId, string kind)
        {
            Dropped += items.RemoveAll(i => i.SessionId == sessionId && i.Kind == kind);
            dirty = true;
        }
    }
}
