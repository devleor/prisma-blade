using Godot;
using Prisma.Sim;

namespace Prisma;

/// <summary>
/// Screen-space overlay in the sketchbook style: inked health and focus bars, the dungeon map of this floor, the
/// sword in hand and the relics, small handwritten banners, and the big round splash: a kanji slammed onto a
/// red brush stroke with ink flicks (第一波 wave one, 守護者 guardian, 決戦 boss, 第二層 floor two...).
/// </summary>
public partial class Hud : Control
{
    readonly Main main;
    string banner = "", sub = "";
    float bannerT, hurtT, perfectT;
    float shownHp = 100f;

    // the round splash
    string splashKanji = "", splashLabel = "", splashSub = "";
    float splashT, splashLen = 1.6f;
    Color splashCol = new(0.72f, 0.1f, 0.08f);
    int splashSeed;

    public string Banner => banner;
    public string SplashLabel => splashLabel;
    public float SplashLeft => splashT;

    /// <summary>Yuji Boku, a brush Japanese face (OFL, bundled); system Japanese faces as fallback.</summary>
    public static readonly Font Kanji = Ink.Bundled("res://assets/fonts/YujiBoku-Regular.ttf", new SystemFont
    {
        FontNames = ["HGGyoshotai", "HGSeikaishotaiPRO", "Yu Mincho", "MS Mincho", "Yu Gothic", "Meiryo", "MS Gothic"],
        FontWeight = 800,
    });

