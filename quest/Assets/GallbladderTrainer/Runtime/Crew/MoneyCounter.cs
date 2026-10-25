using System;
using System.Collections.Generic;
using UnityEngine;

namespace GallbladderTrainer.Crew
{
    /// <summary>
    /// Eased mistake-cost ticker. Counts up from $0; each line drops in as its amount is added.
    /// Assumed lines are marked "assumed". The disclaimer stays visible. Update does not allocate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoneyCounter : MonoBehaviour
    {
        public struct Line
        {
            public string Label;
            public decimal Usd;
            public bool Assumed;
            public string Source;
        }

        const int MaxLines = 8;
        const int DollarCols = 7;
        const int Columns = 9;
        const long MaxCents = 999999999L;
        const float DigitHeight = 0.052f;
        const float LineHeight = 0.0155f;
        const string DefaultDisclaimer = "Illustrative, not billing data.";

        static readonly string[] DigitText = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        static readonly long[] Pow = { 1L, 10L, 100L, 1000L, 10000L, 100000L, 1000000L };
        static readonly Color SourcedInk = new Color(0.94f, 0.96f, 0.97f, 1f);
        static readonly Color AssumedInk = new Color(0.95f, 0.74f, 0.38f, 1f);
        static readonly Color DisclaimerInk = new Color(0.95f, 0.76f, 0.42f, 1f);

        sealed class Column
        {
            public Renderer[] Faces;
            public float X;
            public int Shown = -2;
            public bool Visible;
        }

        sealed class Row
        {
            public Transform Root;
            public TextMesh Label;
            public Renderer Rend;
            public Material Mat;
            public bool BaseP;
            public bool ColorP;
            public Color Base;
            public float RestY;
            public float Progress;
            public bool Armed;
            public long Threshold;
            public float TimeMark;
        }

        readonly CrewTextRegistry texts = new CrewTextRegistry();
        readonly List<Row> pool = new List<Row>();
        readonly Line[] stored = new Line[MaxLines];

        Column[] columns;
        Transform sign;
        float signTarget;
        Renderer commaLo;
        Renderer commaHi;
        Transform bar;
        float barFull = 0.52f;
        float barY = 0.004f;
        float barZ = 0.003f;
        TextMesh disclaimer;
        bool built;
        int warmup;
        int rowCount;
        bool playing;
        bool timed;
        float elapsed;
        float duration = 2.4f;
        long targetCents;
        long shownCents = -1;
        Action done;

        public static MoneyCounter Create(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("MoneyCounter");
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var counter = go.AddComponent<MoneyCounter>();
            counter.Build();
            go.SetActive(true);
            return counter;
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
            float dt = Time.deltaTime;
            if (warmup < 3)
            {
                warmup++;
                texts.Apply();
            }
            TickSign(dt);
            if (!playing) return;

            elapsed += dt;
            float t = duration <= 0f ? 1f : elapsed / duration;
            if (t > 1f) t = 1f;
            float eased = t >= 1f ? 1f : 1f - (1f - t) * (1f - t) * (1f - t);
            long shown = t >= 1f ? targetCents : (long)Math.Round(targetCents * (double)eased, MidpointRounding.AwayFromZero);
            if (shown < 0L) shown = 0L;
            if (shown > targetCents) shown = targetCents;
            ApplyAmount(shown);
            PaintBar(eased);
            TickRows(dt, shown, t);
            if (t >= 1f && RowsSettled()) Finish();
        }

        public void Play(IList<Line> lines, decimal total, string disclaimerText, Action onDone = null)
        {
            if (!built) return;
            playing = false;
            done = null;
            Load(lines, total, disclaimerText);
            elapsed = 0f;
            shownCents = -1;
            ApplyAmount(0L);
            SnapSign();
            PaintBar(0f);
            for (int i = 0; i < rowCount; i++)
            {
                Row row = pool[i];
                row.Armed = false;
                row.Progress = 0f;
                row.Rend.enabled = false;
                var pos = row.Root.localPosition;
                pos.y = row.RestY + 0.04f;
                row.Root.localPosition = pos;
            }
            done = onDone;
            playing = true;
        }

        public void ShowInstant(IList<Line> lines, decimal total, string disclaimerText)
        {
            if (!built) return;
            playing = false;
            done = null;
            Load(lines, total, disclaimerText);
            shownCents = -1;
            ApplyAmount(targetCents);
            SnapSign();
            PaintBar(1f);
            for (int i = 0; i < rowCount; i++)
            {
                Row row = pool[i];
                row.Armed = true;
                row.Progress = 1f;
                row.Rend.enabled = true;
                row.Root.localPosition = new Vector3(0f, row.RestY, 0f);
                PaintRow(row, 1f);
            }
        }

        void Build()
        {
            if (built) return;
            built = true;
            CrewLook.BuiltinFont();

            Material edge = CrewLook.MakeMesh(new Color(0.16f, 0.38f, 0.44f));
            Material plate = CrewLook.MakeMesh(new Color(0.055f, 0.07f, 0.085f));
            Material gold = CrewLook.MakeMesh(new Color(0.95f, 0.78f, 0.35f));
            CrewLook.Piece(transform, PrimitiveType.Cube, "Frame", new Vector3(0f, 0f, 0.012f), Vector3.zero, new Vector3(0.94f, 0.64f, 0.008f), edge);
            CrewLook.Piece(transform, PrimitiveType.Cube, "Plate", new Vector3(0f, 0f, 0.006f), Vector3.zero, new Vector3(0.92f, 0.62f, 0.008f), plate);

            Transform ticker = CrewLook.Empty(transform, "Ticker", new Vector3(0f, 0.20f, 0f));
            Material ink = CrewLook.MakeText(Color.white);
            columns = new Column[Columns];
            LayoutColumns();
            for (int c = 0; c < Columns; c++)
            {
                columns[c].Faces = new Renderer[10];
                for (int d = 0; d < 10; d++)
                {
                    TextMesh face = CrewLook.Label(ticker, "D" + c + "_" + d, DigitText[d], DigitHeight, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, texts, ink);
                    face.transform.localPosition = new Vector3(columns[c].X, 0f, 0f);
                    Renderer rend = face.GetComponent<MeshRenderer>();
                    rend.enabled = false;
                    columns[c].Faces[d] = rend;
                }
            }

            TextMesh signMesh = CrewLook.Label(ticker, "Dollar", "$", DigitHeight, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, texts, ink);
            sign = signMesh.transform;
            TextMesh dot = CrewLook.Label(ticker, "Dot", ".", DigitHeight, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, texts, ink);
            dot.transform.localPosition = new Vector3(DotX(), 0f, 0f);
            TextMesh lo = CrewLook.Label(ticker, "CommaLo", ",", DigitHeight, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, texts, ink);
            lo.transform.localPosition = new Vector3(CommaLoX(), 0f, 0f);
            commaLo = lo.GetComponent<MeshRenderer>();
            commaLo.enabled = false;
            TextMesh hi = CrewLook.Label(ticker, "CommaHi", ",", DigitHeight, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, texts, ink);
            hi.transform.localPosition = new Vector3(CommaHiX(), 0f, 0f);
            commaHi = hi.GetComponent<MeshRenderer>();
            commaHi.enabled = false;

            bar = CrewLook.Piece(transform, PrimitiveType.Cube, "TickerBar", new Vector3(0f, 0.148f, 0f), Vector3.zero, new Vector3(barFull, barY, barZ), gold);
            disclaimer = CrewLook.Label(transform, "Disclaimer", DefaultDisclaimer, 0.015f, DisclaimerInk, TextAnchor.MiddleCenter, FontStyle.Normal, texts);
            disclaimer.transform.localPosition = new Vector3(0f, -0.27f, 0f);

            ApplyAmount(0L);
            SnapSign();
            PaintBar(0f);
            texts.Apply();
        }

        void Load(IList<Line> lines, decimal total, string disclaimerText)
        {
            int count = lines == null ? 0 : lines.Count;
            if (count > MaxLines) count = MaxLines;
            if (count < 0) count = 0;
            EnsureRows(count);
            rowCount = count;
            for (int i = 0; i < count; i++) stored[i] = lines[i];

            double sum = 0d;
            for (int i = 0; i < count; i++)
            {
                double usd = (double)stored[i].Usd;
                if (usd > 0d) sum += usd;
            }
            targetCents = Cents(total);
            timed = targetCents <= 0L;

            float top = 0.08f;
            float bottom = -0.20f;
            float step = count <= 1 ? 0.068f : (top - bottom) / count;
            if (step > 0.068f) step = 0.068f;

            double run = 0d;
            for (int i = 0; i < count; i++)
            {
                Row row = pool[i];
                double usd = (double)stored[i].Usd;
                if (usd < 0d) usd = 0d;
                run += usd;
                long mark = targetCents;
                if (sum > 0d && i < count - 1)
                    mark = (long)Math.Round(run / sum * targetCents, MidpointRounding.AwayFromZero);
                if (mark < 0L) mark = 0L;
                if (mark > targetCents) mark = targetCents;
                row.Threshold = mark;
                row.TimeMark = (i + 1f) / count * 0.9f;
                row.RestY = top - i * step;
                string desc = Describe(stored[i]);
                if (row.Label.text != desc) row.Label.text = desc;
                row.Base = stored[i].Assumed ? AssumedInk : SourcedInk;
                row.Rend.enabled = false;
            }
            for (int i = count; i < pool.Count; i++)
            {
                pool[i].Armed = false;
                pool[i].Rend.enabled = false;
            }

            duration = 2.1f + 0.42f * count;
            if (duration < 2.4f) duration = 2.4f;
            if (duration > 6f) duration = 6f;
            SetDisclaimer(disclaimerText);
            texts.Apply();
        }

        void TickRows(float dt, long shown, float t)
        {
            bool countDone = t >= 1f;
            for (int i = 0; i < rowCount; i++)
            {
                Row row = pool[i];
                if (!row.Armed)
                {
                    bool due = timed ? t >= row.TimeMark : shown >= row.Threshold;
                    if (countDone) due = true;
                    if (!due) continue;
                    row.Armed = true;
                    row.Rend.enabled = true;
                }
                if (row.Progress < 1f)
                {
                    row.Progress += dt / 0.38f;
                    if (row.Progress > 1f) row.Progress = 1f;
                    float k = 1f - (1f - row.Progress) * (1f - row.Progress);
                    row.Root.localPosition = new Vector3(0f, Mathf.Lerp(row.RestY + 0.04f, row.RestY, k), 0f);
                    PaintRow(row, k);
                }
            }
        }

        bool RowsSettled()
        {
            for (int i = 0; i < rowCount; i++)
                if (pool[i].Progress < 1f) return false;
            return true;
        }

        void Finish()
        {
            playing = false;
            ApplyAmount(targetCents);
            PaintBar(1f);
            Action callback = done;
            done = null;
            if (callback != null) callback();
        }

        void ApplyAmount(long cents)
        {
            if (cents < 0L) cents = 0L;
            if (cents > MaxCents) cents = MaxCents;
            if (cents == shownCents) return;
            shownCents = cents;

            long whole = cents / 100L;
            int frac = (int)(cents % 100L);
            SetFace(7, frac / 10, true);
            SetFace(8, frac % 10, true);

            bool seen = false;
            for (int i = DollarCols - 1; i >= 0; i--)
            {
                int digit = (int)((whole / Pow[i]) % 10L);
                if (digit != 0) seen = true;
                SetFace(i, digit, seen || i == 0);
            }

            if (commaLo != null) commaLo.enabled = columns[3].Visible;
            if (commaHi != null) commaHi.enabled = columns[6].Visible;
            int high = 0;
            for (int i = DollarCols - 1; i >= 0; i--)
            {
                if (columns[i].Visible) { high = i; break; }
            }
            signTarget = columns[high].X - 0.040f;
        }

        void SetFace(int column, int digit, bool visible)
        {
            Column col = columns[column];
            if (digit < 0) digit = 0;
            if (digit > 9) digit = 9;
            int packed = visible ? digit : -1;
            col.Visible = visible;
            if (col.Shown == packed) return;
            col.Shown = packed;
            Renderer[] faces = col.Faces;
            for (int i = 0; i < 10; i++) faces[i].enabled = visible && i == digit;
        }

        void TickSign(float dt)
        {
            if (sign == null) return;
            float x = sign.localPosition.x;
            float dx = signTarget - x;
            if (dx < 0.0002f && dx > -0.0002f) return;
            var pos = sign.localPosition;
            pos.x = x + dx * (1f - Mathf.Exp(-10f * dt));
            sign.localPosition = pos;
        }

        void SnapSign()
        {
            if (sign == null) return;
            var pos = sign.localPosition;
            pos.x = signTarget;
            sign.localPosition = pos;
        }

        void PaintBar(float eased)
        {
            if (bar == null) return;
            if (eased < 0f) eased = 0f;
            if (eased > 1f) eased = 1f;
            bar.localScale = new Vector3(Mathf.Lerp(0.02f, barFull, eased), barY, barZ);
        }

        void PaintRow(Row row, float alpha)
        {
            Color color = row.Base;
            color.a = alpha;
            if (row.BaseP) row.Mat.SetColor(CrewLook.BaseId, color);
            if (row.ColorP) row.Mat.SetColor(CrewLook.ColorId, color);
        }

        void EnsureRows(int count)
        {
            while (pool.Count < count) pool.Add(MakeRow());
        }

        Row MakeRow()
        {
            var row = new Row();
            row.Label = CrewLook.Label(transform, "CostLine", "", LineHeight, SourcedInk, TextAnchor.MiddleCenter, FontStyle.Normal, texts);
            row.Root = row.Label.transform;
            row.Rend = row.Label.GetComponent<MeshRenderer>();
            row.Mat = row.Rend.sharedMaterial;
            row.BaseP = CrewLook.HasBase(row.Mat);
            row.ColorP = CrewLook.HasColor(row.Mat);
            row.Base = SourcedInk;
            row.Rend.enabled = false;
            return row;
        }

        void SetDisclaimer(string text)
        {
            if (string.IsNullOrEmpty(text)) text = DefaultDisclaimer;
            text = Wrap(text, 56);
            if (disclaimer != null && disclaimer.text != text) disclaimer.text = text;
        }

        void LayoutColumns()
        {
            const float pitch = 0.048f;
            const float ones = -0.02f;
            for (int i = 0; i < Columns; i++) columns[i] = new Column();
            for (int i = 0; i < DollarCols; i++)
            {
                float gap = 0f;
                if (i >= 3) gap += 0.02f;
                if (i >= 6) gap += 0.02f;
                columns[i].X = ones - i * pitch - gap;
            }
            float dot = ones + pitch * 0.78f;
            columns[7].X = dot + pitch * 0.85f;
            columns[8].X = columns[7].X + pitch;
        }

        float DotX()
        {
            const float pitch = 0.048f;
            return -0.02f + pitch * 0.78f;
        }

        float CommaLoX()
        {
            return (columns[3].X + columns[2].X) * 0.5f;
        }

        float CommaHiX()
        {
            return (columns[6].X + columns[5].X) * 0.5f;
        }

        static string Describe(Line line)
        {
            string label = line.Label ?? "";
            string money = FormatUsd(line.Usd);
            string row = label.Length == 0 ? money : label + "    " + money;
            if (line.Assumed) row += "    assumed";
            if (!string.IsNullOrEmpty(line.Source)) row += "\n" + line.Source;
            return Wrap(row, 52);
        }

        static long Cents(decimal usd)
        {
            if (usd <= 0m) return 0L;
            if (usd > 9999999.99m) usd = 9999999.99m;
            long cents = (long)decimal.Round(usd * 100m, 0, MidpointRounding.AwayFromZero);
            if (cents < 0L) return 0L;
            if (cents > MaxCents) return MaxCents;
            return cents;
        }

        static string FormatUsd(decimal usd)
        {
            long cents = Cents(usd);
            long whole = cents / 100L;
            long frac = cents % 100L;
            string fracText = frac.ToString("00");
            return "$" + Group(whole) + "." + fracText;
        }

        static string Group(long whole)
        {
            string raw = whole.ToString();
            if (raw.Length <= 3) return raw;
            int commas = (raw.Length - 1) / 3;
            var chars = new char[raw.Length + commas];
            int src = raw.Length - 1;
            int dst = chars.Length - 1;
            int group = 0;
            while (src >= 0)
            {
                if (group == 3)
                {
                    chars[dst] = ',';
                    dst--;
                    group = 0;
                }
                chars[dst] = raw[src];
                dst--;
                src--;
                group++;
            }
            return new string(chars);
        }

        static string Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= width) return text ?? "";
            int cut = text.LastIndexOf(' ', width);
            if (cut < width / 3) cut = width;
            string rest = text.Substring(cut).TrimStart();
            string first = text.Substring(0, cut).TrimEnd();
            if (rest.Length > width)
            {
                int cut2 = rest.LastIndexOf(' ', width);
                if (cut2 < 8) cut2 = Mathf.Min(width, rest.Length);
                if (cut2 < rest.Length)
                    rest = rest.Substring(0, cut2).TrimEnd() + "\n" + rest.Substring(cut2).TrimStart();
            }
            return first + "\n" + rest;
        }
    }
}
