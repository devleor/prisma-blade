using System;
using Godot;

namespace Prisma.Video;

/// <summary>
/// The devlog's transition: a broad stroke of ink sweeps across the screen, covering it completely on the very frame
/// a shot changes, then wipes off the other way to reveal the next one. The edges are ragged like a dry brush and
/// throw a few drops ahead. Cuts are known in advance (<see cref="Cuts"/>), so the stroke starts before them.
/// </summary>
public partial class Wipe : Control
{
    /// <summary>Frames the stroke takes to cover the screen, and again to uncover it.</summary>
    public const int Half = 16;

    public int[] Cuts = [];
    public int Frame;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        Position = Vector2.Zero;
        Size = GetViewportRect().Size;
        QueueRedraw();
    }

    static float Ease(float k) => k < 0.5f ? 2f * k * k : 1f - 2f * (1f - k) * (1f - k);

    /// <summary>The x of one ragged edge at height <paramref name="y"/>: slanted, wavy, with bristle streaks.</summary>
    float Edge(float x, float y, int seed, float dir)
    {
        float h = Size.Y, u = y / h;
        float slant = Size.X * 0.14f * (u - 0.5f);
        float wave = Mathf.Sin(u * 7.1f + seed) * 18f + Mathf.Sin(u * 23f + seed * 2.3f) * 9f;
        float bristle = Mathf.Max(0f, Mathf.Sin(u * 61f + seed * 5.1f)) * 34f * dir;
        return x + slant + wave + bristle;
    }

    public override void _Draw()
    {
        int nearest = int.MaxValue, best = int.MaxValue;
        foreach (var c in Cuts)
        {
            int d = Math.Abs(Frame - c);
            if (d < best) { best = d; nearest = c; }
        }
        if (best > Half) return;
        float p = (Frame - nearest) / (float)Half; // -1..0 covering, 0..1 uncovering
        float w = Size.X, margin = w * 0.3f;
        float lead = p < 0 ? Mathf.Lerp(-margin, w + margin, Ease(p + 1f)) : w + margin;
        float trail = p < 0 ? -margin : Mathf.Lerp(-margin, w + margin, Ease(p));
        if (lead - trail < 1f) return;
        int seed = nearest % 97;
        const int n = 48;
        var pts = new Vector2[2 * n];
        for (int i = 0; i < n; i++)
        {
            float y = Size.Y * i / (n - 1f);
            pts[i] = new Vector2(Edge(trail, y, seed + 7, -1f), y);
            pts[2 * n - 1 - i] = new Vector2(Edge(lead, y, seed, 1f), y);
        }
        var ink = new Color(0.07f, 0.06f, 0.055f);
        // strips of two triangles between neighbouring rows: the ragged edges may cross, a triangle always draws
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1], c = pts[2 * n - 2 - i], d = pts[2 * n - 1 - i];
            if (d.X <= a.X && c.X <= b.X) continue; // the stroke has not reached these rows yet
            DrawPrimitive([a, d, c], [ink, ink, ink], []);
            DrawPrimitive([a, c, b], [ink, ink, ink], []);
        }
        // drops flung ahead of the stroke while it covers
        if (p < 0)
            for (int i = 0; i < 9; i++)
            {
                float y = Size.Y * ((i * 0.618f + seed * 0.1f) % 1f);
                float x = Edge(lead, y, seed, 1f) + 30f + ((i * 37 + seed) % 70);
                DrawCircle(new Vector2(x, y), 4f + i % 4 * 2.5f, ink);
            }
    }
}
