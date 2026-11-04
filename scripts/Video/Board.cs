using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Prisma.Video;

/// <summary>
/// The devlog's explainer: a sheet of paper over the whole screen where the ideas are drawn in the game's own ink,
/// stroke by stroke, with the real code typed beside them. Each <see cref="Kind"/> is one little animation; its
/// phase picks a variant (before/after). Drawn in screen space, 1920x1080 or whatever the window is.
/// </summary>
public partial class Board : Control
{
    public enum Kind { None, Boil, Calls, Cascade, Strip, Numbers, Doom, Memory, Cut, Review, Source }

    Kind kind;
    int phase, start, now;

    static readonly Color Red = new(0.72f, 0.1f, 0.07f);
    static readonly Color Wash = new(0.83f, 0.77f, 0.65f);
    static readonly Font Mono = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "Courier New"], FontWeight = 500 };

    public void Show(Kind k, int ph, int frame) { kind = k; phase = ph; start = frame; Visible = k != Kind.None; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        now++;
        Position = Vector2.Zero;
        Size = GetViewportRect().Size; // a Control under a CanvasLayer gets no size from its anchors: fill the screen by hand
        if (Visible) QueueRedraw();
    }

    float T => (now - start) / (float)Devlog.Fps;

    // ---------------------------------------------------------------- little helpers

    /// <summary>0..1 for the window [a, a+len] of the animation's clock.</summary>
    float Ramp(float a, float len) => Mathf.Clamp((T - a) / len, 0f, 1f);

    static float Ease(float k) => 1f - (1f - k) * (1f - k);

    Vector2 P(float x, float y) => new(x * Size.X, y * Size.Y);

    void Line(Vector2 a, Vector2 b, float w, int seed, Color? col = null, float shown = 1f)
    {
        if (shown <= 0.001f) return;
        Ink.Line2(this, a, a.Lerp(b, shown), w, seed, col);
    }

    void Arrow(Vector2 a, Vector2 b, float w, int seed, Color? col = null, float shown = 1f)
    {
        if (shown <= 0.001f) return;
        var e = a.Lerp(b, shown);
        Ink.Line2(this, a, e, w, seed, col);
        var d = (b - a).Normalized();
        float h = 10f + w * 3f;
        Ink.Line2(this, e, e - d.Rotated(0.5f) * h, w, seed + 1, col);
        Ink.Line2(this, e, e - d.Rotated(-0.5f) * h, w, seed + 2, col);
    }

    void Dot(Vector2 p, float r, Color c) => DrawCircle(p, r, c);

    void Label(Vector2 at, string s, int size, Color? col = null, float a = 1f) =>
        Ink.Write(this, at, s, size, new Color(col ?? Ink.Line, a), 900f, true);

    /// <summary>The graphics card: a box with pins, where the requests land.</summary>
    void Gpu(Rect2 r, float glow = 0f)
    {
        Vector2[] box = [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
        Ink.Shape(this, box, new Color(0.62f, 0.66f, 0.6f).Lerp(new Color(0.95f, 0.85f, 0.5f), glow), 900, 4f, 6f);
        for (int i = 0; i < 6; i++)
        {
            float x = r.Position.X + r.Size.X * (i + 1) / 7f;
            Line(new Vector2(x, r.End.Y), new Vector2(x, r.End.Y + 22), 3f, 910 + i);
            Line(new Vector2(x, r.Position.Y), new Vector2(x, r.Position.Y - 22), 3f, 920 + i);
        }
        var chip = r.Grow(-r.Size.X * 0.28f);
        Ink.Shape(this, [chip.Position, new(chip.End.X, chip.Position.Y), chip.End, new(chip.Position.X, chip.End.Y)], new Color(0.3f, 0.32f, 0.3f), 930, 3f);
    }

    /// <summary>Real code being typed into a paper panel, a few characters a frame, with a blinking caret.</summary>
    void Code(Rect2 r, string[] lines, float from, float cps = 38f)
    {
        Vector2[] box = [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
        Ink.Shape(this, box, new Color(0.2f, 0.18f, 0.16f), 950, 3f);
        int budget = (int)(Mathf.Max(0f, T - from) * cps);
        // the text size that fits the panel: the longest line across, every line down (never past the dark box)
        const float pad = 26f;
        float widest = 1f;
        foreach (var l in lines) widest = Mathf.Max(widest, Mono.GetStringSize(l + " ", HorizontalAlignment.Left, -1, 26).X);
        int fs = (int)Mathf.Clamp(Mathf.Min(26f * (r.Size.X - 2 * pad) / widest, (r.Size.Y - 2 * pad) / (lines.Length * 1.55f)), 10f, 34f);
        float lh = fs * 1.55f;
        float y = r.Position.Y + pad + fs;
        foreach (var l in lines)
        {
            if (budget <= 0) break;
            string shown = l.Length <= budget ? l : l[..budget];
            budget -= l.Length + 4;
            var col = l.TrimStart().StartsWith("//") ? new Color(0.6f, 0.7f, 0.5f) : new Color(0.93f, 0.88f, 0.76f);
            DrawString(Mono, new Vector2(r.Position.X + pad, y), shown, HorizontalAlignment.Left, r.Size.X - 2 * pad, fs, col);
            if (budget <= 0 && (now / 20) % 2 == 0)
                DrawRect(new Rect2(r.Position.X + pad + Mono.GetStringSize(shown, HorizontalAlignment.Left, -1, fs).X + 2, y - fs * 0.85f, fs * 0.45f, fs), new Color(0.93f, 0.88f, 0.76f));
            y += lh;
        }
    }

    // ---------------------------------------------------------------- draw

    public override void _Draw()
    {
        if (kind == Kind.None) return;
        Ink.Boil = now / 6; // the board's ink boils too
        var s = Size;
        DrawRect(new Rect2(Vector2.Zero, s), Ink.Paper);
        // a faint grid, like the squared paper of a notebook
        for (float x = 0; x < s.X; x += 48) DrawLine(new Vector2(x, 0), new Vector2(x, s.Y), new Color(0.55f, 0.62f, 0.75f, 0.12f), 1f);
        for (float y = 0; y < s.Y; y += 48) DrawLine(new Vector2(0, y), new Vector2(s.X, y), new Color(0.55f, 0.62f, 0.75f, 0.12f), 1f);
        switch (kind)
        {
            case Kind.Boil: DrawBoil(); break;
            case Kind.Calls: DrawCalls(); break;
            case Kind.Cascade: DrawCascade(phase == 1); break;
            case Kind.Strip: DrawStrip(); break;
            case Kind.Numbers: DrawNumbers(); break;
            case Kind.Doom: DrawDoom(); break;
            case Kind.Memory: DrawMemory(phase == 1); break;
            case Kind.Cut: DrawCut(phase == 1); break;
            case Kind.Review: DrawReview(phase == 1); break;
            case Kind.Source: DrawSource(); break;
        }
    }

    // a line is points plus a wobble recomputed 10 times a second
    void DrawBoil()
    {
        var pts = new Vector2[9];
        for (int i = 0; i < pts.Length; i++)
        {
            float u = i / (pts.Length - 1f);
            pts[i] = P(0.08f + 0.5f * u, 0.5f + 0.13f * Mathf.Sin(u * 5.5f));
        }
        float shown = Ease(Ramp(0.2f, 1.4f));
        int n = Math.Max(2, (int)Mathf.Ceil(pts.Length * shown));
        var part = pts[..n];
        // the exact points (vectors), then the inked line that trembles around them
        for (int i = 0; i < n; i++) Dot(pts[i], 7f, Red);
        if (T > 1.6f)
        {
            Ink.Stroke(this, pts, false, 7f, 40, Ink.Line, 2.2f);
            for (int i = 0; i < pts.Length; i++)
            {
                var j = pts[i] + Ink.J(40, i, 9f);
                Arrow(pts[i], j + (j - pts[i]).Normalized() * 14f, 2f, 60 + i, new Color(Red, 0.8f));
            }
        }
        else if (n >= 2) DrawPolyline(part, new Color(Ink.Line, 0.35f), 2f, true);
        Code(new Rect2(P(0.62f, 0.25f), P(0.34f, 0.42f)),
        [
            "// cada traço é calculado",
            "Ink.Boil = World.Frame / 6;",
            "",
            "var p = pts[i] + J(seed, i, amp);",
            "DrawPolyline(p, cor, largura);",
            "",
            "// 10 vezes por segundo,",
            "// um tremido novo",
        ], 0.6f);
    }

    // each stroke a separate request to the graphics card
    void DrawCalls()
    {
        var gpu = new Rect2(P(0.72f, 0.36f), P(0.17f, 0.26f));
        Gpu(gpu, 0.25f + 0.2f * Mathf.Sin(T * 9f));
        var target = gpu.GetCenter();
        const int cols = 9, rows = 6;
        for (int i = 0; i < cols * rows; i++)
        {
            var c = P(0.08f + 0.055f * (i % cols), 0.2f + 0.11f * (i / cols));
            float appear = Ramp(i * 0.02f, 0.2f);
            if (appear <= 0) continue;
            Line(c - new Vector2(16, 6), c + new Vector2(16, -6), 3f, 300 + i, null, appear);
            // its request flying over, one after another, forever
            float period = 2.6f, k = ((T - 0.8f - i * (period / (cols * rows))) % period + period) % period / 0.55f;
            if (T > 0.8f && k < 1f) Dot(c.Lerp(target, Ease(k)), 6f, Red);
        }
        int count = (int)Mathf.Min(7140, Mathf.Max(0, T - 0.8f) * 2400);
        Label(P(0.805f, 0.78f), count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")), 64, Red);
    }

    // the slow frame makes the game step more, which makes the next frame slower (phase 1: capped at 3)
    void DrawCascade(bool capped)
    {
        float x = Size.X * 0.06f, y = Size.Y * 0.3f, unit = Size.X * 0.022f;
        var floorY = Size.Y * 0.7f;
        Line(new Vector2(Size.X * 0.04f, floorY), new Vector2(Size.X * 0.96f, floorY), 3f, 500);
        for (int k = 0; k < 8; k++)
        {
            float appear = Ramp(0.3f + k * 0.55f, 0.3f);
            if (appear <= 0) break;
            int steps = capped ? Math.Min(3, 1 << Math.Min(k, 3)) : Math.Min(8, 1 << Math.Min(k, 3));
            float w = (capped ? 4f : 2.5f + steps * 1.4f) * unit;
            float h = (capped ? 150f : 110f + steps * 22f) * Ease(appear);
            var r = new Rect2(x, floorY - h, w, h);
            Vector2[] box = [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
            Ink.Shape(this, box, capped ? new Color(0.78f, 0.84f, 0.7f) : Wash.Lerp(new Color(0.9f, 0.55f, 0.45f), steps / 8f), 510 + k, 3f, 5f);
            for (int st = 0; st < steps; st++)
            {
                float sx = r.Position.X + (st + 0.5f) * r.Size.X / steps;
                Line(new Vector2(sx, r.End.Y - 12), new Vector2(sx, r.End.Y - 40), 4f, 530 + k * 9 + st, Red);
            }
            x += w + unit * 0.6f;
            if (x > Size.X * 0.92f) break;
        }
        _ = y;
    }

    // a stroke becomes a strip of triangles, all sent in one go
    void DrawStrip()
    {
        Vector2[] pts = [P(0.08f, 0.62f), P(0.2f, 0.42f), P(0.33f, 0.58f), P(0.46f, 0.38f), P(0.58f, 0.55f)];
        float h = 34f;
        // 1. the line
        float l = Ease(Ramp(0f, 1f));
        for (int i = 0; i < pts.Length - 1; i++)
            Line(pts[i], pts[i + 1], 5f, 700 + i, null, Mathf.Clamp(l * (pts.Length - 1) - i, 0f, 1f));
        foreach (var p in pts) if (l > 0.05f) Dot(p, 7f, Ink.Line);
        // 2. a normal at every point, 3. the two edge points it gives
        var pairs = new List<(Vector2, Vector2)>();
        for (int i = 0; i < pts.Length; i++)
        {
            var n0 = i > 0 ? (pts[i] - pts[i - 1]).Normalized().Orthogonal() : Vector2.Zero;
            var n1 = i < pts.Length - 1 ? (pts[i + 1] - pts[i]).Normalized().Orthogonal() : Vector2.Zero;
            var n = (n0 + n1).Normalized();
            if (n0 == Vector2.Zero) n = n1; if (n1 == Vector2.Zero) n = n0;
            pairs.Add((pts[i] + n * h, pts[i] - n * h));
            float a = Ramp(1.1f + i * 0.15f, 0.3f);
            if (a > 0) { Arrow(pts[i], pts[i] + n * h * 1.6f, 2.5f, 720 + i, Red, a); Arrow(pts[i], pts[i] - n * h * 1.6f, 2.5f, 730 + i, Red, a); }
            if (Ramp(2.1f + i * 0.12f, 0.2f) > 0) { Dot(pairs[i].Item1, 6f, Red); Dot(pairs[i].Item2, 6f, Red); }
        }
        // 4. two triangles per segment, one after another
        for (int i = 0; i < pts.Length - 1; i++)
            for (int t = 0; t < 2; t++)
            {
                float a = Ramp(3.0f + (i * 2 + t) * 0.3f, 0.25f);
                if (a <= 0) continue;
                var (pa, pb) = pairs[i];
                var (pc, pd) = pairs[i + 1];
                Vector2[] tri = t == 0 ? [pa, pb, pc] : [pb, pd, pc];
                DrawColoredPolygon(tri, new Color(t == 0 ? new Color(0.55f, 0.6f, 0.75f) : new Color(0.75f, 0.62f, 0.5f), 0.55f * a));
                Ink.Stroke(this, tri, true, 2f, 760 + i * 2 + t, new Color(Ink.Line, a));
            }
        // 5. one arrow: the whole strip in a single request
        var gpu = new Rect2(P(0.72f, 0.08f), P(0.14f, 0.2f));
        float go = Ramp(5.6f, 0.6f);
        Gpu(gpu, go);
        if (go > 0) Arrow(P(0.45f, 0.3f), gpu.Position + new Vector2(0, gpu.Size.Y / 2), 6f, 790, Red, Ease(go));
        Code(new Rect2(P(0.62f, 0.42f), P(0.35f, 0.5f)),
        [
            "// InkBatch.Polyline",
            "P.Add(pts[i] + off);",
            "P.Add(pts[i] - off);",
            "I.Add(a); I.Add(a + 1); I.Add(a + 2);",
            "I.Add(a + 1); I.Add(a + 3); I.Add(a + 2);",
            "",
            "// um pedido só, no fim",
            "CanvasItemAddTriangleArray(",
            "    item, I, P, C);",
        ], 1.2f, 30f);
    }

    // 133 ms -> 3.5 ms
    void DrawNumbers()
    {
        float a = Ramp(0f, 0.4f), cross = Ramp(1.0f, 0.4f), b = Ramp(1.5f, 0.5f);
        Label(P(0.3f, 0.5f), "133 ms", 150, null, a);
        if (cross > 0) Line(P(0.17f, 0.56f), P(0.17f, 0.56f).Lerp(P(0.43f, 0.4f), 1f), 9f, 801, Red, cross);
        if (b > 0) Arrow(P(0.46f, 0.47f), P(0.56f, 0.47f), 6f, 802, null, b);
        Label(P(0.72f, 0.5f), "3,5 ms", 150, Red, b);
        float c = Ramp(2.3f, 0.5f);
        Label(P(0.5f, 0.75f), "7.140  →  86", 70, null, c);
    }

    // DOOM: the map is lines; it is split to draw only what is seen; the rules tick 35 times a second
    static readonly (float, float, float, float)[] Map =
    [
        // three rooms and two corridors, as line segments (a made-up map, drawn for the explanation)
        (0.10f, 0.20f, 0.34f, 0.20f), (0.34f, 0.20f, 0.34f, 0.40f), (0.34f, 0.48f, 0.34f, 0.62f), (0.34f, 0.62f, 0.10f, 0.62f), (0.10f, 0.62f, 0.10f, 0.20f),
        (0.34f, 0.40f, 0.50f, 0.40f), (0.34f, 0.48f, 0.50f, 0.48f),
        (0.50f, 0.28f, 0.50f, 0.40f), (0.50f, 0.48f, 0.50f, 0.72f), (0.50f, 0.72f, 0.72f, 0.72f), (0.72f, 0.72f, 0.72f, 0.56f),
        (0.72f, 0.48f, 0.72f, 0.28f), (0.72f, 0.28f, 0.50f, 0.28f),
        (0.72f, 0.48f, 0.84f, 0.48f), (0.72f, 0.56f, 0.84f, 0.56f),
        (0.84f, 0.36f, 0.84f, 0.48f), (0.84f, 0.56f, 0.84f, 0.82f), (0.84f, 0.82f, 0.95f, 0.82f), (0.95f, 0.82f, 0.95f, 0.36f), (0.95f, 0.36f, 0.84f, 0.36f),
    ];

    void DrawDoom()
    {
        if (phase == 2) { DrawTicks(); return; }
        Vector2 Q(float x, float y) => P(-0.02f + x * 1.0f, y * 1.0f);
        bool split = phase == 1;
        var viewer = Q(0.2f, 0.45f);
        float fov = Ramp(1.8f, 0.5f);
        for (int i = 0; i < Map.Length; i++)
        {
            var (x0, y0, x1, y1) = Map[i];
            Vector2 a = Q(x0, y0), b = Q(x1, y1);
            float shown = split ? 1f : Ramp(0.2f + i * 0.14f, 0.18f);
            bool seen = split && fov > 0 && InView(viewer, a, b);
            Line(a, b, seen ? 8f : 4.5f, 1000 + i, seen ? Red : null, shown);
            if (shown > 0) { Dot(a, 5f, Ink.Line); if (shown >= 1f) Dot(b, 5f, Ink.Line); }
        }
        if (!split) return;
        // the split lines (BSP): the map is cut in halves, then halves of halves
        float s1 = Ease(Ramp(0f, 0.7f)), s2 = Ease(Ramp(0.8f, 0.6f));
        Line(Q(0.42f, 0.1f), Q(0.42f, 0.9f), 4f, 1100, new Color(0.25f, 0.4f, 0.75f), s1);
        Line(Q(0.42f, 0.5f), Q(0.99f, 0.5f), 4f, 1101, new Color(0.25f, 0.4f, 0.75f), s2);
        if (s1 >= 1f) DrawRect(new Rect2(Q(0.42f, 0.1f), Q(0.99f, 0.9f) - Q(0.42f, 0.1f)), new Color(0.25f, 0.4f, 0.75f, 0.08f));
        // the player and what they can see: only those walls get drawn
        if (fov > 0)
        {
            Dot(viewer, 12f, Red);
            float ang = -0.15f, half = 0.42f;
            var l = viewer + Vector2.FromAngle(ang - half) * Size.X * 0.42f * fov;
            var r = viewer + Vector2.FromAngle(ang + half) * Size.X * 0.42f * fov;
            DrawColoredPolygon([viewer, l, r], new Color(Red, 0.12f));
            Line(viewer, l, 2.5f, 1110, Red);
            Line(viewer, r, 2.5f, 1111, Red);
        }
    }

    bool InView(Vector2 eye, Vector2 a, Vector2 b)
    {
        float ang = -0.15f, half = 0.42f;
        foreach (var p in new[] { a, b, (a + b) / 2 })
        {
            var d = p - eye;
            if (d.Length() < Size.X * 0.42f && Mathf.Abs(Mathf.AngleDifference(ang, d.Angle())) < half) return true;
        }
        return false;
    }

    // a fixed beat for the rules, frames come when they come
    void DrawTicks()
    {
        float x0 = Size.X * 0.1f, x1 = Size.X * 0.9f;
        float yRule = Size.Y * 0.38f, yDraw = Size.Y * 0.66f;
        Label(new Vector2(Size.X * 0.06f, yRule - 60), "35", 70, Red, Ramp(0f, 0.4f));
        Label(new Vector2(Size.X * 0.06f, yDraw - 60), "≈", 70, null, Ramp(0.4f, 0.4f));
        Line(new Vector2(x0, yRule), new Vector2(x1, yRule), 3f, 1200, null, Ramp(0f, 0.5f));
        Line(new Vector2(x0, yDraw), new Vector2(x1, yDraw), 3f, 1201, null, Ramp(0.4f, 0.5f));
        // one second of rules: 35 even ticks, lighting up in time
        float beat = (T % 1f) * 35f;
        for (int i = 0; i < 35; i++)
        {
            float x = Mathf.Lerp(x0, x1, i / 34f);
            bool lit = i <= beat;
            Line(new Vector2(x, yRule - 26), new Vector2(x, yRule + 26), lit ? 5f : 3f, 1210 + i, lit ? Red : new Color(Ink.Line, 0.4f));
        }
        // frames of uneven length underneath
        float[] widths = [0.07f, 0.05f, 0.11f, 0.06f, 0.09f, 0.05f, 0.13f, 0.07f, 0.06f, 0.1f, 0.05f];
        float x2 = x0;
        for (int i = 0; i < widths.Length && x2 < x1; i++)
        {
            float w = widths[i] * (x1 - x0) * 0.8f;
            float a = Ramp(0.8f + i * 0.12f, 0.2f);
            if (a <= 0) break;
            var r = new Rect2(x2, yDraw - 40, Mathf.Min(w, x1 - x2), 80);
            Vector2[] box = [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
            Ink.Shape(this, box, Wash, 1250 + i, 3f, 0f, null, a);
            x2 += w + 8;
        }
    }

    // memory: huge packs go to a special area whose cleanup stops everything; small packs do not
    void DrawMemory(bool small)
    {
        var heap = new Rect2(P(0.08f, 0.32f), P(0.38f, 0.56f));
        var loh = new Rect2(P(0.56f, 0.32f), P(0.36f, 0.56f));
        Vector2[] Box(Rect2 r) => [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
        Ink.Stroke(this, Box(heap), true, 4f, 1300);
        Ink.Shape(this, Box(loh), new Color(0.88f, 0.8f, 0.7f), 1301, 6f, 8f);
        float cycle = small ? 3.2f : 4.6f, t = T % cycle;
        if (!small)
        {
            // a big pack falls into the special area every 0.8 s; when full, everything stops to clean it
            int landed = Math.Min(4, (int)(t / 0.8f));
            for (int i = 0; i <= Math.Min(landed, 3); i++)
            {
                float k = i < landed ? 1f : Ease(Mathf.Clamp((t - i * 0.8f) / 0.5f, 0f, 1f));
                float y = Mathf.Lerp(Size.Y * 0.08f, loh.End.Y - 70 - i * 74, k);
                var r = new Rect2(loh.Position.X + 30, y, loh.Size.X - 60, 62);
                Ink.Shape(this, Box(r), new Color(0.72f, 0.45f, 0.4f), 1310 + i, 3f, 6f);
            }
            if (t > 3.3f && t < 4.3f)
            {
                DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.2f, 0.18f, 0.16f, 0.35f));
                var c = Size / 2;
                DrawRect(new Rect2(c.X - 70, c.Y - 90, 45, 180), Red);
                DrawRect(new Rect2(c.X + 25, c.Y - 90, 45, 180), Red);
            }
            Label(P(0.74f, 0.27f), "> 85 KB", 40, Red);
        }
        else
        {
            // the same pack, cut into small ones under the limit, falling into the ordinary area
            float cut = Ramp(0.2f, 0.4f) * (t < 0.1f ? 0f : 1f);
            var big = new Rect2(P(0.12f, 0.1f), P(0.3f, 0.07f));
            int pieces = 6;
            for (int i = 0; i < pieces; i++)
            {
                float w = big.Size.X / pieces;
                float k = Ease(Mathf.Clamp((t - 0.7f - i * 0.25f) / 0.5f, 0f, 1f));
                float x = big.Position.X + i * w + (cut > 0 ? i * 6f : 0f);
                float y = Mathf.Lerp(big.Position.Y, heap.End.Y - 60 - (i % 3) * 62, k);
                float x2 = Mathf.Lerp(x, heap.Position.X + 30 + (i / 3) * (w + 20) + (i % 3) * 8, k);
                var r = new Rect2(x2, y, w - 4, 52);
                Ink.Shape(this, Box(r), new Color(0.55f, 0.65f, 0.5f), 1330 + i, 3f, 6f);
            }
            Label(P(0.27f, 0.27f), "< 85 KB", 40, new Color(0.25f, 0.45f, 0.2f));
        }
    }

    // the floor cut: repeated points at the tips (refused) vs one point per tip (filled)
    void DrawCut(bool fixedShape)
    {
        var c = P(0.5f, 0.5f);
        var dir = Vector2.FromAngle(-0.12f);
        float len = Size.X * 0.62f, width = Size.Y * 0.2f, bend = Size.Y * 0.06f;
        var side = dir.Orthogonal();
        const int n = 9;
        var start = c - dir * len * 0.5f;
        Vector2 Mid(float u) => start + dir * (u * len) + side * bend * (1f - 4f * (u - 0.5f) * (u - 0.5f));
        var pts = new List<Vector2>();
        if (fixedShape)
        {
            pts.Add(Mid(0));
            for (int i = 1; i < n - 1; i++) { float u = i / (n - 1f); pts.Add(Mid(u) + side * width * Mathf.Sin(Mathf.Pi * u) * 0.5f); }
            pts.Add(Mid(1));
            for (int i = n - 2; i >= 1; i--) { float u = i / (n - 1f); pts.Add(Mid(u) - side * width * Mathf.Sin(Mathf.Pi * u) * 0.3f); }
        }
        else
        {
            for (int i = 0; i < n; i++) { float u = i / (n - 1f); pts.Add(Mid(u) + side * width * Mathf.Sin(Mathf.Pi * u) * 0.5f); }
            for (int i = n - 1; i >= 0; i--) { float u = i / (n - 1f); pts.Add(Mid(u) - side * width * Mathf.Sin(Mathf.Pi * u) * 0.3f); }
        }
        var arr = pts.ToArray();
        float outline = Ease(Ramp(0f, 0.8f));
        Ink.Stroke(this, arr, true, 3f, 1400, new Color(Ink.Line, outline));
        for (int i = 0; i < arr.Length; i++) if (Ramp(0.2f + i * 0.04f, 0.1f) > 0) Dot(arr[i], 7f, Ink.Line);
        if (!fixedShape)
        {
            // the two tips each hold two points in the same place: the cutter gives up and nothing is drawn
            float a = Ramp(1.2f, 0.3f);
            foreach (var tip in new[] { Mid(0), Mid(1) })
            {
                Ink.Stroke(this, Ink.Circle(tip, 26f + 4f * Mathf.Sin(T * 8f), 1410), true, 4f, 1411, new Color(Red, a));
                Dot(tip + new Vector2(5, 0), 7f, new Color(Red, a));
            }
            float x = Ramp(2.2f, 0.4f);
            if (x > 0)
            {
                Line(c + new Vector2(-120, -120), c + new Vector2(120, 120), 12f, 1420, Red, x);
                Line(c + new Vector2(120, -120), c + new Vector2(-120, 120), 12f, 1421, Red, Ramp(2.5f, 0.4f));
            }
        }
        else
        {
            // one point per tip: the triangles come out one by one and the cut fills with ink
            var tris = Geometry2D.TriangulatePolygon(arr);
            int count = tris.Length / 3;
            for (int k = 0; k < count; k++)
            {
                float a = Ramp(1.0f + k * 0.12f, 0.15f);
                if (a <= 0) break;
                Vector2[] tri = [arr[tris[k * 3]], arr[tris[k * 3 + 1]], arr[tris[k * 3 + 2]]];
                DrawColoredPolygon(tri, new Color(0.1f, 0.08f, 0.07f, 0.8f * a));
                Ink.Stroke(this, tri, true, 1.5f, 1450 + k, new Color(Ink.Paper, 0.6f * a));
            }
            foreach (var tip in new[] { Mid(0), Mid(1) }) Dot(tip, 9f, new Color(0.25f, 0.5f, 0.2f));
        }
    }

    // the reviewer's catch: the flash timed by the game clock (slowed 4x in slow motion) vs the real clock
    void DrawReview(bool fixedCode)
    {
        var face = P(0.22f, 0.5f);
        float r = Size.Y * 0.2f;
        Ink.Stroke(this, Ink.Circle(face, r, 1500), true, 6f, 1501);
        for (int i = 0; i < 12; i++)
        {
            var d = Vector2.FromAngle(i * Mathf.Tau / 12f);
            Line(face + d * r * 0.82f, face + d * r * 0.95f, 4f, 1510 + i);
        }
        // the clock the flash reads: in slow motion the game's clock crawls at a quarter, the real one does not
        float speed = fixedCode ? 1f : 0.25f;
        float ang = -Mathf.Pi / 2 + T * Mathf.Tau * 0.5f * speed;
        Line(face, face + Vector2.FromAngle(ang) * r * 0.75f, 7f, 1530, fixedCode ? new Color(0.2f, 0.45f, 0.2f) : Red);
        Dot(face, 9f, Ink.Line);
        // the flash it times: a burst that lasts 75 ms of whichever clock it reads
        float period = 1.6f, k = T % period;
        float lasts = fixedCode ? 0.075f : 0.3f;
        if (T > 0.8f && k < lasts)
            Ink.Shape(this, Ink.Blob(P(0.22f, 0.13f), 70f, 50f, 0.3f, 1540 + (int)(T / period), 0.3f, 14), new Color(0.97f, 0.93f, 0.6f), 1541, 3f);
        string[] code = fixedCode
            ?
            [
                "// Hud.cs: o flash de impacto",
                "ulong now = Time.GetTicksMsec();",
                "impactUntil = now + ImpactMs;",
                "",
                "// relógio de verdade",
                "Visible = Time.GetTicksMsec() < impactUntil;",
            ]
            :
            [
                "// Hud.cs: o flash de impacto",
                "impactT = 0.075f;",
                "",
                "// relógio do jogo: desacelera",
                "impactT -= (float)delta;",
                "Visible = impactT > 0;",
            ];
        Code(new Rect2(P(0.46f, 0.24f), P(0.5f, 0.52f)), code, 0.3f, 40f);
    }

    // the real source, typed: this is what will be open for anyone to read
    static string[]? source;
    void DrawSource()
    {
        if (source == null)
        {
            var all = Godot.FileAccess.GetFileAsString("res://scripts/InkBatch.cs").Split('\n');
            int at = Array.FindIndex(all, l => l.Contains("public static void Polyline"));
            source = [.. all.Skip(Math.Max(0, at)).Take(16).Select(l => l.TrimEnd().Replace("\t", "    "))];
        }
        Code(new Rect2(P(0.08f, 0.1f), P(0.84f, 0.8f)), source, 0.15f, 70f);
    }
}
