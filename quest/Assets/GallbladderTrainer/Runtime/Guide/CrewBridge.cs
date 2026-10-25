using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GallbladderTrainer.Guide
{
    /// <summary>
    /// Adapter to the API crew (GallbladderTrainer.Crew.CrewDisplay / MoneyCounter, built in Runtime/Crew/ by a
    /// separate worker). Bound by reflection so this folder compiles with or without Runtime/Crew present; when the
    /// crew is missing every call is a no-op and Available is false. link.xml keeps the types under IL2CPP stripping.
    /// Provider and state strings are the contract's words ("gemini", "used", "snapshot", ...).
    /// </summary>
    public sealed class CrewBridge
    {
        const string Ns = "GallbladderTrainer.Crew.";
        static bool resolved;
        static Type displayType, counterType, providerType, stateType, lineType;

        object display, counter;

        public bool Available { get { return display != null; } }
        public bool HasCounter { get { return counter != null; } }
        public Component DisplayComponent { get { return display as Component; } }

        static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            // Runtime/Crew compiles into this assembly unless it gets its own asmdef; check here first.
            var candidates = new List<Assembly> { typeof(CrewBridge).Assembly };
            if (candidates[0].GetType(Ns + "CrewDisplay", false) == null) candidates.AddRange(AppDomain.CurrentDomain.GetAssemblies());
            foreach (Assembly a in candidates)
            {
                Type t = a.GetType(Ns + "CrewDisplay", false);
                if (t == null) continue;
                displayType = t;
                counterType = a.GetType(Ns + "MoneyCounter", false);
                providerType = a.GetType(Ns + "Provider", false);
                stateType = a.GetType(Ns + "ProviderState", false);
                lineType = counterType != null ? counterType.GetNestedType("Line") : null;
                break;
            }
        }

        /// <summary>Creates the crew row (CrewDisplay.Create). The money counter is added later with AttachCounter.</summary>
        public static CrewBridge Create(Transform parent, Vector3 crewPos)
        {
            Resolve();
            var b = new CrewBridge();
            b.display = Invoke(displayType, null, "Create", parent, crewPos);
            if (displayType == null) Debug.Log("[Jeremy] Runtime/Crew not present: crew avatars and money counter disabled.");
            return b;
        }

        /// <summary>Creates the MoneyCounter once (MoneyCounter.Create) under the report panel.</summary>
        public void AttachCounter(Transform parent, Vector3 localPos)
        {
            if (counter == null) counter = Invoke(counterType, null, "Create", parent, localPos);
        }

        public void SetState(string provider, string state)
        {
            object p = Provider(provider), s = State(state);
            if (p != null && s != null) Invoke(displayType, display, "SetState", p, s);
        }

        public void SetFromReport(IDictionary<string, string> providers)
        {
            if (providers != null) Invoke(displayType, display, "SetFromReport", providers);
        }

        public void Pulse(string provider, string action)
        {
            object p = Provider(provider);
            if (p != null) Invoke(displayType, display, "Pulse", p, action ?? "");
        }

        public void Speak(string provider, float amplitude01)
        {
            object p = Provider(provider);
            if (p != null) Invoke(displayType, display, "Speak", p, Mathf.Clamp01(amplitude01));
        }

        public struct CostLine { public string Label; public decimal Usd; public bool Assumed; public string Source; }

        /// <summary>Plays the money count-up. Returns false (and does nothing) when MoneyCounter is unavailable.</summary>
        public bool PlayMoney(IList<CostLine> lines, decimal total, string disclaimer, Action onDone, bool instant)
        {
            if (counter == null || lineType == null) return false;
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(lineType));
            foreach (var l in lines)
            {
                object v = Activator.CreateInstance(lineType);
                lineType.GetField("Label").SetValue(v, l.Label);
                lineType.GetField("Usd").SetValue(v, l.Usd);
                lineType.GetField("Assumed").SetValue(v, l.Assumed);
                lineType.GetField("Source").SetValue(v, l.Source);
                list.Add(v);
            }
            if (instant) { Invoke(counterType, counter, "ShowInstant", list, total, disclaimer); return true; }
            Invoke(counterType, counter, "Play", list, total, disclaimer, onDone);
            return true;
        }

        public void SetCounterVisible(bool on)
        {
            var c = counter as Component;
            if (c != null) c.gameObject.SetActive(on);
        }

        public void SetCrewVisible(bool on)
        {
            var c = display as Component;
            if (c != null) c.gameObject.SetActive(on);
        }

        // ------------------------------------------------------------------ mapping

        static object Provider(string id)
        {
            if (providerType == null || id == null) return null;
            switch (id.ToLowerInvariant())
            {
                case "gemini": return Enum.Parse(providerType, "Gemini");
                case "cartesia": return Enum.Parse(providerType, "Cartesia");
                case "apify": return Enum.Parse(providerType, "Apify");
                case "x402": return Enum.Parse(providerType, "X402");
                case "convex": return Enum.Parse(providerType, "Convex");
                default: return null; // unknown future provider: ignore rather than crash
            }
        }

        static object State(string s)
        {
            if (stateType == null) return null;
            string name;
            switch ((s ?? "").ToLowerInvariant())
            {
                case "used": name = "Used"; break;
                case "fallback": name = "Fallback"; break;
                case "off": name = "Off"; break;
                case "live": name = "Live"; break;
                case "snapshot": name = "Snapshot"; break;
                case "paid": name = "Paid"; break;
                case "unpaid": name = "Unpaid"; break;
                case "mirrored": name = "Mirrored"; break;
                case "unavailable": name = "Unavailable"; break;
                default: name = "Unknown"; break;
            }
            return Enum.IsDefined(stateType, name) ? Enum.Parse(stateType, name) : null;
        }

        static object Invoke(Type t, object target, string method, params object[] args)
        {
            if (t == null || (target == null && method != "Create")) return null;
            try
            {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (m.Name != method || m.GetParameters().Length != args.Length) continue;
                    return m.Invoke(target, args);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Jeremy] Crew call " + method + " failed: " + (e.InnerException ?? e).Message);
            }
            return null;
        }
    }
}
