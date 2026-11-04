using Godot;
using Prisma.Sim;

namespace Prisma;

public enum Sky { None, Rain, Drizzle, Ash, Motes }

/// <summary>
/// The weather over a floor, drawn in the same ink: rain as slanted double pen strokes that land as tiny rings and
/// flicks on the floor (with a lightning flash now and then in the forest), drizzle in the swamp, grey ash and rising
/// embers in the caves, glowing motes in the crystal temple. Drawn over everything, under the HUD. Made in code the
/// same way the rain of Ecos da Fenda is (no third-party art).
/// </summary>
public partial class Weather : Node2D
{
    struct Drop { public Vector2 Pos; public float Land, Speed, Len, Life, Seed; }
    struct Splash { public Vector2 Pos; public float T; public int Seed; }

    readonly Main main;
    readonly List<Drop> drops = [];
    readonly List<Splash> splashes = [];
    readonly RandomNumberGenerator rng = new();
    float t, nextFlash = 9f;
    Vector2 wind = new(-0.28f, 1f);

    public Sky Kind { get; private set; } = Sky.None;
    public int Count => drops.Count;
    public int Splashes => splashes.Count;
    public int Flashes { get; private set; }

    public Weather(Main main) { this.main = main; ZIndex = 50; }

    // ---------------------------------------------------------------- sound, synthesized (no sample, no licence)

    const float MixRate = 22050f;
    AudioStreamPlayer? ambience;
    float lp1, lp2, brown, dropEnv, dropPhase, dropFreq, thunder, gust;
    readonly System.Random noise = new(7);

    /// <summary>Frames of ambience written so far (the checks read it).</summary>
    public long Synthesized { get; private set; }

