using Godot;

namespace Prisma;

/// <summary>
/// The title menu, the pause menu and the settings page, drawn on the same sketchbook paper as the game.
/// The title slams in with its kanji (光刃, "light blade"); the chosen line gets a red brush stroke that sweeps in
/// behind it, a scribbled underline and a little jolt; confirming flicks ink that stays on the page. Keyboard,
/// mouse and gamepad all work: up/down to choose, left/right to change a value, accept/cancel.
/// </summary>
public partial class Menu : Control
{
    public enum Page { Title, Pause, Settings }

    /// <summary>In the pause menu the game under it is frozen; closing it lets the game run again.</summary>
    public bool Paused;
    public Page Current { get; private set; }
    public int Selected { get; private set; }
    public IReadOnlyList<string> Items => items;
    public int StainCount => stains.Count;
    /// <summary>What the last confirm asked for (play, quit, resume...), for the checks.</summary>
    public string LastAction { get; private set; } = "";
    /// <summary>Off in the checks: actions are recorded but the scene is not changed.</summary>
    public bool Live = true;

    readonly List<string> items = [];
    float[] glow = [];               // per item, 0..1: how far its brush stroke has swept in
    float t, trauma, titleT;
    Vector2 kick;
    Page back = Page.Title;
    readonly RandomNumberGenerator rng = new();
    readonly List<(Vector2[] pts, Color col)> stains = [];
    readonly List<(Vector2 pos, Vector2 vel, float r, float life)> flicks = [];
    Sfx? sfx;

    static readonly Color Red = new(0.72f, 0.12f, 0.1f);

    public override void _Ready()
    {
        Settings.Load();
        Controls.Register();
        SetAnchorsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always; // the pause menu runs while the tree is paused
        rng.Seed = 7;
        AddChild(sfx = new Sfx());
        if (!Paused) sfx.Music("battle");
        Open(Paused ? Page.Pause : Page.Title);
    }

    void Open(Page page)
    {
        Current = page;
        items.Clear();
        switch (page)
        {
            case Page.Title: items.AddRange(["JOGAR", "CONFIGURAÇÕES", "SAIR"]); break;
            case Page.Pause: items.AddRange(["CONTINUAR", "CONFIGURAÇÕES", "RECOMEÇAR", "MENU PRINCIPAL"]); break;
            case Page.Settings: items.AddRange(["Volume geral", "Música", "Efeitos", "Tremor de tela", "Tela cheia", "Quadro de impacto", "VOLTAR"]); break;
        }
        glow = new float[items.Count];
        Selected = 0;
        titleT = 0f;
        Jolt(0.5f, new Vector2(0, 1));
    }

    // ---------------------------------------------------------------- input

    public override void _Input(InputEvent e)
    {
        if (e.IsActionPressed("ui_up") || e.IsActionPressed(Controls.Up)) { Move(-1); Accept(); }
        else if (e.IsActionPressed("ui_down") || e.IsActionPressed(Controls.Down)) { Move(1); Accept(); }
        else if (e.IsActionPressed("ui_left") || e.IsActionPressed(Controls.Left)) { Adjust(-1); Accept(); }
        else if (e.IsActionPressed("ui_right") || e.IsActionPressed(Controls.Right)) { Adjust(1); Accept(); }
        else if (e.IsActionPressed("ui_accept")) { Confirm(); Accept(); }
        else if (e.IsActionPressed("ui_cancel") || e.IsActionPressed(Controls.Pause)) { Cancel(); Accept(); }
        else if (e is InputEventMouseMotion mm)
        {
            int i = ItemAt(mm.Position);
            if (i >= 0 && i != Selected) { Selected = i; Moved(0); }
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            int i = ItemAt(mb.Position);
            if (i < 0) return;
            Selected = i;
            if (IsSliderItem(i)) SetSlider(i, Mathf.Clamp((mb.Position.X - SliderX0) / SliderW, 0f, 1f));
            else Confirm();
            Accept();
        }
    }

    void Accept() => GetViewport()?.SetInputAsHandled();

