using Godot;

namespace Prisma;

/// <summary>Short-lived effects drawn over the arena: glass shards, sparks, shock rings and damage numbers.</summary>
public partial class Fx : Node2D
{
    struct Shard { public Vector2 Pos, Vel; public float Rot, Spin, Size, Life, Max; public Color Col; }
    struct Spark { public Vector2 Pos, Vel; public float Life, Max; public Color Col; }
    struct RingFx { public Vector2 Pos; public float R0, R1, Life, Max, Width; public Color Col; }
    struct Text { public Vector2 Pos; public string S; public float Life, Max; public Color Col; public int Size; public string? Kanji; }
    struct StampFx { public Vector2 Pos; public string S; public float Size, Rot, Life, Max; public Color Col; }
    struct PuffFx { public Vector2 Pos, Vel; public float Rot, Scale, Life, Max; public Color Col; public int Tex; }

    readonly List<Shard> shards = [];
    readonly List<Spark> sparks = [];
    readonly List<RingFx> rings = [];
    readonly List<Text> texts = [];
    readonly List<StampFx> stamps = [];
    readonly List<PuffFx> puffs = [];
    readonly Texture2D?[] puffTex =
    [
        Load("res://assets/textures/particles/dust_puff.png"), Load("res://assets/textures/particles/dust_puff2.png"),
    ];

    static Texture2D? Load(string path) => ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    readonly RandomNumberGenerator rng = new();

    public int Count => shards.Count + sparks.Count + rings.Count + texts.Count + puffs.Count + stamps.Count;

