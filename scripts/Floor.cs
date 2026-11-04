using Godot;
using Prisma.Sim;

namespace Prisma;

/// <summary>
/// The arena as a page of a sketchbook: paper floor with hand-drawn flagstones, cracks, tufts and pebbles, and a
/// brick wall band with hatched shade. It never moves, so it is drawn once (no boil) behind everything else.
/// </summary>
public partial class Floor : Node2D
{
    const float Wall = 44f;
    readonly Main main;

    public Floor(Main main) { this.main = main; }

    public override void _Ready()
    {
        ShowBehindParent = true;
        QueueRedraw();
    }

    /// <summary>A new room: new slabs, cracks and tufts.</summary>
    public void Redraw() => QueueRedraw();

    public override void _Draw()
    {
        int saved = Ink.Boil;
        int room = main.World?.Room?.Seed ?? 0;
        Ink.Boil = room % 9973; // static art: one fixed jitter per room
        float hw = Tune.HalfW, hh = Tune.HalfH;

        // the desk around the page: dark wash with loose cross-hatching
        DrawRect(new Rect2(-hw - 1200, -hh - 900, (hw + 1200) * 2, (hh + 900) * 2), new Color(0.2f, 0.17f, 0.15f));
        var deskHatch = new Color(0f, 0f, 0f, 0.25f);
        Ink.Hatch(this, Rect(-hw - 700, -hh - 500, (hw + 700) * 2, (hh + 500) * 2), 14f, 0.8f, deskHatch, 1.2f, 3);

        // floor paper
        DrawRect(new Rect2(-hw, -hh, hw * 2, hh * 2), Ink.Paper);
        var stain = new Color(0.78f, 0.7f, 0.55f, 0.1f);
        for (int i = 0; i < 14; i++) // coffee-ish watercolour blooms so the paper is not flat
        {
            var c = new Vector2(Ink.Hash(i + room, 1) * hw * 0.9f, Ink.Hash(i + room, 2) * hh * 0.9f);
            Ink.Fill(this, Ink.Blob(c, 70 + 50 * Mathf.Abs(Ink.Hash(i, 3)), 50 + 40 * Mathf.Abs(Ink.Hash(i, 4)), Ink.Hash(i, 5) * 3f, 500 + i, 0.25f), stain, 500 + i);
        }

        // flagstones: rows of slabs of uneven width, half-offset row to row
        var stone = new Color(Ink.Line, 0.26f);
        var slabTint = new Color(0.62f, 0.56f, 0.46f, 0.06f);
        const float rowH = 58f;
        int row = 0;
        for (float y = -hh; y < hh; y += rowH, row++)
        {
            float x = -hw - (row % 2) * 40f;
            int col = 0;
            while (x < hw)
            {
                float wdt = 62f + 34f * Mathf.Abs(Ink.Hash(row * 31 + col + room, 9));
                float x0 = Mathf.Max(x, -hw), x1 = Mathf.Min(x + wdt, hw), y1 = Mathf.Min(y + rowH, hh);
                int seed = 1000 + row * 97 + col + room * 13;
                if (x1 - x0 > 10)
                {
                    var q = new[] { new Vector2(x0 + 3, y + 3), new Vector2(x1 - 3, y + 3), new Vector2(x1 - 3, y1 - 3), new Vector2(x0 + 3, y1 - 3) };
                    for (int k = 0; k < 4; k++) q[k] += new Vector2(Ink.Hash(seed, k), Ink.Hash(seed, k + 4)) * 3f;
                    if (Ink.Hash(seed, 20) > 0.2f) Ink.Fill(this, q, slabTint, seed);
                    Ink.Stroke(this, q, true, 1.6f, seed, stone);
                    if (Ink.Hash(seed, 21) > 0.55f) // a hatched worn corner
                    {
                        var corner = new[] { q[2], q[2] + new Vector2(-22, 0), q[2] + new Vector2(0, -18) };
                        Ink.Hatch(this, corner, 4f, 0.9f, new Color(Ink.Line, 0.25f), 1f, seed);
                    }
                    if (Ink.Hash(seed, 22) > 0.75f) // a crack
                    {
                        var a = new Vector2(Mathf.Lerp(x0, x1, 0.3f), y + 6);
                        Ink.Stroke(this, [a, a + new Vector2(8, 14), a + new Vector2(3, 26), a + new Vector2(12, 38)], false, 1.2f, seed, new Color(Ink.Line, 0.4f));
                    }
                }
                x += wdt; col++;
            }
        }

        // tufts and pebbles along the walls and around the pillars
        var grass = new Color(0.36f, 0.42f, 0.24f, 0.8f);
        for (int i = 0; i < 46; i++)
        {
            Vector2 at;
            if (i < 30)
            {
                float t = Ink.Hash(i, 40) * 0.5f + 0.5f;
                at = (i % 4) switch
                {
                    0 => new Vector2(Mathf.Lerp(-hw, hw, t), -hh + 12),
                    1 => new Vector2(Mathf.Lerp(-hw, hw, t), hh - 10),
                    2 => new Vector2(-hw + 12, Mathf.Lerp(-hh, hh, t)),
                    _ => new Vector2(hw - 12, Mathf.Lerp(-hh, hh, t)),
                };
            }
            else
            {
                var pillars = main.World?.Pillars;
                if (pillars == null || pillars.Count == 0) continue;
                var pl = pillars[i % pillars.Count];
                at = pl.Pos.ToGodot() + Vector2.FromAngle(Ink.Hash(i + room, 41) * 3f) * (pl.Radius + 12);
            }
            for (int b = 0; b < 3; b++)
                Ink.Line2(this, at + new Vector2(b * 4 - 4, 0), at + new Vector2(b * 5 - 6 + Ink.Hash(i, b) * 3, -9 - 4 * Mathf.Abs(Ink.Hash(i, b + 5))), 1.4f, 7000 + i * 3 + b, grass);
            if (i % 3 == 0)
                Ink.Shape(this, Ink.Circle(at + new Vector2(10, 4), 3.5f, 8000 + i), new Color(0.7f, 0.66f, 0.58f), 8000 + i, 1.2f);
        }

        // walls: a band of bricks, shaded with hatching on the inner side
        var brick = new Color(0.58f, 0.5f, 0.42f);
        Rect2[] bands =
        [
            new(-hw - Wall, -hh - Wall, hw * 2 + Wall * 2, Wall), new(-hw - Wall, hh, hw * 2 + Wall * 2, Wall),
            new(-hw - Wall, -hh, Wall, hh * 2), new(hw, -hh, Wall, hh * 2),
        ];
        int bi = 0;
        foreach (var b in bands)
        {
            DrawRect(b, brick);
            bool horiz = b.Size.X > b.Size.Y;
            float course = 14.6f;
            for (float o = course; o < (horiz ? b.Size.Y : b.Size.X) - 2; o += course)
            {
                var p0 = horiz ? new Vector2(b.Position.X, b.Position.Y + o) : new Vector2(b.Position.X + o, b.Position.Y);
                var p1 = horiz ? new Vector2(b.End.X, b.Position.Y + o) : new Vector2(b.Position.X + o, b.End.Y);
                Ink.Line2(this, p0, p1, 1.3f, 9000 + bi * 10 + (int)o, new Color(Ink.Line, 0.5f));
            }
            int k = 0;
            for (float s = 0; s < (horiz ? b.Size.X : b.Size.Y); s += 30f, k++)
                for (int c = 0; c < 3; c++)
                {
                    float off = s + (c % 2) * 15f;
                    var p0 = horiz ? new Vector2(b.Position.X + off, b.Position.Y + c * course) : new Vector2(b.Position.X + c * course, b.Position.Y + off);
                    var p1 = horiz ? p0 + new Vector2(0, course) : p0 + new Vector2(course, 0);
                    DrawLine(p0, p1, new Color(Ink.Line, 0.45f), 1.2f);
                }
            bi++;
        }
        var shade = new Color(Ink.Line, 0.35f);
        Ink.Hatch(this, Rect(-hw, -hh, hw * 2, 16), 5f, 0.9f, shade, 1.2f, 11); // the top wall's shadow on the floor
        Ink.Hatch(this, Rect(-hw, -hh, 12, hh * 2), 5f, 0.9f, shade, 1.2f, 12);
        Ink.Stroke(this, Rect(-hw, -hh, hw * 2, hh * 2), true, 4f, 13);
        Ink.Stroke(this, Rect(-hw - Wall, -hh - Wall, (hw + Wall) * 2, (hh + Wall) * 2), true, 4f, 14);
        Ink.Boil = saved;
    }

    static Vector2[] Rect(float x, float y, float w, float h) => [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];
}

static class VecExt
{
    public static Vector2 ToGodot(this System.Numerics.Vector2 v) => new(v.X, v.Y);
}
