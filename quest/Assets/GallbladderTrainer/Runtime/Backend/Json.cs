using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GallbladderTrainer.Backend
{
    /// <summary>
    /// Minimal JSON reader/writer. JsonUtility cannot read nullable bools, dictionaries or nested arrays
    /// (report.correct, hover_dwell_s, replay tracks), so responses are parsed into plain
    /// Dictionary/List/double/string/bool/null trees and read through the JsonNode helpers.
    /// </summary>
    public static class Json
    {
        // ------------------------------------------------------------------ parse

        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = 0;
            try
            {
                object v = Value(text, ref i);
                return v;
            }
            catch (Exception) { return null; }
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException();
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return Num(s, ref i);
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            Ws(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i);
                string k = Str(s, ref i);
                Ws(s, ref i);
                i++; // ':'
                d[k] = Value(s, ref i);
                Ws(s, ref i);
                char c = s[i++];
                if (c == '}') return d;
                if (c != ',') throw new FormatException();
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            Ws(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Ws(s, ref i);
                char c = s[i++];
                if (c == ']') return l;
                if (c != ',') throw new FormatException();
            }
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException();
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        static double Num(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        // ------------------------------------------------------------------ read helpers

        public static Dictionary<string, object> Obj(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as List<object> : null;
        }

        public static string Str(object o, string key, string fallback = null)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static double Num(object o, string key, double fallback = 0)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) && v is double ? (double)v : fallback;
        }

        /// <summary>Returns null when the key is missing or JSON null (e.g. report correct=null for "not assessed").</summary>
        public static bool? Bool(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool ? (bool?)(bool)v : null;
        }

        public static bool Has(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            return d != null && d.ContainsKey(key);
        }

        // ------------------------------------------------------------------ write

        /// <summary>Tiny JSON writer: new Json.Writer().BeginObject().Field("a", 1).EndObject().ToString().</summary>
        public sealed class Writer
        {
            readonly StringBuilder sb = new StringBuilder(256);
            readonly Stack<bool> first = new Stack<bool>();

            public Writer BeginObject() { Sep(); sb.Append('{'); first.Push(true); return this; }
            public Writer EndObject() { sb.Append('}'); first.Pop(); return this; }
            public Writer BeginArray(string name = null) { Key(name); sb.Append('['); first.Push(true); return this; }
            public Writer EndArray() { sb.Append(']'); first.Pop(); return this; }
            public Writer BeginObject(string name) { Key(name); sb.Append('{'); first.Push(true); return this; }

            public Writer Field(string name, string v) { Key(name); if (v == null) sb.Append("null"); else Quote(v); return this; }
            public Writer Field(string name, long v) { Key(name); sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
            public Writer Field(string name, bool v) { Key(name); sb.Append(v ? "true" : "false"); return this; }
            public Writer Field(string name, double? v)
            {
                Key(name);
                if (v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value)) sb.Append(v.Value.ToString("R", CultureInfo.InvariantCulture));
                else sb.Append("null");
                return this;
            }
            public Writer Floats(string name, params float[] v)
            {
                Key(name); sb.Append('[');
                for (int i = 0; i < v.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    float f = float.IsNaN(v[i]) || float.IsInfinity(v[i]) ? 0f : v[i];
                    sb.Append(f.ToString("0.#####", CultureInfo.InvariantCulture));
                }
                sb.Append(']');
                return this;
            }
            /// <summary>Appends an already-serialized JSON value (used to splice queued payloads into a batch).</summary>
            public Writer Raw(string json) { Sep(); sb.Append(json); return this; }

            void Key(string name)
            {
                Sep();
                if (name == null) return;
                Quote(name); sb.Append(':');
            }

            void Sep()
            {
                if (first.Count == 0) return;
                if (first.Peek()) { first.Pop(); first.Push(false); }
                else sb.Append(',');
            }

            void Quote(string s)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }

            public override string ToString() { return sb.ToString(); }
        }
    }
}