    /// <summary>Dust clouds (CC0 smoke sprites tinted): wall hits, bowling, dash starts.</summary>
    public void Puff(Vector2 pos, Color col, float scale, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float life = rng.RandfRange(0.4f, 0.7f);
            puffs.Add(new PuffFx
            {
                Pos = pos + new Vector2(rng.RandfRange(-8, 8), rng.RandfRange(-8, 8)),
                Vel = Vector2.FromAngle(rng.RandfRange(0, Mathf.Tau)) * rng.RandfRange(30, 110),
                Rot = rng.RandfRange(0, Mathf.Tau), Scale = scale * rng.RandfRange(0.7f, 1.2f),
                Life = life, Max = life, Col = col, Tex = rng.RandiRange(0, puffTex.Length - 1),
            });
        }
    }

    public void Shards(Vector2 pos, Color col, int n, float speed, float size)
    {
        for (int i = 0; i < n; i++)
        {
            float a = rng.RandfRange(0, Mathf.Tau), s = rng.RandfRange(0.3f, 1f) * speed, life = rng.RandfRange(0.35f, 0.8f);
            shards.Add(new Shard
            {
                Pos = pos, Vel = Vector2.FromAngle(a) * s, Rot = a, Spin = rng.RandfRange(-14, 14),
                Size = size * rng.RandfRange(0.5f, 1.3f), Life = life, Max = life, Col = col,
            });
        }
    }

    public void Sparks(Vector2 pos, float angle, float spread, Color col, int n, float speed)
    {
        for (int i = 0; i < n; i++)
        {
            float a = angle + rng.RandfRange(-spread, spread), life = rng.RandfRange(0.12f, 0.3f);
            sparks.Add(new Spark { Pos = pos, Vel = Vector2.FromAngle(a) * speed * rng.RandfRange(0.4f, 1f), Life = life, Max = life, Col = col });
        }
    }

    public void Ring(Vector2 pos, float r0, float r1, float time, Color col, float width = 4f) =>
        rings.Add(new RingFx { Pos = pos, R0 = r0, R1 = r1, Life = time, Max = time, Col = col, Width = width });

    /// <summary>A number or word that rises and fades; with a kanji (会心 on a crit) it pops in bigger and lasts longer.</summary>
    public void Number(Vector2 pos, string s, Color col, int size = 22, string? kanji = null)
    {
        float life = kanji != null ? 1.0f : 0.7f;
        texts.Add(new Text { Pos = pos + new Vector2(rng.RandfRange(-10, 10), -20), S = s, Life = life, Max = life, Col = col, Size = size, Kanji = kanji });
    }

    /// <summary>A brush kanji slammed onto the arena (破 on a guard break): it lands big, settles, then fades.</summary>
    public void Stamp(Vector2 pos, string s, float size, Color col, float life = 0.75f) =>
        stamps.Add(new StampFx { Pos = pos, S = s, Size = size, Rot = rng.RandfRange(-0.25f, 0.25f), Life = life, Max = life, Col = col });

    public int Stamps => stamps.Count;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = shards.Count - 1; i >= 0; i--)
        {
            var s = shards[i];
            s.Life -= dt;
            if (s.Life <= 0) { shards.RemoveAt(i); continue; }
            s.Pos += s.Vel * dt; s.Vel *= Mathf.Max(0, 1 - 3.5f * dt); s.Rot += s.Spin * dt;
            shards[i] = s;
        }
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var s = sparks[i];
            s.Life -= dt;
            if (s.Life <= 0) { sparks.RemoveAt(i); continue; }
            s.Pos += s.Vel * dt; s.Vel *= Mathf.Max(0, 1 - 6f * dt);
            sparks[i] = s;
        }
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            var r = rings[i];
            r.Life -= dt;
            if (r.Life <= 0) { rings.RemoveAt(i); continue; }
            rings[i] = r;
        }
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            var p = puffs[i];
            p.Life -= dt;
            if (p.Life <= 0) { puffs.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt; p.Vel *= Mathf.Max(0, 1 - 3f * dt); p.Rot += dt;
            puffs[i] = p;
        }
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var t = texts[i];
            t.Life -= dt;
            if (t.Life <= 0) { texts.RemoveAt(i); continue; }
            t.Pos += new Vector2(0, -60 * dt);
            texts[i] = t;
        }
        for (int i = stamps.Count - 1; i >= 0; i--)
        {
            var s = stamps[i];
            s.Life -= dt;
            if (s.Life <= 0) { stamps.RemoveAt(i); continue; }
            stamps[i] = s;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        long t = Prof.Begin();
        DrawBody();
        Prof.End("draw:fx", t);
    }

    void DrawBody()
    {
        foreach (var p in puffs)
        {
            var tex = puffTex[p.Tex];
            if (tex == null) continue;
            float k = 1 - p.Life / p.Max;
            var c = p.Col; c.A *= 1 - k;
            float s = p.Scale * (0.6f + 0.8f * k);
            DrawSetTransform(p.Pos, p.Rot, new Vector2(s, s));
            DrawTexture(tex, -tex.GetSize() / 2, c);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        foreach (var r in rings)
        {
            float k = 1 - r.Life / r.Max, e = 1 - (1 - k) * (1 - k);
            var c = r.Col; c.A *= 1 - k;
            DrawArc(r.Pos, Mathf.Lerp(r.R0, r.R1, e), 0, Mathf.Tau, 64, c, r.Width * (1 - k * 0.6f));
        }
        foreach (var s in shards)
        {
            var c = s.Col; c.A *= Mathf.Clamp(s.Life / s.Max * 1.5f, 0, 1);
            var f = Vector2.FromAngle(s.Rot) * s.Size;
            var side = f.Orthogonal() * 0.45f;
            DrawColoredPolygon([s.Pos + f, s.Pos - f * 0.5f + side, s.Pos - f * 0.5f - side], c);
        }
        foreach (var s in sparks)
        {
            var c = s.Col; c.A *= s.Life / s.Max;
            DrawLine(s.Pos, s.Pos - s.Vel * 0.035f, c, 3f);
        }
        var font = Ink.Hand;
        foreach (var t in texts)
        {
            float a = Mathf.Clamp(t.Life * 3, 0, 1), age = t.Max - t.Life;
            var c = t.Col; c.A *= a;
            var halo = new Color(Ink.Paper, 0.85f * a);
            float pop = t.Kanji != null ? 1f + 0.7f * Mathf.Max(0f, 1f - age / 0.12f) : 1f;
            DrawSetTransform(t.Pos, 0f, new Vector2(pop, pop));
            DrawStringOutline(font, new Vector2(-60, 0), t.S, HorizontalAlignment.Center, 120, t.Size, Mathf.Max(4, t.Size / 6), halo);
            DrawString(font, new Vector2(-60, 0), t.S, HorizontalAlignment.Center, 120, t.Size, c);
            if (t.Kanji != null)
            {
                int ks = t.Size / 2;
                DrawStringOutline(Hud.Kanji, new Vector2(-60, -t.Size * 0.85f), t.Kanji, HorizontalAlignment.Center, 120, ks, 4, halo);
                DrawString(Hud.Kanji, new Vector2(-60, -t.Size * 0.85f), t.Kanji, HorizontalAlignment.Center, 120, ks, new Color(Ink.Line, a));
            }
        }
        foreach (var s in stamps)
        {
            float age = s.Max - s.Life, k = Mathf.Clamp(age / 0.1f, 0f, 1f);
            float scale = Mathf.Lerp(1.8f, 1f, 1f - (1f - k) * (1f - k));
            float a = Mathf.Clamp(s.Life / (s.Max * 0.4f), 0f, 1f) * Mathf.Clamp(age / 0.04f, 0f, 1f);
            DrawSetTransform(s.Pos, s.Rot, new Vector2(scale, scale));
            int size = (int)s.Size;
            var at = new Vector2(-size, size * 0.35f);
            DrawStringOutline(Hud.Kanji, at, s.S, HorizontalAlignment.Center, size * 2, size, Mathf.Max(6, size / 7), new Color(Ink.Paper, 0.9f * a));
            DrawString(Hud.Kanji, at, s.S, HorizontalAlignment.Center, size * 2, size, new Color(s.Col, a));
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
