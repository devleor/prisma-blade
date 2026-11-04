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
        }
        QueueRedraw();
    }

    public override void _Draw()
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
                    else DrawColoredPolygon(Ink.Blob(d.Pos, d.Len, d.Len * 0.7f, d.Seed, (int)d.Seed, 0.2f, 6), new Color(0.55f, 0.53f, 0.52f, 0.6f));
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
