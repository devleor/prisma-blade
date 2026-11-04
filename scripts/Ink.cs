using Godot;

namespace Prisma;

/// <summary>
/// The hand-drawn look, all in code: ink lines that wobble and "boil" (they are redrawn with a new jitter a few times
/// a second, like hand-drawn animation), watercolour fills printed a little off the line, and hatching for shade.
/// Every shape passes a seed, so its irregularity is its own and stays put between boils.
/// </summary>
public static class Ink
{
    public static readonly Color Line = new(0.11f, 0.09f, 0.08f);
    public static readonly Color Paper = new(0.93f, 0.89f, 0.79f);
    public static readonly Color PaperDark = new(0.74f, 0.68f, 0.58f);

    /// <summary>The current boil step. Main sets it from the frame count (a new one every few frames).</summary>
    public static int Boil;

    /// <summary>Handwritten system font when Windows has one (Ink Free, Segoe Print), the default font otherwise.</summary>
    public static readonly Font Hand = MakeFont();

    static Font MakeFont()
    {
        var f = new SystemFont { FontNames = ["Ink Free", "Segoe Print", "Comic Sans MS", "Bradley Hand"], FontWeight = 600 };
        return f;
    }

    /// <summary>Steady noise in -1..1 for (a, b).</summary>
    public static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 32767.5f - 1f;
        }
    }

    /// <summary>Jitter for point i of a shape, changing with each boil.</summary>
    public static Vector2 J(int seed, int i, float amp) =>
        new Vector2(Hash(seed + Boil * 7919, i * 2), Hash(seed + Boil * 7919, i * 2 + 1)) * amp;

    /// <summary>A closed, slightly lumpy ellipse (rx along the rotation, ry across it).</summary>
    public static Vector2[] Blob(Vector2 c, float rx, float ry, float rot, int seed, float lump = 0.07f, int n = 18)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            float k = 1f + Hash(seed, i) * lump;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx * k, Mathf.Sin(a) * ry * k).Rotated(rot);
        }
        return pts;
    }

    public static Vector2[] Circle(Vector2 c, float r, int seed, float lump = 0.05f) => Blob(c, r, r, 0f, seed, lump, r > 30 ? 26 : 16);

    /// <summary>Points of a shape moved by this boil's jitter.</summary>
    public static Vector2[] Shake(Vector2[] pts, int seed, float amp)
    {
        var o = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) o[i] = pts[i] + J(seed, i, amp);
        return o;
    }

    /// <summary>A flat wash of colour, printed a touch off the ink line like a cheap watercolour print.</summary>
    public static void Fill(CanvasItem ci, Vector2[] pts, Color c, int seed)
    {
        if (pts.Length < 3) return;
        var off = new Vector2(1.6f, 1.4f) + J(seed, 97, 1.2f);
        var moved = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) moved[i] = pts[i] + off;
        if (InkBatch.Active(ci)) { InkBatch.Polygon(moved, c); return; } // a folded wobble is skipped inside
        if (Geometry2D.TriangulatePolygon(moved).Length == 0) return; // a wobble folded it: skip this boil
        ci.DrawColoredPolygon(moved, c);
    }

    /// <summary>
    /// An ink line: a firm pass and a thinner, looser second pass, so it reads as drawn with a pen.
    /// Closed shapes overshoot their start a little, the way a hand closes a loop.
    /// </summary>
    public static void Stroke(CanvasItem ci, Vector2[] pts, bool closed, float w, int seed, Color? col = null, float loose = 1f)
    {
        if (pts.Length < 2) return;
        var c = col ?? Line;
        int n = pts.Length + (closed ? 2 : 0);
        var a = new Vector2[n];
        var b = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            var p = pts[i % pts.Length];
            a[i] = p + J(seed, i, 0.9f * loose);
            b[i] = p + J(seed + 31, i, 2.0f * loose);
        }
        if (closed)
        {
            a[n - 1] = a[n - 2] + (a[n - 1] - a[n - 2]) * 0.4f; // stop a little past the start
            b[0] = b[0] + (b[1] - b[0]) * 0.3f;
        }
        else
        {
            // the pen runs on a little past both ends
            a[0] += (a[0] - a[1]).Normalized() * w * 0.6f;
            a[n - 1] += (a[n - 1] - a[n - 2]).Normalized() * w * 0.6f;
        }
        var thin = c; thin.A *= 0.55f;
        if (InkBatch.Active(ci))
        {
            InkBatch.Polyline(a, c, w);
            InkBatch.Polyline(b, thin, Mathf.Max(1f, w * 0.4f));
            return;
        }
        ci.DrawPolyline(a, c, w, true);
        ci.DrawPolyline(b, thin, Mathf.Max(1f, w * 0.4f), true);
    }

    /// <summary>A single pen stroke between two points.</summary>
    public static void Line2(CanvasItem ci, Vector2 a, Vector2 b, float w, int seed, Color? col = null) =>
        Stroke(ci, [a, (a + b) * 0.5f + J(seed, 5, 1.2f), b], false, w, seed, col);

    /// <summary>Parallel hatching clipped to a polygon.</summary>
    public static void Hatch(CanvasItem ci, Vector2[] poly, float spacing, float angle, Color col, float w, int seed)
    {
        if (poly.Length < 3) return;
        var dir = Vector2.FromAngle(angle);
        var nrm = dir.Orthogonal();
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in poly) { float t = p.Dot(nrm); lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t); }
        var hits = new List<float>(8);
        int line = 0;
        for (float t = lo + spacing * 0.5f; t < hi; t += spacing, line++)
        {
            hits.Clear();
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                float da = a.Dot(nrm) - t, db = b.Dot(nrm) - t;
                if (da * db >= 0f) continue;
                var x = a + (b - a) * (da / (da - db));
                hits.Add(x.Dot(dir));
            }
            hits.Sort();
            for (int k = 0; k + 1 < hits.Count; k += 2)
            {
                float s0 = hits[k] + 1.5f + Hash(seed, line) * 2f, s1 = hits[k + 1] - 1.5f + Hash(seed + 3, line) * 2f;
                if (s1 - s0 < 2f) continue;
                var p0 = nrm * t + dir * s0 + J(seed, line, 0.8f);
                var p1 = nrm * t + dir * s1 + J(seed + 7, line, 0.8f);
                if (InkBatch.Active(ci)) InkBatch.Line(p0, p1, col, w);
                else ci.DrawLine(p0, p1, col, w, true);
            }
        }
    }

    /// <summary>The shaded crescent of a shape lit from the top left: the shape minus itself moved toward the light.</summary>
    public static Vector2[]? ShadeCrescent(Vector2[] pts, float amount)
    {
        var lit = new Vector2[pts.Length];
        var off = new Vector2(-amount, -amount * 1.1f);
        for (int i = 0; i < pts.Length; i++) lit[i] = pts[i] + off;
        var parts = Geometry2D.ClipPolygons(pts, lit);
        if (parts.Count == 0) return null;
        var best = parts[0];
        foreach (var p in parts) if (p.Length > best.Length) best = p;
        return best;
    }

    /// <summary>
    /// A complete inked shape: a wash, hatching on the side away from the light (when hatch &gt; 0), then the ink line.
    /// </summary>
    public static void Shape(CanvasItem ci, Vector2[] pts, Color fill, int seed, float w = 3f, float hatch = 0f, Color? line = null, float alpha = 1f)
    {
        var f = fill; f.A *= alpha;
        Fill(ci, pts, f, seed);
        if (hatch > 0f && ShadeCrescent(pts, hatch) is { } cres)
        {
            var hc = line ?? Line; hc.A = 0.45f * alpha;
            Hatch(ci, cres, 4.5f, 0.9f, hc, 1.2f, seed + 11);
        }
        var lc = line ?? Line; lc.A *= alpha;
        Stroke(ci, pts, true, w, seed, lc);
    }

    /// <summary>A five-point doodle star (stun stars, sparkles).</summary>
    public static void Star(CanvasItem ci, Vector2 c, float r, Color fill, int seed)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++) pts[i] = c + Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Pi / 5) * (i % 2 == 0 ? r : r * 0.45f);
        Fill(ci, pts, fill, seed);
        Stroke(ci, pts, true, 1.5f, seed);
    }

    /// <summary>Text in the handwritten font, with a paper-coloured halo so it reads on any background.</summary>
    public static void Write(CanvasItem ci, Vector2 pos, string s, int size, Color col, float width = 400f, bool halo = true)
    {
        var at = pos - new Vector2(width / 2, 0);
        if (InkBatch.Active(ci)) InkBatch.Flush(); // text is drawn directly: put the batched ink under it first
        if (halo)
        {
            var h = Paper; h.A = col.A * 0.85f;
            ci.DrawStringOutline(Hand, at, s, HorizontalAlignment.Center, width, size, 6, h);
        }
        ci.DrawString(Hand, at, s, HorizontalAlignment.Center, width, size, col);
    }
}