    public override void _Ready()
    {
        AddChild(ambience = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.25f },
            Bus = Settings.SfxBus,
            VolumeDb = -14f,
        });
    }

    float Volume => Kind switch { Sky.Rain => 0.55f, Sky.Drizzle => 0.3f, Sky.Ash => 0.35f, _ => 0f };

    /// <summary>
    /// Rain: white noise through two low-pass stages (the hiss) plus random droplets (short decaying blips at random
    /// pitch); a thunder rumble (brown noise, slow decay) follows each lightning. Ash caves: a low wind that gusts.
    /// </summary>
    void Synth()
    {
        if (ambience == null) return;
        if (Volume <= 0f) { if (ambience.Playing) ambience.Stop(); return; }
        if (!ambience.Playing) ambience.Play();
        if (ambience.GetStreamPlayback() is not AudioStreamGeneratorPlayback pb) return;
        int n = pb.GetFramesAvailable();
        float vol = Volume;
        bool rain = Kind is Sky.Rain or Sky.Drizzle;
        float dropRate = Kind == Sky.Rain ? 0.0035f : 0.0012f;
        for (int i = 0; i < n; i++)
        {
            float w = (float)(noise.NextDouble() * 2.0 - 1.0);
            float s;
            if (rain)
            {
                lp1 += (w - lp1) * 0.35f;
                lp2 += (lp1 - lp2) * 0.5f;
                s = lp2 * 0.6f;
                if (dropEnv < 0.01f && noise.NextDouble() < dropRate) { dropEnv = 0.4f + 0.6f * (float)noise.NextDouble(); dropFreq = 900f + 2600f * (float)noise.NextDouble(); }
                if (dropEnv > 0.001f)
                {
                    dropPhase += dropFreq / MixRate;
                    s += Mathf.Sin(dropPhase * Mathf.Tau) * dropEnv * 0.25f;
                    dropEnv *= 0.993f;
                }
            }
            else
            {
                brown = Mathf.Clamp(brown + w * 0.02f, -1f, 1f);
                gust += ((0.5f + 0.5f * Mathf.Sin(t * 0.7f + i / MixRate)) - gust) * 0.0005f;
                s = brown * (0.4f + 0.6f * gust);
            }
            if (thunder > 0.001f)
            {
                brown = Mathf.Clamp(brown + w * 0.03f, -1f, 1f);
                s += brown * thunder * 1.4f;
                thunder *= 0.99979f; // under 0.001 after ~1.5 s at 22 kHz
            }
            s = Mathf.Clamp(s * vol, -1f, 1f);
            pb.PushFrame(new Vector2(s, s));
        }
        Synthesized += n;
    }

    public static Sky Of(Biome b) => b switch
    {
        Biome.Floresta => Sky.Rain,
        Biome.Pantano => Sky.Drizzle,
        Biome.Cinzas => Sky.Ash,
        Biome.Cristal => Sky.Motes,
        _ => Sky.None,
    };

    public void Set(Sky kind)
    {
        if (kind == Kind) return;
        Kind = kind;
        drops.Clear(); splashes.Clear();
        thunder = 0f;
        int n = kind switch { Sky.Rain => 230, Sky.Drizzle => 110, Sky.Ash => 120, Sky.Motes => 60, _ => 0 };
        for (int i = 0; i < n; i++) drops.Add(Spawn(true));
        nextFlash = rng.RandfRange(6f, 12f);
        QueueRedraw();
    }

    float HW => Tune.HalfW + 60f;
    float HH => Tune.HalfH + 60f;

    Drop Spawn(bool anywhere)
    {
        var d = new Drop { Seed = rng.Randf() * 1000f };
        float x = rng.RandfRange(-HW, HW + 200f);
        switch (Kind)
        {
            case Sky.Rain:
            case Sky.Drizzle:
                d.Land = rng.RandfRange(-Tune.HalfH, Tune.HalfH);
                d.Pos = new Vector2(x, anywhere ? rng.RandfRange(-HH - 300f, d.Land) : -HH - rng.RandfRange(0f, 300f));
                d.Speed = Kind == Sky.Rain ? rng.RandfRange(900f, 1250f) : rng.RandfRange(520f, 700f);
                d.Len = Kind == Sky.Rain ? rng.RandfRange(14f, 26f) : rng.RandfRange(8f, 14f);
                break;
            case Sky.Ash:
                bool ember = rng.Randf() < 0.22f;
                d.Pos = new Vector2(x - 100f, anywhere ? rng.RandfRange(-HH, HH) : (ember ? HH : -HH));
                d.Speed = ember ? -rng.RandfRange(40f, 90f) : rng.RandfRange(25f, 60f); // embers rise
                d.Len = ember ? 0f : rng.RandfRange(2f, 4.5f);
                break;
            case Sky.Motes:
                d.Pos = new Vector2(x - 100f, rng.RandfRange(-HH, HH));
                d.Speed = rng.RandfRange(8f, 20f);
                d.Len = rng.RandfRange(2f, 4f);
                break;
        }
        d.Life = 0f;
        return d;
    }

    public override void _Process(double delta)
    {
        Synth();
        if (Kind == Sky.None) return;
        float dt = (float)delta;
        t += dt;
        wind = new Vector2(-0.28f + 0.08f * Mathf.Sin(t * 0.4f), 1f);
        for (int i = 0; i < drops.Count; i++)
        {
            var d = drops[i];
            d.Life += dt;
            switch (Kind)
            {
                case Sky.Rain:
                case Sky.Drizzle:
                    d.Pos += wind.Normalized() * d.Speed * dt;
                    if (d.Pos.Y >= d.Land)
                    {
                        if (Mathf.Abs(d.Pos.X) < Tune.HalfW) splashes.Add(new Splash { Pos = new Vector2(d.Pos.X, d.Land), T = 0f, Seed = (int)d.Seed });
                        d = Spawn(false);
                    }
                    break;
                case Sky.Ash:
                    d.Pos += new Vector2(-12f + Mathf.Sin(t * 0.8f + d.Seed) * 18f, d.Speed) * dt;
                    if (d.Pos.Y > HH || d.Pos.Y < -HH || d.Pos.X < -HW - 150f) d = Spawn(false);
                    break;
                case Sky.Motes:
                    d.Pos += new Vector2(Mathf.Sin(t * 0.5f + d.Seed) * d.Speed, Mathf.Cos(t * 0.37f + d.Seed * 1.3f) * d.Speed) * dt;
                    break;
            }
            drops[i] = d;
        }
        for (int i = splashes.Count - 1; i >= 0; i--)
        {
            var s = splashes[i];
            s.T += dt;
            if (s.T > 0.28f) splashes.RemoveAt(i); else splashes[i] = s;
        }
        if (Kind == Sky.Rain && (nextFlash -= dt) <= 0f) // lightning over the forest
        {
            nextFlash = rng.RandfRange(8f, 16f);
            Flashes++;
            main.Hud?.Flash(new Color(0.85f, 0.9f, 1f), 0.18f);
            main.Rumble(0.12f);
            thunder = 1f; // the rumble follows the flash
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        long t = Prof.Begin();
        InkBatch.Begin(this);
        DrawBody();
        InkBatch.End();
        Prof.End("draw:weather", t);
    }

    // ---- Draw* inside an ink batch become triangles (see InkBatch); outside one they draw directly ----
    new void DrawCircle(Vector2 position, float radius, Color color, bool filled = true, float width = -1f, bool antialiased = false)
    {
        if (InkBatch.Active(this)) InkBatch.Circle(position, radius, color, filled, width < 0 ? 1f : width);
        else base.DrawCircle(position, radius, color, filled, width, antialiased);
    }

    new void DrawLine(Vector2 from, Vector2 to, Color color, float width = -1f, bool antialiased = false)
    {
        if (InkBatch.Active(this)) InkBatch.Line(from, to, color, width < 0 ? 1f : width);
        else base.DrawLine(from, to, color, width, antialiased);
    }

    new void DrawColoredPolygon(Vector2[] points, Color color, Vector2[]? uvs = null, Texture2D? texture = null)
    {
        if (InkBatch.Active(this) && texture == null) InkBatch.Polygon(points, color);
        else { if (InkBatch.Active(this)) InkBatch.Flush(); base.DrawColoredPolygon(points, color, uvs, texture); }
    }

    new void DrawPolyline(Vector2[] points, Color color, float width = -1f, bool antialiased = false)
    {
        if (InkBatch.Active(this)) InkBatch.Polyline(points, color, width < 0 ? 1f : width);
        else base.DrawPolyline(points, color, width, antialiased);
    }

    new void DrawArc(Vector2 center, float radius, float startAngle, float endAngle, int pointCount, Color color, float width = -1f, bool antialiased = false)
    {
        if (InkBatch.Active(this)) InkBatch.Arc(center, radius, startAngle, endAngle, pointCount, color, width < 0 ? 1f : width);
        else base.DrawArc(center, radius, startAngle, endAngle, pointCount, color, width, antialiased);
    }

    new void DrawRect(Rect2 rect, Color color, bool filled = true, float width = -1f, bool antialiased = false)
    {
        if (InkBatch.Active(this)) InkBatch.Rect(rect, color, filled, width < 0 ? 1f : width);
        else base.DrawRect(rect, color, filled, width, antialiased);
    }

    void DrawBody()
    {
        switch (Kind)
        {
            case Sky.Rain:
            case Sky.Drizzle:
            {
                var ink = new Color(0.16f, 0.2f, 0.3f, Kind == Sky.Rain ? 0.5f : 0.38f);
                var dir = wind.Normalized();
                foreach (var d in drops)
                {
                    var tail = d.Pos - dir * d.Len;
                    var wob = new Vector2(Mathf.Sin(d.Seed + Ink.Boil), 0f) * 0.8f; // the pen line boils a little
                    DrawLine(tail + wob, d.Pos, ink, 1.4f);
                    if (d.Seed % 3 < 1) DrawLine(tail + new Vector2(1.2f, 0), d.Pos + new Vector2(1.2f, -2f), new Color(ink, ink.A * 0.5f), 0.8f);
                }
                foreach (var s in splashes) // a little ring and two flicks where it lands
                {
                    float k = s.T / 0.28f;
                    var c = new Color(0.16f, 0.2f, 0.3f, 0.55f * (1f - k));
                    DrawArc(s.Pos, 2f + 9f * k, 0f, Mathf.Tau, 10, c, 1.1f);
                    DrawLine(s.Pos + new Vector2(-2, 0), s.Pos + new Vector2(-6 - 4 * k, -6 * k), c, 1f);
                    DrawLine(s.Pos + new Vector2(2, 0), s.Pos + new Vector2(6 + 4 * k, -5 * k), c, 1f);
                }
                break;
            }
            case Sky.Ash:
                foreach (var d in drops)
                {
                    if (d.Len <= 0f) // an ember: HDR so it glows
                    {
                        float flick = 0.6f + 0.4f * Mathf.Sin(t * 12f + d.Seed);
                        DrawCircle(d.Pos, 1.8f, new Color(1.9f * flick, 0.7f * flick, 0.25f, 0.9f));
                    }
                    else // a flake: a small tumbling sliver, no polygon to build per frame
                    {
                        var r = Vector2.FromAngle(t * 1.5f + d.Seed) * d.Len;
                        DrawLine(d.Pos - r, d.Pos + r, new Color(0.55f, 0.53f, 0.52f, 0.6f), d.Len * 0.9f);
                    }
                }
                break;
            case Sky.Motes:
                foreach (var d in drops)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2f + d.Seed);
                    DrawCircle(d.Pos, d.Len * (0.7f + 0.5f * pulse), new Color(1.2f, 1.05f, 1.6f, 0.25f + 0.35f * pulse));
                }
                break;
        }
    }
}