    static readonly string[] Numerals = ["〇", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十"];
    static string Num(int n) => n >= 0 && n < Numerals.Length ? Numerals[n] : n.ToString();

    public Hud(Main main)
    {
        this.main = main;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        impactRect = new ColorRect { Visible = false, MouseFilter = MouseFilterEnum.Ignore, Material = new ShaderMaterial { Shader = new Shader { Code = ImpactShader } } };
        impactRect.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(impactRect);
    }

    // ---------------------------------------------------------------- screen effects

    /// <summary>The screen as an ink negative: light becomes ink, dark becomes paper, strong reds stay red.</summary>
    const string ImpactShader = @"shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear;
void fragment() {
    vec3 c = texture(screen, SCREEN_UV).rgb;
    float l = dot(c, vec3(0.3, 0.59, 0.11));
    vec3 ink = vec3(0.07, 0.06, 0.05), paper = vec3(0.94, 0.9, 0.8);
    vec3 o = mix(paper, ink, smoothstep(0.32, 0.42, l));
    if (c.r > 0.3 && c.r > c.g * 1.8 && c.r > c.b * 1.8) o = vec3(0.78, 0.06, 0.05);
    COLOR = vec4(o, 1.0);
}";

    readonly ColorRect impactRect;
    ulong impactUntil, lastImpact;
    const ulong ImpactMs = 75;

    /// <summary>A one-frame ink negative on a big blow. Off in the settings; never more than one per second,
    /// so a big wave breaking guards one after another does not strobe.</summary>
    public bool Impact()
    {
        ulong now = Time.GetTicksMsec();
        if (!Settings.ImpactFrames || now - lastImpact < 1000 && lastImpact != 0) return false;
        lastImpact = now;
        impactUntil = now + ImpactMs; // real time: the kill cam's slow motion must not stretch the flash
        return true;
    }

    public bool ImpactShowing => impactRect.Visible;

    struct Splat { public Vector2 Pos; public float R, Rot, Life; public int Seed; }
    readonly List<Splat> splats = [];
    readonly RandomNumberGenerator fxRng = new();

    /// <summary>Ink flung onto the edges of the screen when the hero is hit (only the edges: the fight stays clear).</summary>
    void SplatEdges(int n)
    {
        var size = Size;
        for (int i = 0; i < n; i++)
        {
            int side = fxRng.RandiRange(0, 3);
            float t = fxRng.RandfRange(0.05f, 0.95f);
            var at = side switch
            {
                0 => new Vector2(t * size.X, 0), 1 => new Vector2(size.X, t * size.Y),
                2 => new Vector2(t * size.X, size.Y), _ => new Vector2(0, t * size.Y),
            };
            splats.Add(new Splat { Pos = at, R = fxRng.RandfRange(40f, 95f), Rot = fxRng.RandfRange(0, Mathf.Tau), Life = 1.6f, Seed = fxRng.RandiRange(1, 99999) });
        }
        if (splats.Count > 14) splats.RemoveRange(0, splats.Count - 14);
    }

    float speedT;
    int speedSeed;

    /// <summary>Speed lines rushing in from the edges (dash, perfect dodge).</summary>
    public void Speed(float seconds = 0.22f) { speedT = seconds; speedSeed = fxRng.RandiRange(1, 99999); }

    void DrawScreenFx(Vector2 size, Player p)
    {
        // low health: a dark red vignette breathing in from the edges
        if (p.Alive && p.Hp < p.MaxHp * 0.3f)
        {
            float low = 1f - p.Hp / (p.MaxHp * 0.3f);
            float a = (0.35f + 0.35f * low) * (0.75f + 0.25f * Mathf.Sin(Time.GetTicksMsec() / 160f));
            float d = 120f + 90f * low;
            var edge = new Color(0.45f, 0f, 0.03f, a);
            var clear = new Color(0.45f, 0f, 0.03f, 0f);
            Vector2 tl = Vector2.Zero, tr = new(size.X, 0), br = size, bl = new(0, size.Y);
            Vector2 itl = new(d, d), itr = new(size.X - d, d), ibr = size - new Vector2(d, d), ibl = new(d, size.Y - d);
            DrawPolygon([tl, tr, itr, itl], [edge, edge, clear, clear]);
            DrawPolygon([tr, br, ibr, itr], [edge, edge, clear, clear]);
            DrawPolygon([br, bl, ibl, ibr], [edge, edge, clear, clear]);
            DrawPolygon([bl, tl, itl, ibl], [edge, edge, clear, clear]);
        }

        foreach (var s in splats)
        {
            float a = Mathf.Clamp(s.Life / 0.8f, 0f, 1f) * 0.9f;
            var col = new Color(Ink.Line, a);
            Ink.Fill(this, Ink.Blob(s.Pos, s.R, s.R * 0.75f, s.Rot, s.Seed, 0.22f, 22), col, s.Seed);
            for (int k = 0; k < 5; k++)
            {
                var dir = Vector2.FromAngle(s.Rot + k * 1.3f);
                float r = 3f + 6f * Ink.Hash(s.Seed, k) * Ink.Hash(s.Seed, k);
                DrawCircle(s.Pos + dir * s.R * (1.15f + 0.5f * Mathf.Abs(Ink.Hash(s.Seed + 1, k))), Mathf.Abs(r) + 2f, col);
            }
        }

        if (speedT > 0)
        {
            float k = speedT / 0.22f;
            var c = size / 2;
            var col = new Color(Ink.Line, 0.6f * Mathf.Min(k, 1f));
            for (int i = 0; i < 26; i++)
            {
                float ang = (i + 0.5f * Ink.Hash(speedSeed, i)) * Mathf.Tau / 26f;
                var dir = Vector2.FromAngle(ang);
                // from just past the screen's border (along this direction) a wedge pointing inward
                float reach = Mathf.Min(Mathf.Abs(dir.X) > 1e-3f ? c.X / Mathf.Abs(dir.X) : 1e9f, Mathf.Abs(dir.Y) > 1e-3f ? c.Y / Mathf.Abs(dir.Y) : 1e9f) + 10f;
                var outer = c + dir * reach;
                float len = reach * (0.2f + 0.15f * Mathf.Abs(Ink.Hash(speedSeed + 3, i))) * (1.3f - Mathf.Min(k, 1f) * 0.3f);
                var inner = outer - dir * len;
                var side = dir.Orthogonal() * (3f + 3f * Mathf.Abs(Ink.Hash(speedSeed + 5, i)));
                DrawColoredPolygon([outer + side, outer - side, inner], col);
            }
        }
    }

    public void Show(string text, string subtitle = "", float seconds = 2.2f)
    {
        banner = text; sub = subtitle; bannerT = seconds;
    }

    /// <summary>The big splash for a new wave (or, with floor set, a new floor).</summary>
    public void Round(int n, string label, string subtitle = "", bool floor = false)
    {
        Splash(floor ? $"第{Num(n)}層" : $"第{Num(n)}波", label, subtitle, new Color(0.72f, 0.1f, 0.08f), 1.6f);
    }

    /// <summary>The splash when a guardian or the boss rises.</summary>
    public void Boss(string label, string subtitle) =>
        Splash(label.StartsWith("MONÓLITO") ? "決戦" : "守護者", label, subtitle, new Color(0.1f, 0.08f, 0.08f), 2f);

    public void Splash(string kanji, string label, string subtitle, Color col, float seconds)
    {
        splashKanji = kanji; splashLabel = label; splashSub = subtitle; splashCol = col;
        splashT = splashLen = seconds;
        splashSeed = (int)(Time.GetTicksUsec() & 0xFFFF);
    }

    float flashT, flashLen = 0.1f;
    Color flashCol;

    /// <summary>A white-ish flash over the whole screen for big impacts.</summary>
    public void Flash(Color c, float seconds) { flashCol = c; flashT = flashLen = seconds; }

    public void Hurt() { hurtT = 0.35f; SplatEdges(fxRng.RandiRange(2, 4)); }
    public void Perfect() => perfectT = 0.8f;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bannerT = Mathf.Max(0, bannerT - dt);
        hurtT = Mathf.Max(0, hurtT - dt);
        perfectT = Mathf.Max(0, perfectT - dt);
        splashT = Mathf.Max(0, splashT - dt);
        flashT = Mathf.Max(0, flashT - dt);
        speedT = Mathf.Max(0, speedT - dt);
        impactRect.Visible = Time.GetTicksMsec() < impactUntil;
        for (int i = splats.Count - 1; i >= 0; i--)
        {
            var s = splats[i];
            s.Life -= dt;
            if (s.Life <= 0) splats.RemoveAt(i); else splats[i] = s;
        }
        shownHp = Mathf.Lerp(shownHp, main.World.Player.Hp, 1 - Mathf.Exp(-6 * dt));
        QueueRedraw();
    }

