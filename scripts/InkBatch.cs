using System;
using System.Collections.Generic;
using Godot;

namespace Prisma;

/// <summary>
/// Batched ink. Drawing the art one Godot call per stroke cost thousands of draw calls a frame (and a C#-to-engine
/// crossing for each); between <see cref="Begin"/> and <see cref="End"/> every stroke, wash, hatch line, circle and
/// rect on that canvas item is turned into triangles here and handed to the engine in one call. Lines become strips
/// with mitred joins; washes are ear-clipped in C#. Order is kept: anything drawn directly (text, textures,
/// transforms) must call <see cref="Flush"/> first, which the art classes' Draw* wrappers do.
/// </summary>
public static class InkBatch
{
    static CanvasItem? item;
    static readonly List<Vector2> P = new(16384);
    static readonly List<Color> C = new(16384);
    static readonly List<int> I = new(32768);
    static readonly List<int> ear = new(64);

    /// <summary>Triangles handed to the engine since the counter was last reset (the stress check reads it).</summary>
    public static long Triangles;

    /// <summary>Flush before a batch passes this many vertices: 4000 colours are 64 KB, under the 85 KB large-object limit.</summary>
    const int MaxVerts = 4000;

    /// <summary>Called before adding a shape: starts a new chunk when this one is full (never splits a shape).</summary>
    static void Room(int verts) { if (item != null && P.Count + verts > MaxVerts && I.Count > 0) Flush(); }

    public static bool Active(CanvasItem ci) => item == ci;

    /// <summary>Off only to film the "before" of the devlog: every stroke goes back to one engine call each.</summary>
    public static bool Enabled = true;

    public static void Begin(CanvasItem ci) { Flush(); item = Enabled ? ci : null; }

    public static void End() { Flush(); item = null; }

    public static void Flush()
    {
        if (item != null && I.Count > 0)
        {
            // Exact-size arrays, but flushed in chunks (see MaxVerts) so none reaches the large-object heap: big
            // arrays every frame forced gen-2 collections and frame hitches.
            RenderingServer.CanvasItemAddTriangleArray(item.GetCanvasItem(), I.ToArray(), P.ToArray(), C.ToArray());
            Triangles += I.Count / 3;
        }
        P.Clear(); C.Clear(); I.Clear();
    }

    static Vector2 Normal(Vector2 d)
    {
        float l = d.Length();
        return l < 1e-4f ? Vector2.Zero : new Vector2(-d.Y / l, d.X / l);
    }

    /// <summary>A polyline of width w as one triangle strip with mitred joins (sharp corners are clamped).</summary>
    public static void Polyline(System.ReadOnlySpan<Vector2> pts, Color c, float w)
    {
        int n = pts.Length;
        if (n < 2) return;
        Room(n * 4);
        float h = Mathf.Max(0.5f, w * 0.5f);
        int b = P.Count;
        Vector2 last = Vector2.Zero;
        for (int i = 0; i < n; i++)
        {
            var n0 = i > 0 ? Normal(pts[i] - pts[i - 1]) : Vector2.Zero;
            var n1 = i < n - 1 ? Normal(pts[i + 1] - pts[i]) : Vector2.Zero;
            if (n0 == Vector2.Zero) n0 = n1;
            if (n1 == Vector2.Zero) n1 = n0;
            if (n0 == Vector2.Zero) n0 = n1 = last == Vector2.Zero ? Vector2.Up : last;
            last = n1;
            var m = n0 + n1;
            float ml = m.Length();
            if (ml > 1e-3f && (m / ml).Dot(n1) >= 0.5f)
            {
                // an ordinary corner: one mitred pair (never longer than twice the half-width)
                m /= ml;
                var off = m * (h / (m.Dot(n1)));
                P.Add(pts[i] + off); P.Add(pts[i] - off);
                C.Add(c); C.Add(c);
            }
            else
            {
                // a sharp corner (a hand zigzag): bevel it with two pairs instead of a long spike
                P.Add(pts[i] + n0 * h); P.Add(pts[i] - n0 * h);
                P.Add(pts[i] + n1 * h); P.Add(pts[i] - n1 * h);
                C.Add(c); C.Add(c); C.Add(c); C.Add(c);
            }
        }
        for (int a = b; a + 3 < P.Count; a += 2)
        {
            I.Add(a); I.Add(a + 1); I.Add(a + 2);
            I.Add(a + 1); I.Add(a + 3); I.Add(a + 2);
        }
    }

