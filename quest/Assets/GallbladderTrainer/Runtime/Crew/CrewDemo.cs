using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GallbladderTrainer.Crew
{
    /// <summary>
    /// Drop on an empty GameObject and press Play. Builds the crew and the money ticker and runs a scripted sequence with no backend.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrewDemo : MonoBehaviour
    {
        const string Disclaimer = "Illustrative, not billing data. The per-error adverse-event probability is an assumption.";

        struct Beat
        {
            public float At;
            public float Duration;
            public byte Kind;
            public Provider Who;
            public ProviderState State;
            public string Text;
        }

        // 1 set state, 2 pulse, 3 speak, 4 apply the report map, 5 play the count.
        static readonly Beat[] Beats =
        {
            new Beat { At = 0.7f, Kind = 1, Who = Provider.Claude, State = ProviderState.Used },
            new Beat { At = 0.7f, Kind = 2, Who = Provider.Claude, Text = "wrote the line" },
            new Beat { At = 2.3f, Kind = 1, Who = Provider.Cartesia, State = ProviderState.Unavailable },
            new Beat { At = 3.9f, Kind = 1, Who = Provider.Cartesia, State = ProviderState.Used },
            new Beat { At = 3.9f, Kind = 3, Duration = 2.3f },
            new Beat { At = 6.4f, Kind = 1, Who = Provider.Apify, State = ProviderState.Snapshot },
            new Beat { At = 8.2f, Kind = 1, Who = Provider.Apify, State = ProviderState.Live },
            new Beat { At = 8.2f, Kind = 2, Who = Provider.Apify, Text = "fetched prices" },
            new Beat { At = 9.6f, Kind = 1, Who = Provider.X402, State = ProviderState.Paid },
            new Beat { At = 9.6f, Kind = 2, Who = Provider.X402, Text = "settled the report" },
            new Beat { At = 11.0f, Kind = 1, Who = Provider.Convex, State = ProviderState.Mirrored },
            new Beat { At = 11.0f, Kind = 2, Who = Provider.Convex, Text = "synced the session" },
            new Beat { At = 12.4f, Kind = 4 },
            new Beat { At = 12.6f, Kind = 5 }
        };

        static readonly MoneyCounter.Line[] Empty = new MoneyCounter.Line[0];

        CrewDisplay crew;
        MoneyCounter money;
        MoneyCounter.Line[] lines;
        decimal total;
        Dictionary<string, string> report;
        float clock;
        int next;
        float speakUntil;
        float speakDur;
        bool speaking;

        void Start()
        {
            EnsureRig();
            crew = CrewDisplay.Create(transform, new Vector3(0f, 1.18f, 0.18f));
            money = MoneyCounter.Create(transform, new Vector3(0f, 0.62f, -0.06f));
            money.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);

            lines = new[]
            {
                new MoneyCounter.Line { Label = "OR time", Usd = 186.20m, Assumed = false, Source = "Childers and Maggard-Gibbons, JAMA Surg 2018" },
                new MoneyCounter.Line { Label = "Laparoscopic instruments", Usd = 48.80m, Assumed = false, Source = "Apelgren et al., Surg Endosc 1994" },
                new MoneyCounter.Line { Label = "Clips and trocars", Usd = 150m, Assumed = true, Source = "assumed stand-in, no cited price" },
                new MoneyCounter.Line { Label = "Bile duct injury risk", Usd = 196.26m, Assumed = true, Source = "JAMA Surg 2016 complication cost, 1% assumption" }
            };
            total = 0m;
            for (int i = 0; i < lines.Length; i++) total += lines[i].Usd;
            report = new Dictionary<string, string>
            {
                { "claude", "used" },
                { "cartesia", "used" },
                { "apify", "live" },
                { "x402", "paid" },
                { "convex", "mirrored" }
            };
            money.ShowInstant(Empty, 0m, Disclaimer);
        }

        void Update()
        {
            if (crew == null || money == null) return;
            float dt = Time.deltaTime;
            clock += dt;
            while (next < Beats.Length && clock >= Beats[next].At)
            {
                Fire(Beats[next]);
                next++;
            }
            if (clock < speakUntil)
            {
                float u = speakDur <= 0f ? 1f : 1f - (speakUntil - clock) / speakDur;
                if (u < 0f) u = 0f;
                if (u > 1f) u = 1f;
                float env = Mathf.Sin(u * Mathf.PI);
                float wave = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(clock * 16f));
                crew.Speak(Provider.Cartesia, env * wave);
                speaking = true;
            }
            else if (speaking)
            {
                crew.Speak(Provider.Cartesia, 0f);
                speaking = false;
            }
        }

        void Fire(Beat beat)
        {
            switch (beat.Kind)
            {
                case 1:
                    crew.SetState(beat.Who, beat.State);
                    break;
                case 2:
                    crew.Pulse(beat.Who, beat.Text);
                    break;
                case 3:
                    speakDur = beat.Duration;
                    speakUntil = clock + beat.Duration;
                    break;
                case 4:
                    crew.SetFromReport(report);
                    break;
                case 5:
                    crew.Pulse(Provider.Apify, "sourced and assumed");
                    money.Play(lines, total, Disclaimer, null);
                    break;
            }
        }

        void EnsureRig()
        {
            if (FindAnyObjectByType<Camera>() != null) return;

            var camGo = new GameObject("CrewDemoCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.12f, -1.32f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.085f, 0.10f, 1f);
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.02f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.LookAt(transform.TransformPoint(new Vector3(0f, 0.98f, 0.05f)));

            if (FindAnyObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("CrewDemoLight");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localRotation = Quaternion.Euler(48f, -28f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.97f, 0.92f);
                light.shadows = LightShadows.None;
            }

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.36f, 0.40f);
        }
    }
}
