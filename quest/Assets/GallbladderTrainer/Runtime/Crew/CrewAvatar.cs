using System.Collections.Generic;
using UnityEngine;

namespace GallbladderTrainer.Crew
{
    /// <summary>One primitive crew member. Driven from CrewDisplay.Tick. Idle motion never claims the service ran.</summary>
    sealed class CrewAvatar
    {
        const float PulseSeconds = 0.82f;

        static readonly string[] Names = { "Claude", "Cartesia", "Apify", "x402", "Convex" };
        static readonly string[] Roles =
        {
            "writes the words",
            "the voice",
            "fetched prices",
            "payment for the report",
            "live sync to instructor"
        };

        static readonly Color DimWord = new Color(0.58f, 0.62f, 0.66f, 1f);

        struct Tone
        {
            public Material Mat;
            public Color Base;
            public float Floor;
            public bool Glow;
            public bool BaseP;
            public bool ColorP;
            public bool EmitP;
        }

        Provider provider;
        ProviderState state = ProviderState.Unknown;
        public Transform Root;

        Transform bob;
        Transform starA;
        Transform starB;
        Transform pen;
        Quaternion penBase;
        Transform[] mouth;
        Vector3[] mouthBase;
        Transform[] legs;
        Quaternion[] legBase;
        Transform mag;
        Quaternion magBase;
        Transform coin;
        Quaternion coinBase;
        Transform[] rings;
        Quaternion[] ringBase;
        float[] ringSpeed;
        float[] ringAngle;
        TextMesh status;
        Material statusMat;
        bool statusBase;
        bool statusColor;
        TextMesh caption;
        Renderer captionRend;
        Material captionMat;
        bool captionBase;
        bool captionColor;
        TextMesh lensNote;
        Renderer lensRend;
        Color accent;
        Tone[] tones;
        List<Tone> toneList;

        float phase;
        float level = 0.62f;
        float levelTarget = 0.62f;
        float speakShown;
        float speakTarget;
        float orbit;
        bool pulsing;
        float pulsePhase;

        public static CrewAvatar Build(Provider who, Transform parent, int index, CrewTextRegistry registry)
        {
            var avatar = new CrewAvatar();
            avatar.provider = who;
            avatar.phase = index * 0.73f;
            avatar.accent = Accent(who);
            avatar.Root = CrewLook.Empty(parent, Names[(int)who], Vector3.zero);
            avatar.bob = CrewLook.Empty(avatar.Root, "Bob", Vector3.zero);
            avatar.toneList = new List<Tone>(12);
            avatar.Construct(registry);
            avatar.tones = avatar.toneList.ToArray();
            avatar.toneList = null;
            avatar.ApplyColors();
            return avatar;
        }

        public void SetState(ProviderState next)
        {
            state = next;
            string word = Word(next);
            if (status != null && status.text != word) status.text = word;
            levelTarget = LevelFor(next);
            UpdateLens();
        }

        public void Pulse(string action)
        {
            pulsing = true;
            pulsePhase = 0f;
            if (caption == null) return;
            bool show = !string.IsNullOrEmpty(action);
            if (show && caption.text != action) caption.text = action;
            if (captionRend != null) captionRend.enabled = show;
            if (show) PaintCaption(true);
        }

        public void Speak(float amplitude01)
        {
            speakTarget = amplitude01 < 0f ? 0f : (amplitude01 > 1f ? 1f : amplitude01);
        }

        public void Tick(float dt)
        {
            bool moving = false;
            if (Mathf.Abs(level - levelTarget) > 0.0015f)
            {
                level = CrewLook.Damp(level, levelTarget, 6.5f, dt);
                moving = true;
            }
            else level = levelTarget;

            if (Mathf.Abs(speakShown - speakTarget) > 0.0015f)
            {
                speakShown = CrewLook.Damp(speakShown, speakTarget, 18f, dt);
                moving = true;
            }
            else speakShown = speakTarget;

            bool wasPulsing = pulsing;
            if (pulsing)
            {
                pulsePhase += dt / PulseSeconds;
                if (pulsePhase >= 1f)
                {
                    pulsePhase = 1f;
                    pulsing = false;
                }
            }

            float env = pulsing ? Mathf.Sin(pulsePhase * Mathf.PI) : 0f;
            float bobAmp = Mathf.Lerp(0.0022f, 0.0085f, level);
            bob.localPosition = new Vector3(0f, Mathf.Sin(Time.time * 1.65f + phase) * bobAmp, 0f);
            float scale = 1f + env * 0.07f;
            bob.localScale = new Vector3(scale, scale, scale);

            if (moving || pulsing || wasPulsing) ApplyColors();
            PaintCaption(pulsing || wasPulsing);

            switch (provider)
            {
                case Provider.Claude: TickSpark(dt, env); break;
                case Provider.Cartesia: TickSpeaker(env); break;
                case Provider.Apify: TickScout(env); break;
                case Provider.X402: TickCoin(env); break;
                case Provider.Convex: TickOrb(dt, env); break;
            }
        }