    public static void Line(Vector2 a, Vector2 b, Color c, float w) => Polyline([a, b], c, w);

    /// <summary>A filled simple polygon, ear-clipped. False (and nothing added) when it folds over itself.</summary>
    public static bool Polygon(System.ReadOnlySpan<Vector2> pts, Color c)
    {
        int n = pts.Length;
        if (n < 3) return false;
        float area = 0f;
        for (int i = 0; i < n; i++) { var p = pts[i]; var q = pts[(i + 1) % n]; area += p.X * q.Y - q.X * p.Y; }
        if (Mathf.Abs(area) < 1e-3f) return false;
        float sign = area > 0 ? 1f : -1f;
        Room(n);
        int b = P.Count, i0 = I.Count;
        for (int i = 0; i < n; i++) { P.Add(pts[i]); C.Add(c); }
        if (n == 3) { I.Add(b); I.Add(b + 1); I.Add(b + 2); return true; }
        ear.Clear();
        for (int i = 0; i < n; i++) ear.Add(i);
        int guard = 0;
        while (ear.Count > 3)
        {
            bool cut = false;
            for (int k = 0; k < ear.Count; k++)
            {
                int ia = ear[(k + ear.Count - 1) % ear.Count], ib = ear[k], ic = ear[(k + 1) % ear.Count];
                Vector2 pa = pts[ia], pb = pts[ib], pc = pts[ic];
                float turn = (pb - pa).Cross(pc - pb) * sign;
                if (MathF.Abs(turn) < 1e-5f && (pb - pa).Dot(pc - pb) >= 0f)
                {
                    ear.RemoveAt(k); // a point on a straight edge adds no area: drop it
                    cut = true;
                    break;
                }
                if (turn <= 0f) continue; // reflex corner
                bool inside = false;
                foreach (int j in ear)
                {
                    if (j == ia || j == ib || j == ic) continue;
                    if (InTri(pts[j], pa, pb, pc, sign)) { inside = true; break; }
                }
                if (inside) continue;
                I.Add(b + ia); I.Add(b + ib); I.Add(b + ic);
                ear.RemoveAt(k);
                cut = true;
                break;
            }
            if (!cut || ++guard > n * 2)
            {
                // folded: take it all back
                P.RemoveRange(b, n); C.RemoveRange(b, n); I.RemoveRange(i0, I.Count - i0);
                return false;
            }
        }
        I.Add(b + ear[0]); I.Add(b + ear[1]); I.Add(b + ear[2]);
        return true;
    }

    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float sign) =>
        (b - a).Cross(p - a) * sign >= 0f && (c - b).Cross(p - b) * sign >= 0f && (a - c).Cross(p - c) * sign >= 0f;

    static Vector2[] RingPts(Vector2 c, float r, float a0, float a1, int n)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) pts[i] = c + Vector2.FromAngle(a0 + (a1 - a0) * i / (n - 1)) * r;
        return pts;
    }

    public static void Circle(Vector2 c, float r, Color col, bool filled, float w)
    {
        int n = Mathf.Clamp((int)(r * 0.8f) + 8, 10, 48);
        if (filled)
        {
            Room(n + 1);
            int b = P.Count;
            P.Add(c); C.Add(col);
            for (int i = 0; i < n; i++) { P.Add(c + Vector2.FromAngle(i * Mathf.Tau / n) * r); C.Add(col); }
            for (int i = 0; i < n; i++) { I.Add(b); I.Add(b + 1 + i); I.Add(b + 1 + (i + 1) % n); }
        }
        else Polyline(RingPts(c, r, 0f, Mathf.Tau, n + 1), col, w);
    }

    public static void Arc(Vector2 c, float r, float a0, float a1, int n, Color col, float w) =>
        Polyline(RingPts(c, r, a0, a1, Mathf.Max(2, n)), col, w);

    public static void Rect(Rect2 r, Color col, bool filled, float w)
    {
        var a = r.Position; var b = r.Position + new Vector2(r.Size.X, 0); var c = r.End; var d = r.Position + new Vector2(0, r.Size.Y);
        if (filled)
        {
            Room(4);
            int i = P.Count;
            P.Add(a); P.Add(b); P.Add(c); P.Add(d);
            C.Add(col); C.Add(col); C.Add(col); C.Add(col);
            I.Add(i); I.Add(i + 1); I.Add(i + 2); I.Add(i); I.Add(i + 2); I.Add(i + 3);
        }
        else Polyline([a, b, c, d, a], col, w);
    }
}