    public void Move(int d)
    {
        if (items.Count == 0) return;
        Selected = (Selected + d + items.Count) % items.Count;
        Moved(d);
    }

    void Moved(int d)
    {
        glow[Selected] = 0f; // the stroke sweeps in again
        Jolt(0.18f, new Vector2(0, d == 0 ? 1 : d));
        sfx?.Play("swing", -8f, 1.25f);
    }

    public void Adjust(int d)
    {
        if (Current != Page.Settings) return;
        int i = Selected;
        if (IsSlider(i)) SetSlider(i, Mathf.Clamp(SliderValue(i) + d * 0.1f, 0f, 1f));
        else if (i is 4 or 5) Toggle(i);
    }

    public void Confirm()
    {
        string item = items[Selected];
        Splash(ItemCenter(Selected), 1f);
        Jolt(0.45f, new Vector2(1, 0));
        sfx?.Play("hit", -4f, 1.1f);
        switch (Current, item)
        {
            case (Page.Title, "JOGAR"): Go("play"); break;
            case (Page.Title, "SAIR"): Go("quit"); break;
            case (_, "CONFIGURAÇÕES"): back = Current; Open(Page.Settings); break;
            case (Page.Pause, "CONTINUAR"): Go("resume"); break;
            case (Page.Pause, "RECOMEÇAR"): Go("restart"); break;
            case (Page.Pause, "MENU PRINCIPAL"): Go("title"); break;
            case (Page.Settings, "Tela cheia"): Toggle(4); break;
            case (Page.Settings, "Quadro de impacto"): Toggle(5); break;
            case (Page.Settings, "VOLTAR"): Settings.Save(); Open(back); break;
            case (Page.Settings, _): break; // sliders change with left/right or a click
        }
    }

    public void Cancel()
    {
        if (Current == Page.Settings) { Settings.Save(); Open(back); }
        else if (Current == Page.Pause) Go("resume");
    }

    void Go(string action)
    {
        LastAction = action;
        if (!Live) return;
        var tree = GetTree();
        switch (action)
        {
            case "play": tree.ChangeSceneToFile("res://scenes/Main.tscn"); break;
            case "quit": tree.Quit(); break;
            case "resume": tree.Paused = false; GetParent()?.QueueFree(); break;
            case "restart": tree.Paused = false; tree.ReloadCurrentScene(); break;
            case "title": tree.Paused = false; tree.ChangeSceneToFile("res://scenes/Menu.tscn"); break;
        }
    }

    static bool IsSlider(int i) => i <= 3;
    bool IsSliderItem(int i) => Current == Page.Settings && IsSlider(i);

    static float SliderValue(int i) => i switch { 0 => Settings.Master, 1 => Settings.Music, 2 => Settings.Effects, _ => Settings.Shake };

    void SetSlider(int i, float v)
    {
        if (Current != Page.Settings) return;
        v = Mathf.Snapped(v, 0.05f);
        switch (i) { case 0: Settings.Master = v; break; case 1: Settings.Music = v; break; case 2: Settings.Effects = v; break; default: Settings.Shake = v; break; }
        Settings.Apply();
        if (i == 3) Jolt(0.5f * v + 0.05f, new Vector2(1, 0)); // feel the new strength at once
        sfx?.Play("hit", -10f, 0.8f + v * 0.6f);
        Splash(SliderKnob(i), 0.35f);
    }

    void Toggle(int i)
    {
        if (i == 4) Settings.Fullscreen = !Settings.Fullscreen;
        else Settings.ImpactFrames = !Settings.ImpactFrames;
        Settings.Apply();
    }

    // ---------------------------------------------------------------- feel

    void Jolt(float amount, Vector2 dir)
    {
        trauma = Mathf.Min(1f, trauma + amount * Settings.Shake);
        kick += dir.Normalized() * 10f * amount * Settings.Shake;
    }

