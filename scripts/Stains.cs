using Godot;

namespace Prisma;

/// <summary>
/// Ink left on the map: kills leave a splat in the enemy's colour, hits flick drops, bombs leave scorch marks, and
/// they stay for the whole run, so the arena ends up painted with the fight. Drawn on the floor, under everyone,
/// and only redrawn when a stain is added (stains never boil: dried ink holds still).
/// </summary>
public partial class Stains : Node2D
{
    public const int Max = 420;

    readonly struct Stain(Vector2[] pts, Color col)
    {
        public readonly Vector2[] Pts = pts;
        public readonly Color Col = col;
    }

    readonly Dictionary<(int, int, int), List<Stain>> rooms = [];
    List<Stain> stains = [];
    readonly RandomNumberGenerator rng = new();
    int seed = 1;
    bool dirty;

    public int Count => stains.Count;

    public override void _Ready() => ShowBehindParent = true;

    /// <summary>A big splat: a lumpy blob with drops thrown out along <paramref name="dir"/> (radians).</summary>
    public void Splat(Vector2 pos, float size, Color col, float dir, int drops = 6)
    {
        var c = Wash(col);
        Add(Ink.Blob(pos, size, size * rng.RandfRange(0.7f, 1f), rng.RandfRange(0, Mathf.Tau), seed++, 0.35f, 22), c);
        for (int i = 0; i < drops; i++)
        {
            float a = dir + rng.RandfRange(-0.9f, 0.9f), d = size * rng.RandfRange(1.1f, 2.6f), r = size * rng.RandfRange(0.08f, 0.22f);
            var at = pos + Vector2.FromAngle(a) * d;
            Add(Ink.Blob(at, r * 1.6f, r, a, seed++, 0.2f, 10), c); // drops are stretched along their flight
        }
    }

    /// <summary>A few small drops flicked off a hit.</summary>
    public void Drops(Vector2 pos, Color col, float dir, int n)
    {
        var c = Wash(col);
        for (int i = 0; i < n; i++)
        {
            float a = dir + rng.RandfRange(-0.6f, 0.6f), d = rng.RandfRange(10, 46), r = rng.RandfRange(1.5f, 4f);
            Add(Ink.Blob(pos + Vector2.FromAngle(a) * d, r * 1.5f, r, a, seed++, 0.2f, 8), c);
        }
    }

    /// <summary>A scorched ring of soot where a bomb went off.</summary>
    public void Scorch(Vector2 pos, float radius)
    {
        var soot = new Color(0.12f, 0.1f, 0.09f, 0.32f);
        Add(Ink.Blob(pos, radius * 0.55f, radius * 0.5f, rng.RandfRange(0, Mathf.Tau), seed++, 0.4f, 20), soot);
        for (int i = 0; i < 7; i++)
        {
            float a = i * Mathf.Tau / 7 + rng.RandfRange(-0.3f, 0.3f);
            var tip = pos + Vector2.FromAngle(a) * radius * rng.RandfRange(0.7f, 1.05f);
            var side = Vector2.FromAngle(a).Orthogonal() * radius * 0.08f;
            Add([pos + side, tip, pos - side], new Color(soot, 0.22f));
        }
    }

    public void Clear() { stains.Clear(); dirty = true; }

    /// <summary>Switches to the stains of another room (each room keeps its own).</summary>
    public void Show(int floor, int x, int y)
    {
        if (!rooms.TryGetValue((floor, x, y), out var list)) rooms[(floor, x, y)] = list = [];
        stains = list;
        dirty = true;
    }

    /// <summary>Ink soaks into paper: a stain is the colour darkened and see-through.</summary>
    static Color Wash(Color c) => new(c.R * 0.75f, c.G * 0.72f, c.B * 0.72f, 0.42f);

    void Add(Vector2[] pts, Color c)
    {
        if (Geometry2D.TriangulatePolygon(pts).Length == 0) return; // a blob the jitter folded over itself: skip it once, here
        stains.Add(new Stain(pts, c));
        if (stains.Count > Max) stains.RemoveAt(0); // the oldest dry into the paper
        dirty = true;
    }

    public override void _Process(double delta)
    {
        if (!dirty) return;
        dirty = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        long t = Prof.Begin();
        InkBatch.Begin(this);
        DrawBody();
        InkBatch.End();
        Prof.End("draw:stains", t);
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
        foreach (var s in stains)
            DrawColoredPolygon(s.Pts, s.Col);
    }
}