        void Construct(CrewTextRegistry registry)
        {
            Material disc = CrewLook.MakeMesh(new Color(0.07f, 0.08f, 0.10f));
            CrewLook.Piece(bob, PrimitiveType.Cylinder, "Disc", new Vector3(0f, 0.004f, 0f), Vector3.zero, new Vector3(0.10f, 0.004f, 0.10f), disc);

            switch (provider)
            {
                case Provider.Claude: BuildClaude(); break;
                case Provider.Cartesia: BuildCartesia(); break;
                case Provider.Apify: BuildApify(registry); break;
                case Provider.X402: BuildX402(registry); break;
                case Provider.Convex: BuildConvex(); break;
            }
            BuildTag(registry);
        }

        void BuildClaude()
        {
            Material shell = Solid(new Color(0.20f, 0.17f, 0.38f), 0.30f, false);
            Material gold = Solid(new Color(1f, 0.78f, 0.38f), 0.22f, false);
            Material lilac = Solid(new Color(0.74f, 0.56f, 1f), 0.22f, false);
            CrewLook.Piece(bob, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.078f, 0f), Vector3.zero, new Vector3(0.050f, 0.046f, 0.046f), shell);
            CrewLook.Piece(bob, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.150f, -0.006f), Vector3.zero, new Vector3(0.058f, 0.058f, 0.058f), shell);
            CrewLook.Piece(bob, PrimitiveType.Cylinder, "Collar", new Vector3(0f, 0.112f, 0f), Vector3.zero, new Vector3(0.034f, 0.007f, 0.034f), gold);
            starA = CrewLook.Empty(bob, "StarA", new Vector3(0.04f, 0.162f, -0.02f));
            starB = CrewLook.Empty(bob, "StarB", new Vector3(-0.04f, 0.150f, -0.016f));
            Spark(starA, gold);
            Spark(starB, lilac);
            pen = CrewLook.Piece(bob, PrimitiveType.Cylinder, "Pen", new Vector3(0.048f, 0.088f, -0.032f), new Vector3(0f, 0f, -58f), new Vector3(0.006f, 0.026f, 0.006f), gold);
            penBase = pen.localRotation;
        }

        static void Spark(Transform pivot, Material mat)
        {
            CrewLook.Piece(pivot, PrimitiveType.Quad, "RayA", Vector3.zero, Vector3.zero, new Vector3(0.036f, 0.036f, 1f), mat);
            CrewLook.Piece(pivot, PrimitiveType.Quad, "RayB", Vector3.zero, new Vector3(0f, 0f, 45f), new Vector3(0.036f, 0.036f, 1f), mat);
        }

        void BuildCartesia()
        {
            Material body = Solid(new Color(0.07f, 0.26f, 0.32f), 0.26f, false);
            Material deep = Solid(new Color(0.04f, 0.14f, 0.18f), 0.34f, false);
            Material glow = Solid(new Color(0.40f, 1f, 0.95f), 0.20f, true);
            CrewLook.Piece(bob, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.096f, 0f), Vector3.zero, new Vector3(0.118f, 0.118f, 0.118f), body);
            CrewLook.Piece(bob, PrimitiveType.Cylinder, "EarL", new Vector3(-0.064f, 0.102f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.030f, 0.006f, 0.030f), deep);
            CrewLook.Piece(bob, PrimitiveType.Cylinder, "EarR", new Vector3(0.064f, 0.102f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.030f, 0.006f, 0.030f), deep);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.108f + i * 0.013f;
                CrewLook.Piece(bob, PrimitiveType.Cube, "Grille", new Vector3(0f, y, -0.060f), Vector3.zero, new Vector3(0.064f, 0.0045f, 0.004f), glow);
            }
            mouth = new Transform[5];
            mouthBase = new Vector3[5];
            float[] gain = { 0.45f, 0.75f, 1f, 0.8f, 0.5f };
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 0.0145f;
                float h = 0.010f + gain[i] * 0.022f;
                mouth[i] = CrewLook.Piece(bob, PrimitiveType.Cube, "Mouth", new Vector3(x, 0.064f, -0.060f), Vector3.zero, new Vector3(0.008f, h, 0.005f), glow);
                mouthBase[i] = mouth[i].localScale;
            }
        }

        void BuildApify(CrewTextRegistry registry)
        {
            Material body = Solid(new Color(0.14f, 0.34f, 0.22f), 0.26f, false);
            Material limb = Solid(new Color(0.09f, 0.20f, 0.13f), 0.32f, false);
            Material amber = Solid(new Color(1f, 0.72f, 0.32f), 0.22f, false);
            Material glass = Solid(new Color(0.55f, 0.95f, 1f), 0.22f, false);
            CrewLook.Piece(bob, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.070f, 0f), Vector3.zero, new Vector3(0.062f, 0.056f, 0.062f), body);
            CrewLook.Piece(bob, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.118f, -0.008f), Vector3.zero, new Vector3(0.046f, 0.044f, 0.046f), body);
            legs = new Transform[6];
            legBase = new Quaternion[6];
            for (int i = 0; i < 6; i++)
            {
                legs[i] = CrewLook.Empty(bob, "Leg", new Vector3(0f, 0.062f, 0f));
                legs[i].localRotation = Quaternion.Euler(62f, i * 60f, 0f);
                legBase[i] = legs[i].localRotation;
                CrewLook.Piece(legs[i], PrimitiveType.Capsule, "Limb", new Vector3(0f, 0.042f, 0f), Vector3.zero, new Vector3(0.012f, 0.040f, 0.012f), limb);
            }
            mag = CrewLook.Empty(bob, "Magnifier", new Vector3(0.034f, 0.108f, -0.028f));
            magBase = Quaternion.Euler(16f, -28f, 12f);
            mag.localRotation = magBase;
            const int segments = 10;
            const float radius = 0.022f;
            for (int i = 0; i < segments; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / segments;
                CrewLook.Piece(mag, PrimitiveType.Cube, "Rim", new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f), new Vector3(0f, 0f, -a * Mathf.Rad2Deg), new Vector3(0.007f, 0.007f, 0.005f), amber);
            }
            CrewLook.Piece(mag, PrimitiveType.Cylinder, "Lens", Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.032f, 0.0016f, 0.032f), glass);
            CrewLook.Piece(mag, PrimitiveType.Cylinder, "Handle", new Vector3(-0.026f, -0.030f, 0.012f), new Vector3(0f, 0f, 58f), new Vector3(0.005f, 0.026f, 0.005f), amber);
            Color noteColor = new Color(0.85f, 0.95f, 1f);
            lensNote = CrewLook.Label(mag, "LensNote", "", 0.012f, noteColor, TextAnchor.MiddleCenter, FontStyle.Bold, registry);
            lensNote.transform.localPosition = new Vector3(0f, -0.042f, -0.006f);
            lensRend = lensNote.GetComponent<MeshRenderer>();
            lensRend.enabled = false;
            Track(lensNote, noteColor, 0.45f);
        }

        void BuildX402(CrewTextRegistry registry)
        {
            Material bronze = Solid(new Color(0.62f, 0.40f, 0.14f), 0.26f, false);
            Material gold = Solid(new Color(1f, 0.82f, 0.36f), 0.20f, false);
            coin = CrewLook.Empty(bob, "Coin", new Vector3(0f, 0.108f, 0f));
            coinBase = Quaternion.identity;
            CrewLook.Piece(coin, PrimitiveType.Cylinder, "Rim", Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.086f, 0.006f, 0.086f), bronze);
            CrewLook.Piece(coin, PrimitiveType.Cylinder, "Face", new Vector3(0f, 0f, -0.008f), new Vector3(90f, 0f, 0f), new Vector3(0.064f, 0.005f, 0.064f), gold);
            CrewLook.Piece(coin, PrimitiveType.Sphere, "Pip", new Vector3(0f, 0f, -0.014f), Vector3.zero, new Vector3(0.016f, 0.016f, 0.016f), bronze);
            Material board = CrewLook.MakeMesh(new Color(0.08f, 0.07f, 0.05f));
            Material edge = Solid(new Color(1f, 0.82f, 0.36f), 0.24f, false);
            CrewLook.Piece(bob, PrimitiveType.Cube, "TagEdge", new Vector3(0f, 0.018f, -0.058f), Vector3.zero, new Vector3(0.186f, 0.064f, 0.006f), edge);
            CrewLook.Piece(bob, PrimitiveType.Cube, "TagBoard", new Vector3(0f, 0.018f, -0.064f), Vector3.zero, new Vector3(0.172f, 0.050f, 0.005f), board);
            CrewLook.Piece(bob, PrimitiveType.Cylinder, "Arm", new Vector3(0.01f, 0.055f, -0.03f), new Vector3(78f, 0f, 0f), new Vector3(0.008f, 0.028f, 0.008f), bronze);
            TextMesh tag = CrewLook.Label(bob, "UsdcTag", "itemized report:\n0.01 USDC (testnet)", 0.012f, new Color(1f, 0.90f, 0.55f), TextAnchor.MiddleCenter, FontStyle.Bold, registry);
            tag.transform.localPosition = new Vector3(0f, 0.018f, -0.070f);
            Track(tag, new Color(1f, 0.90f, 0.55f), 0.55f);
        }

        void BuildConvex()
        {
            Material core = Solid(new Color(0.36f, 0.58f, 1f), 0.22f, false);
            Material hot = Solid(new Color(0.78f, 0.90f, 1f), 0.28f, false);
            Material a = Solid(new Color(0.45f, 0.92f, 1f), 0.20f, false);
            Material b = Solid(new Color(0.52f, 0.62f, 1f), 0.20f, false);
            Material c = Solid(new Color(0.74f, 0.52f, 1f), 0.20f, false);
            CrewLook.Piece(bob, PrimitiveType.Sphere, "Core", new Vector3(0f, 0.112f, 0f), Vector3.zero, new Vector3(0.070f, 0.070f, 0.070f), core);
            CrewLook.Piece(bob, PrimitiveType.Sphere, "Inner", new Vector3(0f, 0.112f, -0.01f), Vector3.zero, new Vector3(0.028f, 0.028f, 0.028f), hot);
            rings = new Transform[3];
            ringBase = new Quaternion[3];
            ringSpeed = new float[] { 28f, -18f, 34f };
            ringAngle = new float[3];
            rings[0] = Ring(0.056f, 0.008f, new Vector3(78f, 14f, 0f), a);
            rings[1] = Ring(0.076f, 0.0065f, new Vector3(16f, 30f, 74f), b);
            rings[2] = Ring(0.096f, 0.0055f, new Vector3(50f, 0f, 18f), c);
            for (int i = 0; i < rings.Length; i++) ringBase[i] = rings[i].localRotation;
        }

        Transform Ring(float radius, float thick, Vector3 euler, Material mat)
        {
            Transform root = CrewLook.Empty(bob, "Ring", new Vector3(0f, 0.112f, 0f));
            root.localRotation = Quaternion.Euler(euler);
            const int segments = 14;
            for (int i = 0; i < segments; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / segments;
                CrewLook.Piece(root, PrimitiveType.Cube, "Seg",
                    new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius),
                    new Vector3(0f, -a * Mathf.Rad2Deg, 0f),
                    new Vector3(thick * 1.75f, thick, thick * 0.55f), mat);
            }
            return root;
        }

        void BuildTag(CrewTextRegistry registry)
        {
            int index = (int)provider;
            Material plate = CrewLook.MakeMesh(new Color(0.055f, 0.065f, 0.080f));
            Material edge = Solid(accent, 0.32f, false);
            CrewLook.Piece(bob, PrimitiveType.Cube, "TagEdge", new Vector3(0f, 0.252f, 0.014f), Vector3.zero, new Vector3(0.214f, 0.104f, 0.004f), edge);
            CrewLook.Piece(bob, PrimitiveType.Cube, "TagPlate", new Vector3(0f, 0.252f, 0.010f), Vector3.zero, new Vector3(0.200f, 0.090f, 0.004f), plate);
            Color nameColor = Color.white;
            Color roleColor = new Color(0.78f, 0.84f, 0.90f);
            TextMesh nameMesh = CrewLook.Label(bob, "Name", Names[index], 0.024f, nameColor, TextAnchor.MiddleCenter, FontStyle.Bold, registry);
            nameMesh.transform.localPosition = new Vector3(0f, 0.274f, -0.002f);
            Track(nameMesh, nameColor, 0.62f);
            TextMesh roleMesh = CrewLook.Label(bob, "Role", Roles[index], 0.0135f, roleColor, TextAnchor.MiddleCenter, FontStyle.Normal, registry);
            roleMesh.transform.localPosition = new Vector3(0f, 0.248f, -0.002f);
            Track(roleMesh, roleColor, 0.55f);
            status = CrewLook.Label(bob, "Status", "standby", 0.0145f, accent, TextAnchor.MiddleCenter, FontStyle.Bold, registry);
            status.transform.localPosition = new Vector3(0f, 0.224f, -0.002f);
            statusMat = status.GetComponent<MeshRenderer>().sharedMaterial;
            statusBase = CrewLook.HasBase(statusMat);
            statusColor = CrewLook.HasColor(statusMat);
            caption = CrewLook.Label(bob, "Caption", "", 0.016f, accent, TextAnchor.MiddleCenter, FontStyle.Bold, registry);
            caption.transform.localPosition = new Vector3(0f, 0.322f, -0.002f);
            captionRend = caption.GetComponent<MeshRenderer>();
            captionMat = captionRend.sharedMaterial;
            captionBase = CrewLook.HasBase(captionMat);
            captionColor = CrewLook.HasColor(captionMat);
            captionRend.enabled = false;
        }

        Material Solid(Color color, float floor, bool glow)
        {
            Material mat = CrewLook.MakeMesh(color);
            toneList.Add(new Tone
            {
                Mat = mat,
                Base = color,
                Floor = floor,
                Glow = glow,
                BaseP = CrewLook.HasBase(mat),
                ColorP = CrewLook.HasColor(mat),
                EmitP = CrewLook.HasEmit(mat)
            });
            return mat;
        }

        void Track(TextMesh mesh, Color color, float floor)
        {
            if (mesh == null) return;
            Material mat = mesh.GetComponent<MeshRenderer>().sharedMaterial;
            toneList.Add(new Tone
            {
                Mat = mat,
                Base = color,
                Floor = floor,
                Glow = false,
                BaseP = CrewLook.HasBase(mat),
                ColorP = CrewLook.HasColor(mat),
                EmitP = false
            });
        }

        void ApplyColors()
        {
            float boost = 1f + (pulsing ? Mathf.Sin(pulsePhase * Mathf.PI) * 0.16f : 0f);
            float speakBoost = 1f + speakShown * 0.55f;
            int count = tones.Length;
            for (int i = 0; i < count; i++)
            {
                Tone tone = tones[i];
                float mul = Mathf.Lerp(tone.Floor, 1f, level) * boost;
                if (tone.Glow) mul *= speakBoost;
                Color color = tone.Base * mul;
                color.a = tone.Base.a;
                if (tone.BaseP) tone.Mat.SetColor(CrewLook.BaseId, color);
                if (tone.ColorP) tone.Mat.SetColor(CrewLook.ColorId, color);
                if (tone.EmitP) tone.Mat.SetColor(CrewLook.EmitId, color * 0.85f);
            }
            Color word = Color.Lerp(DimWord, accent, level);
            word.a = 1f;
            if (statusBase) statusMat.SetColor(CrewLook.BaseId, word);
            if (statusColor) statusMat.SetColor(CrewLook.ColorId, word);
        }

        void PaintCaption(bool active)
        {
            if (!active || captionMat == null || captionRend == null || !captionRend.enabled) return;
            float alpha = 1f;
            if (pulsePhase > 0.68f) alpha = 1f - (pulsePhase - 0.68f) / 0.32f;
            if (!pulsing) alpha = 0f;
            if (alpha < 0f) alpha = 0f;
            Color color = accent;
            color.a = alpha;
            if (captionBase) captionMat.SetColor(CrewLook.BaseId, color);
            if (captionColor) captionMat.SetColor(CrewLook.ColorId, color);
            if (!pulsing) captionRend.enabled = false;
        }

        void TickSpark(float dt, float env)
        {
            orbit += dt * Mathf.Lerp(0.35f, 0.95f, level);
            if (starA != null)
            {
                starA.localPosition = new Vector3(Mathf.Cos(orbit) * 0.050f, 0.164f + Mathf.Sin(orbit * 2f) * 0.005f, -0.02f);
                starA.localRotation = Quaternion.Euler(0f, 0f, orbit * Mathf.Rad2Deg);
            }
            if (starB != null)
            {
                float b = orbit + Mathf.PI;
                starB.localPosition = new Vector3(Mathf.Cos(b) * 0.046f, 0.148f + Mathf.Sin(b * 2f) * 0.004f, -0.016f);
                starB.localRotation = Quaternion.Euler(0f, 0f, -orbit * Mathf.Rad2Deg * 0.8f);
            }
            if (pen != null)
            {
                float wag = env > 0f ? Mathf.Sin(pulsePhase * Mathf.PI * 4f) * 26f : 0f;
                pen.localRotation = penBase * Quaternion.Euler(0f, 0f, wag);
            }
        }

        void TickSpeaker(float env)
        {
            if (mouth == null) return;
            float drive = speakShown;
            if (drive < env) drive = env;
            float open = Mathf.Lerp(0.30f, 1f, drive);
            for (int i = 0; i < mouth.Length; i++)
            {
                Vector3 scale = mouthBase[i];
                scale.y *= open;
                mouth[i].localScale = scale;
            }
        }

        void TickScout(float env)
        {
            if (legs != null)
            {
                float wiggle = Mathf.Lerp(0.3f, 4.2f, level);
                for (int i = 0; i < legs.Length; i++)
                {
                    float wag = Mathf.Sin(Time.time * 2.1f + phase + i * 0.7f) * wiggle;
                    legs[i].localRotation = legBase[i] * Quaternion.Euler(wag, 0f, 0f);
                }
            }
            if (mag != null)
            {
                float pitch = env > 0f ? env * 34f : 0f;
                mag.localRotation = magBase * Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        void TickCoin(float env)
        {
            if (coin == null) return;
            float rock = Mathf.Sin(Time.time * 0.85f + phase) * 8f;
            float kick = env * 75f;
            coin.localRotation = coinBase * Quaternion.Euler(0f, rock + kick, 0f);
        }

        void TickOrb(float dt, float env)
        {
            if (rings == null) return;
            float mul = Mathf.Lerp(0.28f, 1f, level) * (1f + env * 2.2f);
            for (int i = 0; i < rings.Length; i++)
            {
                ringAngle[i] += ringSpeed[i] * mul * dt;
                rings[i].localRotation = ringBase[i] * Quaternion.AngleAxis(ringAngle[i], Vector3.up);
            }
        }

        void UpdateLens()
        {
            if (lensNote == null || lensRend == null) return;
            if (state == ProviderState.Live)
            {
                if (lensNote.text != "sourced") lensNote.text = "sourced";
                lensRend.enabled = true;
            }
            else if (state == ProviderState.Snapshot)
            {
                if (lensNote.text != "assumed") lensNote.text = "assumed";
                lensRend.enabled = true;
            }
            else lensRend.enabled = false;
        }

        static Color Accent(Provider who)
        {
            switch (who)
            {
                case Provider.Claude: return new Color(1f, 0.78f, 0.38f);
                case Provider.Cartesia: return new Color(0.40f, 1f, 0.95f);
                case Provider.Apify: return new Color(0.55f, 0.95f, 0.62f);
                case Provider.X402: return new Color(1f, 0.82f, 0.36f);
                default: return new Color(0.55f, 0.74f, 1f);
            }
        }

        static float LevelFor(ProviderState next)
        {
            if (next == ProviderState.Fallback || next == ProviderState.Off || next == ProviderState.Unavailable || next == ProviderState.Snapshot)
                return 0f;
            if (next == ProviderState.Used || next == ProviderState.Live || next == ProviderState.Paid || next == ProviderState.Mirrored)
                return 1f;
            return 0.62f;
        }

        static string Word(ProviderState next)
        {
            switch (next)
            {
                case ProviderState.Used: return "used";
                case ProviderState.Fallback: return "fallback";
                case ProviderState.Off: return "off";
                case ProviderState.Live: return "live";
                case ProviderState.Snapshot: return "snapshot";
                case ProviderState.Paid: return "paid";
                case ProviderState.Unpaid: return "unpaid";
                case ProviderState.Mirrored: return "mirrored";
                case ProviderState.Unavailable: return "unavailable";
                default: return "standby";
            }
        }
    }
}
