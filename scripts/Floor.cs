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
        long t = Prof.Begin();
        InkBatch.Begin(this);
        DrawBody();
        InkBatch.End();
        Prof.End("draw:floor", t);
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
        int saved = Ink.Boil;
        int room = main.World?.Room?.Seed ?? 0;
        Ink.Boil = room % 9973; // static art: one fixed jitter per room
        float hw = Tune.HalfW, hh = Tune.HalfH;
        var biome = main.World?.Map?.Biome ?? Biome.Masmorra;

        // the desk around the page: dark wash with loose cross-hatching
        DrawRect(new Rect2(-hw - 1200, -hh - 900, (hw + 1200) * 2, (hh + 900) * 2), new Color(0.2f, 0.17f, 0.15f));
        var deskHatch = new Color(0f, 0f, 0f, 0.25f);
        Ink.Hatch(this, Rect(-hw - 700, -hh - 500, (hw + 700) * 2, (hh + 500) * 2), 14f, 0.8f, deskHatch, 1.2f, 3);

        // floor paper
        DrawRect(new Rect2(-hw, -hh, hw * 2, hh * 2), PaperOf(biome));
        var stain = new Color(0.78f, 0.7f, 0.55f, 0.1f);
        for (int i = 0; i < 14; i++) // coffee-ish watercolour blooms so the paper is not flat
        {
            var c = new Vector2(Ink.Hash(i + room, 1) * hw * 0.9f, Ink.Hash(i + room, 2) * hh * 0.9f);
            Ink.Fill(this, Ink.Blob(c, 70 + 50 * Mathf.Abs(Ink.Hash(i, 3)), 50 + 40 * Mathf.Abs(Ink.Hash(i, 4)), Ink.Hash(i, 5) * 3f, 500 + i, 0.25f), stain, 500 + i);
        }

        if (biome == Biome.Masmorra)
        {
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

        }
        else DrawGround(biome, room, hw, hh);

        if (biome is Biome.Masmorra or Biome.Floresta)
        {
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

        }

        if (biome == Biome.Masmorra)
        {
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
        }
        else DrawWalls(biome, hw, hh);
        var shade = new Color(Ink.Line, 0.35f);
        Ink.Hatch(this, Rect(-hw, -hh, hw * 2, 16), 5f, 0.9f, shade, 1.2f, 11); // the top wall's shadow on the floor
        Ink.Hatch(this, Rect(-hw, -hh, 12, hh * 2), 5f, 0.9f, shade, 1.2f, 12);
        Ink.Stroke(this, Rect(-hw, -hh, hw * 2, hh * 2), true, 4f, 13);
        Ink.Stroke(this, Rect(-hw - Wall, -hh - Wall, (hw + Wall) * 2, (hh + Wall) * 2), true, 4f, 14);
        Ink.Boil = saved;
    }

    public static Color PaperOf(Biome b) => b switch
    {
        Biome.Floresta => new Color(0.84f, 0.87f, 0.72f),
        Biome.Cinzas => new Color(0.72f, 0.69f, 0.66f),
        Biome.Pantano => new Color(0.78f, 0.79f, 0.64f),
        Biome.Cristal => new Color(0.9f, 0.88f, 0.95f),
        _ => Ink.Paper,
    };

    // ---------------------------------------------------------------- the ground of each biome

    void DrawGround(Biome biome, int room, float hw, float hh)
    {
        switch (biome)
        {
            case Biome.Floresta: Forest(room, hw, hh); break;
            case Biome.Cinzas: Ash(room, hw, hh); break;
            case Biome.Pantano: Swamp(room, hw, hh); break;
            case Biome.Cristal: Crystal(room, hw, hh); break;
        }
    }

    Vector2 Spot(int room, int i, float hw, float hh, float margin = 30f) =>
        new(Ink.Hash(i + room, 61) * (hw - margin), Ink.Hash(i + room, 62) * (hh - margin));

    /// <summary>Forest floor: patches of earth and moss, puddles from the rain, fallen leaves, roots from the edges.</summary>
    void Forest(int room, float hw, float hh)
    {
        for (int i = 0; i < 16; i++) // earth and moss patches
        {
            var c = Spot(room, i, hw, hh);
            bool moss = i % 3 != 0;
            var col = moss ? new Color(0.45f, 0.58f, 0.3f, 0.22f) : new Color(0.55f, 0.42f, 0.28f, 0.2f);
            var blob = Ink.Blob(c, 50 + 40 * Mathf.Abs(Ink.Hash(i, 63)), 36 + 30 * Mathf.Abs(Ink.Hash(i, 64)), Ink.Hash(i, 65) * 3f, 2000 + i, 0.3f, 16);
            Ink.Fill(this, blob, col, 2000 + i);
            if (moss) Ink.Hatch(this, blob, 6f, 1.2f, new Color(0.3f, 0.42f, 0.2f, 0.35f), 1f, 2000 + i);
        }
        for (int i = 0; i < 5; i++) // rain puddles
        {
            var c = Spot(room, i + 40, hw, hh, 90f);
            var pud = Ink.Blob(c, 44 + 24 * Mathf.Abs(Ink.Hash(i, 66)), 22 + 10 * Mathf.Abs(Ink.Hash(i, 67)), 0.1f, 2100 + i, 0.2f, 16);
            Ink.Fill(this, pud, new Color(0.4f, 0.5f, 0.56f, 0.35f), 2100 + i);
            Ink.Stroke(this, pud, true, 1.4f, 2100 + i, new Color(Ink.Line, 0.4f));
            Ink.Line2(this, c + new Vector2(-14, -4), c + new Vector2(6, -6), 1.2f, 2110 + i, new Color(1f, 1f, 1f, 0.5f)); // a glint
        }
        Color[] leaf = [new(0.42f, 0.55f, 0.24f), new(0.7f, 0.55f, 0.2f), new(0.62f, 0.3f, 0.16f), new(0.5f, 0.6f, 0.3f)];
        for (int i = 0; i < 70; i++) // fallen leaves
        {
            var c = Spot(room, i + 100, hw, hh, 14f);
            var d = Vector2.FromAngle(Ink.Hash(i, 68) * 3.14f);
            var n = d.Orthogonal();
            Vector2[] l = [c - d * 7, c + n * 3.5f, c + d * 7, c - n * 3.5f];
            Ink.Shape(this, l, leaf[i % leaf.Length], 2200 + i, 1f);
            DrawLine(c - d * 6, c + d * 6, new Color(Ink.Line, 0.4f), 0.8f);
        }
        for (int i = 0; i < 10; i++) // roots creeping in from the walls
        {
            float t = Ink.Hash(i + room, 69);
            Vector2 a = (i % 4) switch
            {
                0 => new(t * hw, -hh), 1 => new(t * hw, hh), 2 => new(-hw, t * hh), _ => new(hw, t * hh),
            };
            var inward = -a.Normalized();
            var pts = new Vector2[5];
            for (int k = 0; k < 5; k++) pts[k] = a + inward * k * 18f + inward.Orthogonal() * Mathf.Sin(k * 1.3f + i) * 7f;
            Ink.Stroke(this, pts, false, 3.2f - 0.15f * i % 2, 2300 + i, new Color(0.38f, 0.26f, 0.16f, 0.9f));
        }
    }

    /// <summary>Ash caves: hexagonal basalt slabs, some split by glowing ember cracks, and drifts of grey ash.</summary>
    void Ash(int room, float hw, float hh)
    {
        const float R = 34f;
        float dx = R * Mathf.Sqrt(3f), dy = R * 1.5f;
        int row = 0;
        for (float y = -hh - R; y < hh + R; y += dy, row++)
        {
            int col = 0;
            for (float x = -hw - dx + (row % 2) * dx / 2; x < hw + dx; x += dx, col++)
            {
                int seed = 3000 + row * 53 + col + room * 7;
                var hex = new Vector2[6];
                for (int k = 0; k < 6; k++) hex[k] = new Vector2(x, y) + Vector2.FromAngle(Mathf.Pi / 6 + k * Mathf.Pi / 3) * (R - 3) + new Vector2(Ink.Hash(seed, k), Ink.Hash(seed, k + 6)) * 2.5f;
                if (hex.Any(v => Mathf.Abs(v.X) > hw || Mathf.Abs(v.Y) > hh)) continue;
                if (Ink.Hash(seed, 20) > -0.3f) Ink.Fill(this, hex, new Color(0.35f, 0.33f, 0.32f, 0.12f), seed);
                Ink.Stroke(this, hex, true, 1.4f, seed, new Color(Ink.Line, 0.32f));
                if (Ink.Hash(seed, 21) > 0.72f) // an ember crack
                {
                    var c = new Vector2(x, y);
                    var d = Vector2.FromAngle(Ink.Hash(seed, 22) * 3f);
                    Vector2[] crack = [c - d * 20, c - d * 6 + d.Orthogonal() * 5, c + d * 7 - d.Orthogonal() * 4, c + d * 21];
                    Ink.Stroke(this, crack, false, 3.5f, seed + 1, new Color(0.25f, 0.1f, 0.05f));
                    Ink.Stroke(this, crack, false, 1.6f, seed + 2, new Color(1.8f, 0.7f, 0.25f)); // HDR: it glows
                }
            }
        }
        for (int i = 0; i < 10; i++) // ash drifts
        {
            var c = Spot(room, i + 200, hw, hh);
            Ink.Fill(this, Ink.Blob(c, 60 + 40 * Mathf.Abs(Ink.Hash(i, 70)), 26, Ink.Hash(i, 71) * 3f, 3500 + i, 0.3f, 14), new Color(0.82f, 0.8f, 0.78f, 0.18f), 3500 + i);
        }
        for (int i = 0; i < 26; i++) // scattered black rubble
            Ink.Shape(this, Ink.Blob(Spot(room, i + 260, hw, hh, 16f), 4 + 3 * Mathf.Abs(Ink.Hash(i, 72)), 3.5f, Ink.Hash(i, 73) * 3f, 3600 + i, 0.3f, 7), new Color(0.22f, 0.2f, 0.2f), 3600 + i, 1f);
    }

    /// <summary>Swamp: mud, murky pools with lily pads, reeds and cattails at the edges of the water.</summary>
    void Swamp(int room, float hw, float hh)
    {
        for (int i = 0; i < 14; i++)
        {
            var c = Spot(room, i + 300, hw, hh);
            Ink.Fill(this, Ink.Blob(c, 60 + 40 * Mathf.Abs(Ink.Hash(i, 74)), 40 + 26 * Mathf.Abs(Ink.Hash(i, 75)), Ink.Hash(i, 76) * 3f, 4000 + i, 0.3f, 16), new Color(0.45f, 0.4f, 0.26f, 0.2f), 4000 + i);
        }
        for (int i = 0; i < 4; i++) // pools
        {
            var c = Spot(room, i + 340, hw, hh, 160f);
            float rx = 80 + 40 * Mathf.Abs(Ink.Hash(i, 77)), ry = 44 + 20 * Mathf.Abs(Ink.Hash(i, 78));
            var pool = Ink.Blob(c, rx, ry, Ink.Hash(i, 79) * 0.6f, 4100 + i, 0.18f, 22);
            Ink.Fill(this, pool, new Color(0.25f, 0.32f, 0.22f, 0.45f), 4100 + i);
            Ink.Hatch(this, pool, 7f, 0.15f, new Color(0.15f, 0.2f, 0.14f, 0.35f), 1f, 4100 + i);
            Ink.Stroke(this, pool, true, 1.8f, 4100 + i, new Color(Ink.Line, 0.5f));
            for (int k = 0; k < 3; k++) // lily pads
            {
                var lp = c + new Vector2(Ink.Hash(i * 7 + k, 80) * rx * 0.6f, Ink.Hash(i * 7 + k, 81) * ry * 0.5f);
                float a0 = Ink.Hash(i * 7 + k, 82) * 3f;
                var pad = new List<Vector2> { lp };
                for (int q = 0; q <= 10; q++) pad.Add(lp + Vector2.FromAngle(a0 + 0.5f + q * (Mathf.Tau - 1f) / 10f) * 9f);
                Ink.Shape(this, [.. pad], new Color(0.42f, 0.6f, 0.3f), 4200 + i * 7 + k, 1.2f);
            }
            for (int k = 0; k < 9; k++) // reeds and cattails around the pool
            {
                float a = k / 9f * Mathf.Tau + Ink.Hash(i, k) * 0.3f;
                var root = c + new Vector2(Mathf.Cos(a) * (rx + 6), Mathf.Sin(a) * (ry + 6));
                var top = root + new Vector2(Ink.Hash(i, k + 20) * 5f, -22 - 8 * Mathf.Abs(Ink.Hash(i, k + 30)));
                Ink.Line2(this, root, top, 1.4f, 4300 + i * 10 + k, new Color(0.35f, 0.42f, 0.22f));
                if (k % 3 == 0) Ink.Shape(this, Ink.Blob(top + new Vector2(0, 5), 2.6f, 6f, Mathf.Pi / 2, 4400 + i * 10 + k, 0.05f, 8), new Color(0.45f, 0.3f, 0.18f), 4400 + i * 10 + k, 1f);
            }
        }
    }

    /// <summary>Crystal temple: a pale diamond lattice, rune circles, and clusters of glowing crystal by the walls.</summary>
    void Crystal(int room, float hw, float hh)
    {
        const float step = 64f;
        for (float k = -hw - hh; k < hw + hh; k += step) // the lattice: two families of diagonals, clipped to the floor
        {
            foreach (int sign in new[] { 1, -1 })
            {
                var a0 = ClipDiag(k, sign, hw, hh, true);
                var a1 = ClipDiag(k, sign, hw, hh, false);
                if (a1.X - a0.X > 4f) Ink.Line2(this, a0, a1, 1.1f, 5000 + (int)k * 2 + sign, new Color(0.45f, 0.38f, 0.6f, 0.3f));
            }
        }
        for (int i = 0; i < 3; i++) // rune circles
        {
            var c = Spot(room, i + 400, hw, hh, 140f);
            Ink.Stroke(this, Ink.Circle(c, 60f, 5200 + i), true, 1.6f, 5200 + i, new Color(0.5f, 0.4f, 0.7f, 0.5f));
            Ink.Stroke(this, Ink.Circle(c, 48f, 5210 + i), true, 1.1f, 5210 + i, new Color(0.5f, 0.4f, 0.7f, 0.4f));
            for (int k = 0; k < 6; k++)
            {
                var d = Vector2.FromAngle(k * Mathf.Tau / 6 + i);
                Ink.Line2(this, c + d * 50f, c + d * 58f + d.Orthogonal() * 4f, 1.4f, 5220 + i * 6 + k, new Color(0.5f, 0.4f, 0.7f, 0.6f));
            }
        }
        for (int i = 0; i < 18; i++) // crystal clusters along the walls
        {
            float t = Ink.Hash(i + room, 83);
            Vector2 at = (i % 4) switch
            {
                0 => new(t * hw, -hh + 16), 1 => new(t * hw, hh - 14), 2 => new(-hw + 16, t * hh), _ => new(hw - 16, t * hh),
            };
            for (int k = 0; k < 3; k++)
            {
                var d = Vector2.FromAngle(-Mathf.Pi / 2 + (k - 1) * 0.5f + Ink.Hash(i, k + 40) * 0.2f);
                float len = 12f + 9f * Mathf.Abs(Ink.Hash(i, k + 50));
                var n = d.Orthogonal();
                Vector2[] shard = [at + n * 4, at + d * len * 0.75f + n * 4, at + d * len, at + d * len * 0.75f - n * 4, at - n * 4];
                Ink.Shape(this, shard, new Color(1.05f, 0.95f, 1.4f, 0.85f), 5300 + i * 3 + k, 1.3f);
            }
        }
    }

    static Vector2 ClipDiag(float k, int sign, float hw, float hh, bool start)
    {
        // the line y = sign * (x - k); walk it from one edge of the floor to the other
        float x0 = Mathf.Max(-hw, sign > 0 ? k - hh : k - hh), x1 = Mathf.Min(hw, k + hh);
        if (sign < 0) { x0 = Mathf.Max(-hw, -k - hh); x1 = Mathf.Min(hw, -k + hh); }
        float x = start ? x0 : x1;
        float y = sign > 0 ? x - k : -x - k;
        return new Vector2(x, Mathf.Clamp(y, -hh, hh));
    }

    // ---------------------------------------------------------------- walls

    void DrawWalls(Biome biome, float hw, float hh)
    {
        const float W = Wall;
        Rect2[] bands =
        [
            new(-hw - W, -hh - W, hw * 2 + W * 2, W), new(-hw - W, hh, hw * 2 + W * 2, W),
            new(-hw - W, -hh, W, hh * 2), new(hw, -hh, W, hh * 2),
        ];
        int bi = 0;
        foreach (var b in bands)
        {
            bool horiz = b.Size.X > b.Size.Y;
            float len = horiz ? b.Size.X : b.Size.Y;
            Vector2 Along(float t, float across) => horiz ? new Vector2(b.Position.X + t, b.Position.Y + across) : new Vector2(b.Position.X + across, b.Position.Y + t);
            switch (biome)
            {
                case Biome.Floresta: // a hedge of dark leaves with trunks
                    DrawRect(b, new Color(0.22f, 0.33f, 0.18f));
                    for (float t = 8; t < len; t += 22f)
                        for (int r = 0; r < 2; r++)
                        {
                            int seed = 6000 + bi * 400 + (int)t + r;
                            Ink.Shape(this, Ink.Blob(Along(t + r * 11, 12 + r * 18), 14, 11, Ink.Hash(seed, 1) * 3, seed, 0.25f, 10), new Color(0.3f + 0.08f * r, 0.45f, 0.24f), seed, 1.3f, 3f);
                        }
                    for (float t = 40; t < len; t += 140f)
                        Ink.Shape(this, Ink.Circle(Along(t, W * 0.5f), 9f, 6900 + bi * 30 + (int)(t / 10)), new Color(0.4f, 0.28f, 0.17f), 6900 + bi * 30 + (int)(t / 10), 2f);
                    break;
                case Biome.Cinzas: // jagged dark rock with ember veins
                    DrawRect(b, new Color(0.24f, 0.22f, 0.22f));
                    for (float t = 0; t < len; t += 34f)
                    {
                        int seed = 7000 + bi * 400 + (int)t;
                        var c = Along(t + 17, W * 0.5f);
                        Ink.Shape(this, Ink.Blob(c, 20, 17, Ink.Hash(seed, 1) * 3, seed, 0.3f, 7), new Color(0.3f, 0.28f, 0.27f), seed, 1.6f, 4f);
                        if (Ink.Hash(seed, 2) > 0.5f) Ink.Line2(this, c - new Vector2(6, 4), c + new Vector2(5, 6), 1.4f, seed + 1, new Color(1.6f, 0.6f, 0.2f));
                    }
                    break;
                case Biome.Pantano: // a rotting wooden palisade
                    DrawRect(b, new Color(0.38f, 0.3f, 0.2f));
                    for (float t = 0; t < len; t += 16f)
                    {
                        int seed = 8000 + bi * 400 + (int)t;
                        var a0 = Along(t, 2); var a1 = Along(t, W - 2);
                        var a2 = Along(t + 14, W - 2); var a3 = Along(t + 14, 2);
                        Ink.Shape(this, [a0, a3, a2, a1], new Color(0.45f + 0.05f * Ink.Hash(seed, 1), 0.36f, 0.24f), seed, 1.3f, 0f);
                        if (Ink.Hash(seed, 3) > 0.4f) Ink.Line2(this, Along(t + 4, 8), Along(t + 6, W - 10), 0.9f, seed + 1, new Color(Ink.Line, 0.5f)); // grain
                        if (Ink.Hash(seed, 4) > 0.75f) Ink.Fill(this, Ink.Blob(Along(t + 7, W * 0.6f), 6, 4, 0, seed + 2, 0.3f, 8), new Color(0.35f, 0.5f, 0.25f, 0.6f), seed + 2); // moss
                    }
                    break;
                case Biome.Cristal: // pale stone blocks with crystal veins
                    DrawRect(b, new Color(0.72f, 0.7f, 0.8f));
                    for (float t = 0; t < len; t += 48f)
                    {
                        int seed = 9000 + bi * 400 + (int)t;
                        Ink.Stroke(this, [Along(t, 2), Along(t, W - 2)], false, 1.4f, seed, new Color(Ink.Line, 0.5f));
                        if (Ink.Hash(seed, 1) > 0.3f) Ink.Line2(this, Along(t + 10, 10), Along(t + 30, W - 12), 1.6f, seed + 1, new Color(1.1f, 0.95f, 1.5f));
                    }
                    Ink.Line2(this, Along(0, W * 0.5f), Along(len, W * 0.5f), 1.2f, 9900 + bi, new Color(Ink.Line, 0.4f));
                    break;
            }
            bi++;
        }
    }

    static Vector2[] Rect(float x, float y, float w, float h) => [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];
}

static class VecExt
{
    public static Vector2 ToGodot(this System.Numerics.Vector2 v) => new(v.X, v.Y);
}
