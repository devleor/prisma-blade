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

    public override void _Ready()
    {
        ShowBehindParent = true;
        AddChild(cutLayer = new CutLayer(this));
    }

    CutLayer cutLayer = null!;

    /// <summary>The cuts fade every frame; on their own layer they do not make the dried stains redraw.</summary>
    sealed partial class CutLayer(Stains owner) : Node2D
    {
        public CutLayer() : this(null!) { }

        public override void _Draw()
        {
            long t = Prof.Begin();
            InkBatch.Begin(this);
            owner.DrawCuts();
            InkBatch.End();
            Prof.End("draw:cuts", t);
        }
    }

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

    // ---------------------------------------------------------------- sword cuts

    struct CutMark { public Vector2 Pos, Dir; public float Len, Width, Bend, Age; public bool Red; }
    readonly List<CutMark> cuts = [];
    /// <summary>How long a cut stays on the floor (seconds), the last part spent fading.</summary>
    public const float CutLife = 7f, CutFade = 2.5f, CutGrow = 0.07f;
    public const int MaxCuts = 40;

    public int Cuts => cuts.Count;

    /// <summary>A gash scored into the floor where a blade struck, across the swing (<paramref name="angle"/>):
    /// it is drawn in a flash, stays a while and fades. Crits cut red.</summary>
    public void Cut(Vector2 pos, float angle, bool heavy, bool crit)
    {
        float len = (heavy ? 92f : 58f) * rng.RandfRange(0.85f, 1.15f);
        cuts.Add(new CutMark
        {
            Pos = pos, Dir = Vector2.FromAngle(angle + rng.RandfRange(-0.3f, 0.3f)), Len = len,
            Width = heavy ? 7f : 4.5f, Bend = rng.RandfRange(-0.18f, 0.18f) * len, Red = crit,
        });
        if (cuts.Count > MaxCuts) cuts.RemoveAt(0);
        cutLayer?.QueueRedraw();
    }

    /// <summary>A lens: sharp at both ends, widest in the middle, bowed a little like a real slash.</summary>
    static Vector2[] Gash(Vector2 c, Vector2 dir, float len, float width, float bend, float shown)
    {
        const int n = 9;
        var side = dir.Orthogonal();
        float l = len * shown; // the cut grows from its start while the blade passes
        var start = c - dir * len * 0.5f;
        Vector2 Mid(float u) => start + dir * (u * l) + side * bend * shown * (1f - 4f * (u - 0.5f) * (u - 0.5f));
        // one shared point at each tip, then the two edges between them (no repeated points: they break triangulation)
        var pts = new Vector2[2 * n - 2];
        pts[0] = Mid(0f);
        pts[n - 1] = Mid(1f);
        for (int i = 1; i < n - 1; i++)
        {
            float u = i / (float)(n - 1), w = width * Mathf.Sin(Mathf.Pi * u) * 0.5f;
            pts[i] = Mid(u) + side * w;
            pts[2 * n - 2 - i] = Mid(u) - side * w * 0.6f;
        }
        return pts;
    }

    public void Clear() { stains.Clear(); cuts.Clear(); cutLayer?.QueueRedraw(); dirty = true; }

    /// <summary>Switches to the stains of another room (each room keeps its own).</summary>
    public void Show(int floor, int x, int y)
    {
        if (!rooms.TryGetValue((floor, x, y), out var list)) rooms[(floor, x, y)] = list = [];
        stains = list;
        cuts.Clear(); cutLayer?.QueueRedraw(); // cuts are fresh marks of this fight, they do not wait for you in another room
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
        if (cuts.Count > 0)
        {
            for (int i = cuts.Count - 1; i >= 0; i--)
            {
                var c = cuts[i];
                c.Age += (float)delta;
                if (c.Age >= CutLife) cuts.RemoveAt(i); else cuts[i] = c;
            }
            cutLayer.QueueRedraw(); // only the cut layer: the dried stains stay as they were
        }
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

    void DrawCuts()
    {
        foreach (var c in cuts)
        {
            float shown = Mathf.Clamp(c.Age / CutGrow, 0.05f, 1f);
            float a = Mathf.Clamp((CutLife - c.Age) / CutFade, 0f, 1f);
            var ink = c.Red ? new Color(0.55f, 0.04f, 0.03f, 0.75f * a) : new Color(0.08f, 0.06f, 0.05f, 0.7f * a);
            InkBatch.Polygon(Gash(c.Pos, c.Dir, c.Len, c.Width, c.Bend, shown), ink);
            // the lip of the cut catches the light: a thin pale line along one side
            var side = c.Dir.Orthogonal();
            var lip = Gash(c.Pos + side * (c.Width * 0.55f), c.Dir, c.Len * 0.8f, c.Width * 0.35f, c.Bend, shown);
            InkBatch.Polygon(lip, new Color(Ink.Paper, 0.45f * a));
        }
    }
}