    static Vector2[] Box(Rect2 r) => [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];

    void Bar(Rect2 r, float fill, Color col, int seed, float ghost = -1f, Color? ghostCol = null)
    {
        DrawRect(r.Grow(2), new Color(Ink.Paper, 0.9f));
        if (ghost >= 0) DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Mathf.Clamp(ghost, 0, 1), r.Size.Y)), ghostCol ?? new Color(1, 1, 1));
        var f = new Rect2(r.Position, new Vector2(r.Size.X * Mathf.Clamp(fill, 0, 1), r.Size.Y));
        DrawRect(f, col);
        Ink.Hatch(this, Box(f), 5f, 0.9f, new Color(0, 0, 0, 0.18f), 1f, seed + 1);
        Ink.Stroke(this, Box(r.Grow(2)), true, 2.4f, seed);
    }

    public override void _Draw()
    {
        long t = Prof.Begin();
        DrawBody();
        Prof.End("draw:hud", t);
    }

    void DrawBody()
    {
        var w = main.World;
        var p = w.Player;
        var size = Size;
        var hand = Ink.Hand;

        if (w.SlowMo > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(0.2f, 0.5f, 1f, 0.06f));
        if (hurtT > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(0.8f, 0.05f, 0.05f, hurtT * 0.45f));
        DrawScreenFx(size, p);

        // health, with the spin's recoverable part striped
        var hpRect = new Rect2(26, 22, 300, 16);
        Bar(hpRect, p.Hp / (float)p.MaxHp, new Color(0.72f, 0.14f, 0.12f), 10, shownHp / p.MaxHp, new Color(1f, 0.8f, 0.6f));
        if (p.Recover > 0)
        {
            float x0 = hpRect.Size.X * p.Hp / p.MaxHp, wRec = Mathf.Min(hpRect.Size.X - x0, hpRect.Size.X * p.Recover / p.MaxHp);
            for (float x = 0; x < wRec; x += 6)
                DrawLine(hpRect.Position + new Vector2(x0 + x, hpRect.Size.Y), hpRect.Position + new Vector2(x0 + Mathf.Min(wRec, x + 4), 0), new Color(0.3f, 0.55f, 0.25f, 0.8f), 1.5f);
        }
        DrawString(hand, new Vector2(334, 37), $"{p.Hp}/{p.MaxHp}", HorizontalAlignment.Left, -1, 18, Ink.Paper);

        // focus: two charges of Shatter
        var fRect = new Rect2(26, 50, 220, 10);
        bool ready = p.Focus >= Tune.ShatterCost;
        Bar(fRect, p.Focus / Tune.FocusMax, ready ? new Color(1.1f, 0.95f, 1.5f) : new Color(0.5f, 0.4f, 0.72f), 20);
        DrawLine(fRect.Position + new Vector2(fRect.Size.X / 2, -3), fRect.Position + new Vector2(fRect.Size.X / 2, 13), Ink.Line, 2);
        DrawString(hand, new Vector2(256, 62), ready ? "ESTILHAÇAR [Q]" : "foco", HorizontalAlignment.Left, -1, 16,
            ready ? new Color(1.2f, 1.1f, 1.5f) : new Color(Ink.Paper, 0.6f));

        // the sword in hand and the relics of this run
        float y = size.Y - 70;
        DrawString(hand, new Vector2(26, y), p.Weapon.Name, HorizontalAlignment.Left, -1, 22, Ink.Paper);
        DrawString(hand, new Vector2(26, y + 18), p.Weapon.Perk, HorizontalAlignment.Left, 520, 13, new Color(Ink.Paper, 0.65f));
        var relics = Enum.GetValues<RKind>().Where(k => p.Has(k) > 0).Select(k => p.Has(k) > 1 ? $"{Relics.Name(k)} ×{p.Has(k)}" : Relics.Name(k)).ToArray();
        if (relics.Length > 0)
            DrawString(hand, new Vector2(26, y - 26), string.Join(" · ", relics), HorizontalAlignment.Left, 700, 14, new Color(0.95f, 0.8f, 0.5f));

        // combo counter
        if (w.Combo >= 2)
        {
            float fade = 1f - Mathf.Clamp((w.ComboT - Tune.ComboDrop * 0.6f) / (Tune.ComboDrop * 0.4f), 0, 1);
            int sizeC = 34 + Mathf.Min(w.Combo, 40) / 2;
            var cc = w.Combo >= 20 ? new Color(1f, 0.55f, 0.2f, fade) : new Color(Ink.Paper, fade);
            DrawString(hand, new Vector2(size.X - 400, 132), $"{w.Combo}", HorizontalAlignment.Right, 130, sizeC, cc);
            DrawString(hand, new Vector2(size.X - 264, 132), "HITS", HorizontalAlignment.Left, -1, 20, new Color(Ink.Paper, 0.7f * fade));
        }

        DrawMap(w, size);

        // boss bar
        var boss = w.Enemies.FirstOrDefault(e => e.Boss);
        if (boss != null)
        {
            var b = new Rect2(size.X / 2 - 320, size.Y - 44, 640, 14);
            DrawString(hand, new Vector2(0, b.Position.Y - 8), "MONÓLITO", HorizontalAlignment.Center, size.X, 22, new Color(1f, 0.8f, 0.7f));
            Bar(b, boss.Hp / (float)boss.MaxHp, new Color(0.6f, 0.1f, 0.12f), 30);
            DrawRect(new Rect2(b.Position + new Vector2(0, b.Size.Y + 4), new Vector2(b.Size.X * boss.Poise / boss.MaxPoise, 3)), new Color(0.95f, 0.8f, 0.3f));
        }

        if (perfectT > 0)
        {
            var c = new Color(1.3f, 1.2f, 1.7f, Mathf.Clamp(perfectT * 2, 0, 1));
            DrawString(Kanji, new Vector2(0, size.Y * 0.3f), "完璧", HorizontalAlignment.Center, size.X, 64, c);
            DrawString(hand, new Vector2(0, size.Y * 0.3f + 34), "PERFEITO", HorizontalAlignment.Center, size.X, 26, c);
        }

        // small banner: a strip of paper low on the screen, clear of the altars
        bool final = w.Phase is Phase.Lost or Phase.Won;
        if (bannerT > 0 || final)
        {
            float a = final ? 1f : Mathf.Clamp(bannerT * 2, 0, 1);
            float by = final ? size.Y * 0.62f : size.Y * 0.74f;
            var strip = new Rect2(size.X / 2 - 330, by, 660, 74);
            Ink.Fill(this, Box(strip), new Color(Ink.Paper, 0.92f * a), 40);
            Ink.Stroke(this, Box(strip), true, 2.5f, 41, new Color(Ink.Line, a));
            DrawString(hand, new Vector2(0, by + 40), banner, HorizontalAlignment.Center, size.X, 34, new Color(Ink.Line, a));
            if (sub != "") DrawString(hand, new Vector2(0, by + 64), sub, HorizontalAlignment.Center, size.X, 16, new Color(Ink.Line, 0.75f * a));
            if (final) DrawString(Kanji, new Vector2(0, size.Y * 0.5f), w.Phase == Phase.Won ? "勝利" : "敗北", HorizontalAlignment.Center, size.X, 150,
                w.Phase == Phase.Won ? new Color(0.95f, 0.75f, 0.25f) : new Color(0.75f, 0.1f, 0.08f));
        }

        if (splashT > 0) DrawSplash(size);
        if (flashT > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(flashCol, 0.55f * flashT / flashLen));

        string hint = "WASD mover · mouse mirar · LMB combo · RMB segurar = pesado · Espaço dash · F giro · Q estilhaçar · ande até um portão aberto para trocar de sala · R reiniciar";
        DrawString(hand, new Vector2(0, size.Y - 8), hint, HorizontalAlignment.Center, size.X, 13, new Color(Ink.Paper, 0.35f));
    }

    /// <summary>
    /// The round splash: a red brush stroke sweeps across, the kanji slams down huge and settles (the camera shakes
    /// with it, from Main), ink flicks fly off, the label is written under it, and it all fades.
    /// </summary>
    void DrawSplash(Vector2 size)
    {
        float t = splashLen - splashT;               // seconds since it began
        float fade = Mathf.Clamp(splashT / 0.35f, 0, 1);
        var c = size / 2 + new Vector2(0, -size.Y * 0.08f);
        int saved = Ink.Boil;
        Ink.Boil = (int)(t * 12) + splashSeed;

        // the brush stroke: a long lumpy band that grows from left to right in the first 0.18 s
        float grow = Mathf.Clamp(t / 0.18f, 0, 1);
        float half = size.X * 0.42f;
        int n = 24;
        var top = new Vector2[n]; var bot = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)(n - 1);
            float x = Mathf.Lerp(-half, -half + 2 * half * grow, k);
            float thick = 70f * Mathf.Sin(Mathf.Pi * Mathf.Clamp(k * 1.15f, 0, 1)) + 12f;
            float wob = Ink.Hash(splashSeed, i) * 10f;
            top[i] = c + new Vector2(x, -thick + wob - k * 18f);
            bot[i] = c + new Vector2(x, thick * 0.8f + wob * 0.6f - k * 18f);
        }
        var band = top.Concat(bot.Reverse()).ToArray();
        var col = splashCol; col.A = 0.88f * fade;
        if (grow > 0.05f) Ink.Fill(this, band, col, splashSeed);
        // dry-brush streaks along the stroke
        for (int s = 0; s < 7; s++)
        {
            float yy = Mathf.Lerp(-50, 45, s / 6f);
            var a0 = c + new Vector2(-half * 0.95f, yy);
            var a1 = c + new Vector2(-half + 2 * half * grow * (0.75f + 0.25f * Mathf.Abs(Ink.Hash(splashSeed, s + 40))), yy - 16 + Ink.Hash(splashSeed, s) * 8);
            DrawLine(a0, a1, new Color(col.R * 0.6f, col.G * 0.5f, col.B * 0.5f, 0.35f * fade), 2f);
        }
        // ink flicks thrown off the stroke
        for (int i = 0; i < 18; i++)
        {
            var at = c + new Vector2(Ink.Hash(splashSeed, i + 70) * half * 1.1f, Ink.Hash(splashSeed, i + 90) * 150f);
            float r = 3f + 9f * Mathf.Abs(Ink.Hash(splashSeed, i + 110));
            DrawColoredPolygon(Ink.Blob(at, r * 1.4f, r, Ink.Hash(splashSeed, i) * 3f, splashSeed + i, 0.3f, 9), new Color(col, 0.8f * fade * grow));
        }

        // the kanji: slams in from huge, overshoots and settles
        float k2 = Mathf.Clamp((t - 0.08f) / 0.16f, 0, 1);
        float scale = k2 < 1 ? Mathf.Lerp(2.6f, 0.92f, k2 * k2) : 1f + 0.08f * Mathf.Exp(-(t - 0.24f) * 9f) * Mathf.Cos((t - 0.24f) * 30f);
        if (t > 0.08f)
        {
            int fs = 150;
            var ink = splashCol.R > 0.4f ? Ink.Line : new Color(0.95f, 0.15f, 0.1f);
            DrawSetTransform(c + new Vector2(0, 0), -0.06f, new Vector2(scale, scale));
            var ts = Kanji.GetStringSize(splashKanji, HorizontalAlignment.Left, -1, fs);
            var org = new Vector2(-ts.X / 2, ts.Y * 0.32f);
            DrawStringOutline(Kanji, org, splashKanji, HorizontalAlignment.Left, -1, fs, 16, new Color(Ink.Paper, 0.95f * fade));
            DrawString(Kanji, org, splashKanji, HorizontalAlignment.Left, -1, fs, new Color(ink, fade));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        // the label written underneath
        if (t > 0.25f)
        {
            float la = Mathf.Clamp((t - 0.25f) / 0.15f, 0, 1) * fade;
            DrawStringOutline(Ink.Hand, new Vector2(0, c.Y + 118), splashLabel, HorizontalAlignment.Center, size.X, 40, 8, new Color(Ink.Line, la));
            DrawString(Ink.Hand, new Vector2(0, c.Y + 118), splashLabel, HorizontalAlignment.Center, size.X, 40, new Color(Ink.Paper, la));
            if (splashSub != "")
            {
                DrawStringOutline(Ink.Hand, new Vector2(0, c.Y + 146), splashSub, HorizontalAlignment.Center, size.X, 18, 6, new Color(Ink.Line, la));
                DrawString(Ink.Hand, new Vector2(0, c.Y + 146), splashSub, HorizontalAlignment.Center, size.X, 18, new Color(Ink.Paper, 0.9f * la));
            }
        }
        Ink.Boil = saved;
    }

    /// <summary>The floor map, top right: rooms you have seen, and doorways to ones you have not.</summary>
    void DrawMap(World w, Vector2 size)
    {
        if (w.Map == null) return;
        const float cell = 22f, gap = 6f;
        var rooms = w.Map.Rooms.Values;
        var known = rooms.Where(r => r.Seen || Sides.All.Any(s => w.Map.Neighbor(r, s)?.Seen == true)).ToList();
        if (known.Count == 0) return;
        int minX = known.Min(r => r.X), maxX = known.Max(r => r.X), minY = known.Min(r => r.Y);
        var origin = new Vector2(size.X - 26 - (maxX - minX + 1) * (cell + gap), 22);
        Vector2 At(RoomNode r) => origin + new Vector2((r.X - minX) * (cell + gap), (r.Y - minY) * (cell + gap));
        DrawString(Ink.Hand, origin + new Vector2(0, -4), $"FASE {w.Map.Floor + 1}/{Dungeon.Floors}", HorizontalAlignment.Left, -1, 15, new Color(Ink.Paper, 0.8f));
        foreach (var r in known)
            foreach (var s in w.Map.Doors(r))
            {
                var n = w.Map.Neighbor(r, s)!;
                if (!known.Contains(n) || (int)s > 1) continue; // each corridor once
                DrawLine(At(r) + new Vector2(cell / 2, cell / 2), At(n) + new Vector2(cell / 2, cell / 2), new Color(Ink.Paper, 0.6f), 3f);
            }
        foreach (var r in known)
        {
            var box = new Rect2(At(r), new Vector2(cell, cell));
            var fill = r == w.Room ? new Color(0.85f, 0.2f, 0.15f) : r.Seen ? (r.Cleared ? Ink.Paper : Ink.PaperDark) : new Color(0.3f, 0.26f, 0.23f);
            DrawRect(box, fill);
            DrawRect(box, Ink.Line, false, 2f);
            if (!r.Seen) continue;
            string mark = r.Kind switch { RoomKind.Boss => "鬼", RoomKind.Treasure => "宝", RoomKind.Elite => "★", _ => "" };
            if (mark != "") DrawString(Kanji, box.Position + new Vector2(0, cell - 5), mark, HorizontalAlignment.Center, cell, 15, Ink.Line);
        }
    }
}