    /// <summary>Ink thrown off a confirm: drops fly out and land as stains that stay on the page.</summary>
    void Splash(Vector2 at, float size)
    {
        int n = (int)(5 + 7 * size);
        for (int i = 0; i < n; i++)
        {
            float a = rng.RandfRange(-Mathf.Pi, 0.3f), s = rng.RandfRange(160f, 520f) * size;
            flicks.Add((at + new Vector2(rng.RandfRange(-60f, 60f), 0), Vector2.FromAngle(a) * s, rng.RandfRange(2.5f, 7f) * Mathf.Max(0.5f, size), rng.RandfRange(0.35f, 0.6f)));
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        t += dt; titleT += dt;
        var view = GetViewportRect().Size; // fill the screen whatever the parent is (a CanvasLayer, the root, a test node)
        if (Size != view) { Position = Vector2.Zero; Size = view; }
        trauma = Mathf.Max(0f, trauma - 2.2f * dt);
        kick = kick.Lerp(Vector2.Zero, 1f - Mathf.Exp(-12f * dt));
        for (int i = 0; i < glow.Length; i++)
            glow[i] = i == Selected ? Mathf.Min(1f, glow[i] + dt / 0.16f) : Mathf.Max(0f, glow[i] - dt / 0.12f);
        for (int i = flicks.Count - 1; i >= 0; i--)
        {
            var f = flicks[i];
            f.vel += new Vector2(0, 900f) * dt;
            f.pos += f.vel * dt;
            f.life -= dt;
            if (f.life <= 0f)
            {
                flicks.RemoveAt(i);
                var blot = Ink.Blob(f.pos, f.r * 1.6f, f.r, f.vel.Angle(), stains.Count * 31 + 5, 0.25f, 10);
                if (Geometry2D.TriangulatePolygon(blot).Length > 0) stains.Add((blot, new Color(Red.R * 0.8f, Red.G, Red.B, 0.45f)));
                if (stains.Count > 160) stains.RemoveAt(0);
            }
            else flicks[i] = f;
        }
        Ink.Boil = (int)(t * 10f);
        QueueRedraw();
    }

    // ---------------------------------------------------------------- layout

    Vector2 Center => Size / 2f;
    float ItemsTop => Current == Page.Settings ? Size.Y * 0.36f : Size.Y * 0.58f;
    float Spacing => Current == Page.Settings ? 56f : 64f;
    public Vector2 ItemCenter(int i) => new(Current == Page.Settings ? Size.X * 0.36f : Center.X, ItemsTop + i * Spacing);
    float SliderX0 => Size.X * 0.5f;
    float SliderW => Size.X * 0.28f;
    Vector2 SliderKnob(int i) => new(SliderX0 + SliderW * SliderValue(i), ItemCenter(i).Y - 10f);

    int ItemAt(Vector2 p)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var c = ItemCenter(i);
            float x0 = Current == Page.Settings ? Size.X * 0.18f : c.X - 220f;
            float x1 = Current == Page.Settings ? SliderX0 + SliderW + 30f : c.X + 220f;
            if (p.X >= x0 && p.X <= x1 && p.Y >= c.Y - Spacing * 0.75f && p.Y <= c.Y + Spacing * 0.25f) return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------- drawing

    public override void _Draw()
    {
        float s = trauma * trauma * 18f;
        var off = new Vector2(rng.RandfRange(-s, s), rng.RandfRange(-s, s)) + kick;
        DrawSetTransform(off, 0f, Vector2.One);

        var size = Size;
        if (Paused) DrawRect(new Rect2(-off - new Vector2(40, 40), size + new Vector2(80, 80)), new Color(0.05f, 0.04f, 0.04f, 0.7f));
        var page = Paused
            ? new Rect2(size.X * 0.2f, size.Y * 0.1f, size.X * 0.6f, size.Y * 0.8f)
            : new Rect2(-40, -40, size.X + 80, size.Y + 80);
        DrawPaper(page);
        foreach (var (pts, col) in stains) DrawColoredPolygon(pts, col); // only shapes that triangulate are kept
        if (!Paused && Current == Page.Title) DrawSword(new Vector2(size.X * 0.84f, size.Y * 0.52f));

        if (Current == Page.Settings) DrawHeader("設定", "CONFIGURAÇÕES", size.Y * 0.2f, 0.55f);
        else if (Current == Page.Pause) DrawHeader("休止", "PAUSA", size.Y * 0.27f, 0.6f);
        else DrawHeader("光刃", "PRISMA BLADE", size.Y * 0.27f, 1f);

        for (int i = 0; i < items.Count; i++) DrawItem(i);

        foreach (var f in flicks)
            DrawColoredPolygon(Ink.Blob(f.pos, f.r * 1.5f, f.r, f.vel.Angle(), 77, 0.2f, 9), new Color(Red, 0.9f));

        string hint = Current == Page.Settings ? "↑↓ escolher · ←→ ajustar · Esc voltar" : "↑↓ escolher · Enter confirmar" + (Paused ? " · Esc continuar" : "");
        Ink.Write(this, new Vector2(size.X / 2, size.Y - (Paused ? size.Y * 0.13f : 30f)), hint, 16, new Color(Ink.Line, 0.55f), 800f, false);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    void DrawPaper(Rect2 r)
    {
        Vector2[] box = [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
        DrawColoredPolygon(box, Ink.Paper);
        // watercolour blooms soaked into the page (fixed: the paper itself never boils)
        for (int i = 0; i < 9; i++)
        {
            var c = r.Position + new Vector2(r.Size.X * (0.5f + 0.5f * Ink.Hash(i, 3)), r.Size.Y * (0.5f + 0.5f * Ink.Hash(i, 4)));
            float rad = 60f + 90f * Mathf.Abs(Ink.Hash(i, 5));
            DrawColoredPolygon(Ink.Blob(c, rad * 1.3f, rad, Ink.Hash(i, 6) * 3f, 1000 + i, 0.25f, 18), new Color(0.75f, 0.62f, 0.45f, 0.07f));
        }
        Ink.Hatch(this, [r.Position, new(r.End.X, r.Position.Y), new(r.End.X, r.Position.Y + 26f), new(r.Position.X, r.Position.Y + 26f)], 7f, 0.9f, new Color(Ink.Line, 0.12f), 1f, 50);
        if (Paused) Ink.Stroke(this, box, true, 3f, 51);
    }

    void DrawHeader(string kanji, string title, float y, float scale)
    {
        var c = new Vector2(Size.X / 2, y);
        // the title slams in: huge, then it lands with a small bounce (the page jolts with it on open)
        float k = Mathf.Clamp(titleT / 0.22f, 0f, 1f);
        float sc = k < 1f ? Mathf.Lerp(2.4f, 0.94f, k * k) : 1f + 0.06f * Mathf.Exp(-(titleT - 0.22f) * 8f) * Mathf.Cos((titleT - 0.22f) * 28f);
        sc *= scale;
        // red brush band behind the kanji
        float half = 300f * scale;
        var band = new List<Vector2>();
        for (int i = 0; i <= 14; i++)
        {
            float u = i / 14f, x = Mathf.Lerp(-half, half, Mathf.Min(1f, u / Mathf.Max(0.05f, k)));
            band.Add(c + new Vector2(x, -46f * scale * Mathf.Sin(Mathf.Pi * Mathf.Clamp(u * 1.1f, 0, 1)) - 8f + Ink.Hash(i, 9) * 8f - u * 14f));
        }
        for (int i = 14; i >= 0; i--)
        {
            float u = i / 14f, x = Mathf.Lerp(-half, half, Mathf.Min(1f, u / Mathf.Max(0.05f, k)));
            band.Add(c + new Vector2(x, 40f * scale * Mathf.Sin(Mathf.Pi * Mathf.Clamp(u * 1.1f, 0, 1)) + 6f + Ink.Hash(i, 10) * 6f - u * 14f));
        }
        Ink.Fill(this, [.. band], new Color(Red, 0.85f), 60);

        DrawSetTransform(c + kick, -0.05f, new Vector2(sc, sc));
        int fs = 130;
        var ts = Hud.Kanji.GetStringSize(kanji, HorizontalAlignment.Left, -1, fs);
        var org = new Vector2(-ts.X / 2, ts.Y * 0.3f);
        DrawStringOutline(Hud.Kanji, org, kanji, HorizontalAlignment.Left, -1, fs, 14, new Color(Ink.Paper, 0.95f));
        DrawString(Hud.Kanji, org, kanji, HorizontalAlignment.Left, -1, fs, Ink.Line);
        DrawSetTransform(kick, 0f, Vector2.One);
        Ink.Write(this, c + new Vector2(0, 110f * scale + 20f), title, (int)(54 * Mathf.Max(0.7f, scale)), Ink.Line, 900f);
    }

    void DrawItem(int i)
    {
        var c = ItemCenter(i);
        float g = glow[i];
        bool sel = i == Selected;
        bool settings = Current == Page.Settings;
        float wText = settings ? Size.X * 0.3f : 440f;
        float xL = settings ? Size.X * 0.18f : c.X - wText / 2;

        // the brush stroke sweeping in behind the chosen line
        if (g > 0.01f)
        {
            float len = (settings ? (SliderX0 + SliderW + 40f - xL) : wText + 40f) * EaseOut(g);
            var pts = new List<Vector2>();
            int n = 12;
            for (int k = 0; k <= n; k++) pts.Add(new Vector2(xL - 20f + len * k / n, c.Y - 30f - Mathf.Sin(k * 0.7f) * 3f + Ink.Hash(i * 13 + k, 1) * 4f));
            for (int k = n; k >= 0; k--) pts.Add(new Vector2(xL - 20f + len * k / n, c.Y + 8f + Mathf.Sin(k * 0.9f) * 3f + Ink.Hash(i * 13 + k, 2) * 4f));
            Ink.Fill(this, [.. pts], new Color(Red, 0.82f * g), 70 + i);
            // a scribbled underline drawn left to right
            var u0 = new Vector2(xL, c.Y + 14f);
            Ink.Stroke(this, [u0, u0 + new Vector2(len * 0.5f, 3f), u0 + new Vector2(len * 0.95f - 20f, -2f)], false, 2.5f, 90 + i);
        }

        float wob = sel ? Mathf.Sin(t * 9f) * 2f : 0f;
        var col = sel && g > 0.4f ? Ink.Paper : Ink.Line;
        int fs = settings ? 30 : 40;
        if (settings)
            DrawStringOutline(Ink.Hand, new Vector2(xL, c.Y + wob), items[i], HorizontalAlignment.Left, wText, fs, 6, sel ? new Color(Ink.Line, g) : new Color(Ink.Paper, 0.9f));
        else
            DrawStringOutline(Ink.Hand, new Vector2(xL, c.Y + wob), items[i], HorizontalAlignment.Center, wText, fs, 6, sel ? new Color(Ink.Line, g) : new Color(Ink.Paper, 0.9f));
        DrawString(Ink.Hand, new Vector2(xL, c.Y + wob), items[i], settings ? HorizontalAlignment.Left : HorizontalAlignment.Center, wText, fs, col);

        // a little ink sword points at the chosen line
        if (sel)
        {
            var tip = new Vector2(xL - 30f + Mathf.Sin(t * 6f) * 4f, c.Y - 10f);
            Ink.Shape(this, [tip, tip + new Vector2(-34, -5), tip + new Vector2(-34, 5)], new Color(0.82f, 0.84f, 0.88f), 95, 2f);
            Ink.Line2(this, tip + new Vector2(-34, -11), tip + new Vector2(-34, 11), 3f, 96);
            Ink.Line2(this, tip + new Vector2(-36, 0), tip + new Vector2(-50, 0), 4f, 97, new Color(0.35f, 0.2f, 0.12f));
        }

        if (IsSliderItem(i)) DrawSlider(i, c.Y - 10f, sel);
        else if (settings && i is 4 or 5)
            Ink.Write(this, new Vector2(SliderX0 + SliderW / 2, c.Y), (i == 4 ? Settings.Fullscreen : Settings.ImpactFrames) ? "sim" : "não", 30, sel && g > 0.4f ? Ink.Paper : Ink.Line, 300f, !sel);
    }

    void DrawSlider(int i, float y, bool sel)
    {
        float v = SliderValue(i);
        var a = new Vector2(SliderX0, y);
        var b = a + new Vector2(SliderW, 0);
        var ink = sel ? Ink.Paper : Ink.Line;
        // filled part: hatched ink
        var fillEnd = a + new Vector2(SliderW * v, 0);
        if (v > 0.01f)
        {
            Vector2[] bar = [a + new Vector2(0, -7), fillEnd + new Vector2(0, -7), fillEnd + new Vector2(0, 7), a + new Vector2(0, 7)];
            DrawColoredPolygon(bar, sel ? new Color(1f, 0.85f, 0.7f, 0.5f) : new Color(Red, 0.35f));
            Ink.Hatch(this, bar, 5f, 0.9f, new Color(ink, 0.6f), 1.2f, 200 + i);
        }
        Ink.Stroke(this, [a + new Vector2(0, -7), b + new Vector2(0, -7), b + new Vector2(0, 7), a + new Vector2(0, 7)], true, 2f, 210 + i, ink);
        for (int k = 0; k <= 10; k++)
        {
            var tk = a + new Vector2(SliderW * k / 10f, 0);
            DrawLine(tk + new Vector2(0, 9), tk + new Vector2(0, k % 5 == 0 ? 17 : 13), new Color(ink, 0.6f), 1.5f);
        }
        // the knob: an ink blot
        Ink.Shape(this, Ink.Circle(SliderKnob(i) + new Vector2(0, 10f), 11f, 220 + i, 0.12f), sel ? Ink.Paper : Red, 220 + i, 2.5f);
        Ink.Write(this, b + new Vector2(50f, 9f), $"{Mathf.RoundToInt(v * 100)}%", 22, Ink.Line, 90f);
    }

    /// <summary>A big boiling sword drawn down the right side of the title page.</summary>
    void DrawSword(Vector2 at)
    {
        var dir = Vector2.FromAngle(-1.25f + Mathf.Sin(t * 0.8f) * 0.03f);
        var side = dir.Orthogonal();
        float len = Size.Y * 0.62f;
        var hilt = at - dir * len * 0.35f;
        var tip = hilt + dir * len;
        Vector2[] blade = [hilt + side * 16f, tip - dir * 40f + side * 12f, tip, tip - dir * 40f - side * 12f, hilt - side * 16f];
        Ink.Shape(this, blade, new Color(0.84f, 0.86f, 0.9f), 300, 3f, 0.4f);
        Ink.Line2(this, hilt + dir * 10f, tip - dir * 50f, 2f, 301, new Color(Ink.Line, 0.45f));
        Ink.Shape(this, [hilt + side * 54f + dir * 4f, hilt + side * 54f - dir * 12f, hilt - side * 54f - dir * 12f, hilt - side * 54f + dir * 4f], new Color(0.62f, 0.48f, 0.2f), 302, 3f);
        Ink.Shape(this, [hilt - dir * 12f + side * 9f, hilt - dir * 92f + side * 9f, hilt - dir * 92f - side * 9f, hilt - dir * 12f - side * 9f], new Color(0.35f, 0.2f, 0.12f), 303, 3f, 0.5f);
        Ink.Shape(this, Ink.Circle(hilt - dir * 104f, 15f, 304), new Color(0.62f, 0.48f, 0.2f), 304, 3f);
        // a red tassel swinging from the pommel
        var knot = hilt - dir * 112f;
        float sw = Mathf.Sin(t * 2.2f) * 0.3f;
        for (int k = -1; k <= 1; k++)
            Ink.Stroke(this, [knot, knot + Vector2.FromAngle(Mathf.Pi / 2 + sw + k * 0.15f) * 50f, knot + Vector2.FromAngle(Mathf.Pi / 2 + sw * 1.5f + k * 0.2f) * 90f], false, 3f, 310 + k, Red);
    }

    static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
}
