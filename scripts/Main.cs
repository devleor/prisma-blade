using Godot;
using Prisma.Sim;
using V = System.Numerics.Vector2;

namespace Prisma;

/// <summary>
/// The game scene: reads input, steps the simulation at 60 Hz, turns its events into sound, shake and particles,
/// and draws a stone arena seen whole from above: textured floor, walls and pillars, flat-coloured shapes with ink
/// outlines and ground shadows. Only magic effects (shatter, perfect dodge) use HDR colours that glow.
/// </summary>
public partial class Main : Node2D
{
    /// <summary>Set by the headless check: no automatic waves.</summary>
    public static bool Peaceful;

    public World World { get; private set; } = null!;
    public Camera2D Camera { get; private set; } = null!;
    public Fx Fx { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public Sfx Sfx { get; private set; } = null!;
    public WorldEnvironment Env { get; private set; } = null!;
    public readonly Dictionary<Ev, int> EventCounts = [];

    /// <summary>Tests aim with this instead of the mouse.</summary>
    public Vector2? AimOverride;

    bool mouseAim = true;
    float trauma;
    readonly List<(float angle, float reach, int age, bool heavy)> trail = [];
    readonly List<(Vector2 pos, float facing, float life)> ghosts = [];
    readonly RandomNumberGenerator rng = new();

    static readonly Color Bg = new(0.05f, 0.045f, 0.04f);
    static readonly Color Ink = new(0.07f, 0.05f, 0.04f);
    static readonly Color PlayerCol = new(0.93f, 0.88f, 0.76f);
    static readonly Color HeavyCol = new(1.0f, 0.72f, 0.3f);
    static readonly Color DangerCol = new(0.95f, 0.22f, 0.14f);
    static readonly Color MagicCol = new(1.3f, 1.15f, 1.8f);
    static readonly Color Shadow = new(0f, 0f, 0f, 0.35f);

    public static Color KindColor(EKind k) => k switch
    {
        EKind.Spike => new Color(0.86f, 0.46f, 0.2f),
        EKind.Orb => new Color(0.56f, 0.42f, 0.76f),
        EKind.Bastion => new Color(0.46f, 0.6f, 0.34f),
        _ => new Color(0.62f, 0.14f, 0.2f),
    };

    Texture2D? floorTex, wallTex, pillarTex;

    /// <summary>World area kept on screen: the arena plus its walls, with room for the HUD bars.</summary>
    const float ViewW = Tune.HalfW * 2 + 120f, ViewH = Tune.HalfH * 2 + 150f;

    static Vector2 G(V v) => new(v.X, v.Y);

    public override void _Ready()
    {
        Controls.Register();
        World = new World((uint)Time.GetTicksUsec() | 1u, waves: !Peaceful);

        // the whole arena on screen, centred and still, like a stage; zoom is fitted to the window each frame
        AddChild(Camera = new Camera2D { Position = new Vector2(0, -12) });
        Camera.MakeCurrent();
        TextureRepeat = TextureRepeatEnum.Enabled;
        floorTex = Load("floor"); wallTex = Load("wall"); pillarTex = Load("pillar");
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Canvas,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowStrength = 1.0f,
            GlowBloom = 0f,
            GlowHdrThreshold = 1.05f, // only the HDR magic colours glow
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
        };
        env.SetGlowLevel(1, 1f); env.SetGlowLevel(2, 1f); env.SetGlowLevel(3, 0.7f); env.SetGlowLevel(4, 0.4f);
        AddChild(Env = new WorldEnvironment { Environment = env });

        AddChild(Fx = new Fx());
        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(Hud = new Hud(this));
        AddChild(Sfx = new Sfx());
        Sfx.Music("battle");
        if (!Peaceful) Hud.Show("PRISMA BLADE", "sobreviva às ondas e estilhace o Monólito", 3f);
    }

    static Texture2D? Load(string name) =>
        ResourceLoader.Exists($"res://assets/textures/{name}.png") ? GD.Load<Texture2D>($"res://assets/textures/{name}.png") : null;

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseMotion or InputEventMouseButton) mouseAim = true;
        else if (e is InputEventJoypadMotion jm && jm.Axis is JoyAxis.RightX or JoyAxis.RightY && Mathf.Abs(jm.AxisValue) > 0.4f) mouseAim = false;
        else if (e is InputEventJoypadButton) mouseAim = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Input.IsActionJustPressed(Controls.Restart) && !Peaceful) { GetTree().ReloadCurrentScene(); return; }

        var p = World.Player;
        var move = Input.GetVector(Controls.Left, Controls.Right, Controls.Up, Controls.Down);
        Vector2 aim;
        if (AimOverride is { } o) aim = o;
        else if (mouseAim) aim = GetGlobalMousePosition() - G(p.Pos);
        else aim = Input.GetVector(Controls.AimLeft, Controls.AimRight, Controls.AimUp, Controls.AimDown);

        World.Step(new Inputs
        {
            Move = new V(move.X, move.Y),
            Aim = new V(aim.X, aim.Y),
            Light = Input.IsActionJustPressed(Controls.Light),
            HeavyHeld = Input.IsActionPressed(Controls.Heavy),
            Dash = Input.IsActionJustPressed(Controls.Dash),
            Shatter = Input.IsActionJustPressed(Controls.Shatter),
            Special = Input.IsActionJustPressed(Controls.Special),
        });
        foreach (var ev in World.Events) React(ev);

        bool frozen = World.Hitstop > 0;
        if (!frozen)
        {
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                var t = trail[i];
                if (++t.age > 7) trail.RemoveAt(i); else trail[i] = t;
            }
            if (p.State == PState.Dash && World.Frame % 2 == 0) ghosts.Add((G(p.Pos), p.Facing, 0.25f));
        }
        if (p.BladeActive && (trail.Count == 0 || trail[^1].age > 0 || trail[^1].angle != p.BladeAngle))
            trail.Add((p.BladeAngle, p.BladeReach, 0, p.HeavySwing));
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            var g = ghosts[i];
            g.life -= dt;
            if (g.life <= 0) ghosts.RemoveAt(i); else ghosts[i] = g;
        }
        trauma = Mathf.Max(0, trauma - 1.8f * dt);
        float s = trauma * trauma * 22f;
        var view = GetViewportRect().Size;
        float zoom = Mathf.Min(view.X / ViewW, view.Y / ViewH);
        Camera.Zoom = new Vector2(zoom, zoom);
        Camera.Offset = new Vector2(rng.RandfRange(-s, s), rng.RandfRange(-s, s));
    }

    void Shake(float amount) => trauma = Mathf.Min(1f, trauma + amount);

    void React(GameEvent ev)
    {
        EventCounts[ev.Kind] = EventCounts.GetValueOrDefault(ev.Kind) + 1;
        var pos = G(ev.Pos);
        var p = World.Player;
        switch (ev.Kind)
        {
            case Ev.Swing:
                if (ev.I == Moves.SpinIndex)
                {
                    Sfx.Play("heavy_swing", 3f, 0.75f); Sfx.Play("swing", 0f, 0.7f);
                    Fx.Ring(pos, 30, Moves.Spin.Reach, 0.25f, new Color(1, 0.95f, 0.85f, 0.8f), 6f);
                    Shake(0.15f);
                }
                else if (ev.I == Moves.HeavyIndex) { Sfx.Play("heavy_swing", 2f, 0.6f); Shake(0.12f); }
                else Sfx.Play("swing", -2f, ev.I == 2 ? 0.8f : 1.05f);
                break;
            case Ev.Hit:
            {
                bool heavy = ev.I == 1;
                Sfx.Play(heavy ? "heavy_hit" : "hit", heavy ? 1f : -3f, heavy ? 0.9f : 1.1f);
                float a = (pos - G(p.Pos)).Angle();
                Fx.Sparks(pos, a, 0.7f, heavy ? HeavyCol : new Color(1f, 0.97f, 0.88f), heavy ? 14 : 8, heavy ? 700 : 520);
                Fx.Number(pos, ((int)ev.A).ToString(), heavy ? HeavyCol : new Color(1f, 0.96f, 0.9f), heavy ? 28 : 20);
                Shake(heavy ? 0.32f : 0.14f);
                break;
            }
            case Ev.Kill:
            {
                var col = KindColor((EKind)ev.I);
                bool boss = (EKind)ev.I == EKind.Monolith;
                Fx.Shards(pos, col, boss ? 80 : 18, boss ? 900 : 520, ev.A * 0.45f);
                Fx.Ring(pos, ev.A, ev.A * 4f, 0.45f, col, 5f);
                Sfx.Play("enemy_die", 0f, boss ? 0.5f : 1f);
                if (boss) { Sfx.Play("shatter", 3f, 0.6f); Shake(1f); }
                else Shake(0.2f);
                break;
            }
            case Ev.Dash:
                Sfx.Play("dash", -4f, 1.15f);
                Fx.Puff(pos, new Color(0.75f, 0.65f, 0.5f, 0.5f), 0.35f, 3);
                break;
            case Ev.WallHit:
            {
                bool charge = ev.I == 1;
                Sfx.Play("heavy_hit", charge ? 3f : 0f, charge ? 0.55f : 0.75f);
                Fx.Puff(pos, new Color(0.7f, 0.62f, 0.5f, 0.75f), charge ? 0.8f : 0.5f, charge ? 6 : 4);
                Fx.Shards(pos, new Color(0.55f, 0.52f, 0.46f), charge ? 16 : 8, 380, 6f);
                if (charge) Fx.Number(pos, "NA PAREDE!", new Color(1f, 0.85f, 0.5f), 20);
                Shake(charge ? 0.4f : 0.25f);
                break;
            }
            case Ev.Bowl:
                Sfx.Play("hit", 0f, 0.75f);
                Sfx.Play("heavy_hit", -6f, 1.2f);
                Fx.Puff(pos, new Color(0.7f, 0.62f, 0.5f, 0.6f), 0.45f, 3);
                Fx.Number(pos, "STRIKE", new Color(1f, 0.9f, 0.6f), 18);
                Shake(0.2f);
                break;
            case Ev.Perfect:
                Sfx.Play("perfect", 2f, 1f, 0f);
                Fx.Ring(pos, 20, Tune.PerfectMarkRadius, 0.5f, MagicCol, 6f);
                Hud.Perfect();
                Shake(0.2f);
                break;
            case Ev.PlayerHurt:
                Sfx.Play("player_hurt", 0f);
                Fx.Shards(pos, PlayerCol, 8, 400, 7f);
                Hud.Hurt();
                Shake(0.45f);
                break;
            case Ev.Shoot:
                Sfx.Play("enemy_shoot", ev.A > 0 ? -2f : -8f, ev.A > 0 ? 0.7f : 1f);
                break;
            case Ev.Deflect:
                Sfx.Play("hit", -2f, 1.6f);
                Fx.Sparks(pos, (pos - G(p.Pos)).Angle(), 0.5f, PlayerCol, 10, 600);
                break;
            case Ev.ShatterCast:
                Sfx.Play("charge", 0f, 1.7f, 0f);
                Fx.Ring(pos, 10, 200, 0.35f, MagicCol, 8f);
                Shake(0.25f);
                break;
            case Ev.Shatter:
                Sfx.Play("shatter", 0f, 1f + ev.I * 0.09f);
                Fx.Shards(pos, MagicCol, 22, 650, 9f);
                Fx.Ring(pos, 10, Tune.ShatterChainRadius, 0.3f, MagicCol, 5f);
                Fx.Number(pos, "ESTILHAÇO", MagicCol, 16);
                Shake(0.3f);
                break;
            case Ev.Telegraph:
                if ((EKind)ev.I == EKind.Monolith) Sfx.Play("charge", -6f, 0.55f, 0f);
                break;
            case Ev.Slam:
                Sfx.Play("heavy_hit", 0f, 0.55f);
                Fx.Ring(pos, 20, ev.A, 0.3f, KindColor(EKind.Bastion), 8f);
                Fx.Shards(pos, KindColor(EKind.Bastion), 10, 300, 6f);
                Shake(0.3f);
                break;
            case Ev.Charge:
                Sfx.Play("charge", -6f, 0.8f, 0f);
                break;
            case Ev.ChargeFull:
                Sfx.Play("perfect", -5f, 1.5f, 0f);
                Fx.Ring(pos, 70, 20, 0.25f, HeavyCol, 5f);
                break;
            case Ev.WaveStart:
                Sfx.Play("wave_start", -2f, 1f, 0f);
                if (World.BossRoom) { Sfx.Music("boss"); Hud.Show("MONÓLITO", "quebre a guarda dele (barra amarela) para abrir a defesa", 3f); }
                else Hud.Show($"SALA {ev.I + 1}", ev.I == 0 ? "dash no último instante de um ataque = PERFEITO" : "");
                break;
            case Ev.WaveClear:
                Hud.Show("ONDA LIMPA", "+25 de vida", 1.6f);
                Fx.Ring(pos, 10, 120, 0.6f, new Color(0.55f, 0.9f, 0.45f), 6f);
                break;
            case Ev.Victory:
                Sfx.Music("calm");
                Sfx.Play("victory", 0f, 1f, 0f);
                Hud.Show("VITÓRIA", $"maior combo: {World.BestCombo} hits · R para jogar de novo", 999f);
                break;
            case Ev.Defeat:
                Sfx.Music("calm");
                Sfx.Play("defeat", 0f, 1f, 0f);
                Hud.Show("ESTILHAÇADO", "R para tentar de novo", 999f);
                break;
        }
    }

    // ---------------------------------------------------------------- drawing

    public override void _Draw()
    {
        float hw = Tune.HalfW, hh = Tune.HalfH;
        DrawRect(new Rect2(-hw - 900, -hh - 700, (hw + 900) * 2, (hh + 700) * 2), Bg);
        DrawFloor(hw, hh);
        DrawWalls(hw, hh);

        foreach (var pl in World.Pillars) DrawCircle(G(pl.Pos) + new Vector2(10, 14), pl.Radius + 4, Shadow);
        foreach (var e in World.Enemies) DrawEllipseShadow(G(e.Pos), e.Radius);
        if (World.Player.Alive) DrawEllipseShadow(G(World.Player.Pos), 17);

        foreach (var e in World.Enemies) DrawTelegraph(e);
        foreach (var e in World.Enemies) DrawEnemy(e);
        foreach (var pl in World.Pillars) DrawPillar(pl);
        foreach (var pr in World.Projs)
        {
            var c = pr.Friendly ? new Color(1f, 0.95f, 0.8f) : DangerCol;
            DrawCircle(G(pr.Pos), pr.Radius + 2, Ink);
            DrawCircle(G(pr.Pos), pr.Radius, c);
            DrawCircle(G(pr.Pos), pr.Radius * 0.4f, new Color(1, 1, 0.9f));
        }
        DrawTrail();
        DrawPlayer();
        if (World.SlowMo > 0) DrawRect(new Rect2(-hw, -hh, hw * 2, hh * 2), new Color(0.3f, 0.35f, 0.6f, 0.12f));
    }

    void DrawFloor(float hw, float hh)
    {
        var area = new Rect2(-hw, -hh, hw * 2, hh * 2);
        if (floorTex != null)
            // 1 texel = 0.55 px: slabs about the size of the hero; darkened and greyed toward old stone
            DrawTextureRectRegion(floorTex, area, new Rect2(0, 0, area.Size.X / 0.55f, area.Size.Y / 0.55f), new Color(0.82f, 0.86f, 0.9f)); // cooler tint takes the red out of the pavers
        else DrawRect(area, new Color(0.22f, 0.2f, 0.18f));
        for (int i = 0; i < 6; i++) // soft vignette toward the walls
            DrawRect(new Rect2(-hw + i * 8, -hh + i * 8, (hw - i * 8) * 2, (hh - i * 8) * 2), new Color(0, 0, 0, 0.05f), false, 8f);
    }

    void DrawWalls(float hw, float hh)
    {
        const float t = 44f;
        var wallCol = new Color(1.0f, 0.95f, 0.88f);
        Rect2[] bands =
        [
            new(-hw - t, -hh - t, hw * 2 + t * 2, t), new(-hw - t, hh, hw * 2 + t * 2, t),
            new(-hw - t, -hh, t, hh * 2), new(hw, -hh, t, hh * 2),
        ];
        foreach (var b in bands)
        {
            if (wallTex != null) DrawTextureRectRegion(wallTex, b, new Rect2(b.Position / 0.5f, b.Size / 0.5f), wallCol);
            else DrawRect(b, new Color(0.35f, 0.33f, 0.3f));
        }
        DrawRect(new Rect2(-hw, -hh, hw * 2, hh * 2), Ink, false, 4);
        DrawRect(new Rect2(-hw - t, -hh - t, hw * 2 + t * 2, hh * 2 + t * 2), Ink, false, 4);
        DrawRect(new Rect2(-hw, -hh, hw * 2, 18), new Color(0, 0, 0, 0.3f)); // shadow of the top wall on the floor
    }

    void DrawPillar(Pillar pl)
    {
        var c = G(pl.Pos);
        var pts = Poly(c, pl.Radius, 24, 0);
        if (pillarTex != null)
        {
            var texSize = pillarTex.GetSize();
            var offset = new Vector2(pl.Pos.X, pl.Pos.Y) / texSize; // each pillar shows a different patch of rock
            var uvs = pts.Select(p => (p - c) * 2.2f / texSize + offset).ToArray(); // UVs are 0..1 across the texture
            DrawPolygon(pts, new[] { new Color(1f, 0.97f, 0.92f) }, uvs, pillarTex);
        }
        else DrawColoredPolygon(pts, new Color(0.4f, 0.38f, 0.35f));
        DrawArc(c + new Vector2(-3, -3), pl.Radius - 6, Mathf.Pi, Mathf.Pi * 1.5f, 12, new Color(1, 1, 1, 0.18f), 4); // lit rim
        DrawPolyline([.. pts, pts[0]], Ink, 4, true);
        DrawArc(c, pl.Radius * 0.6f, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.25f), 2);
    }

    void DrawEllipseShadow(Vector2 pos, float r)
    {
        var pts = new Vector2[16];
        for (int i = 0; i < 16; i++)
        {
            var d = Vector2.FromAngle(i * Mathf.Tau / 16);
            pts[i] = pos + new Vector2(6, r * 0.75f) + new Vector2(d.X * r * 1.05f, d.Y * r * 0.5f);
        }
        DrawColoredPolygon(pts, Shadow);
    }

    void DrawTelegraph(Enemy e)
    {
        if (e.State != EState.Telegraph) return;
        var pos = G(e.Pos);
        float pulse = 0.5f + 0.5f * Mathf.Sin(e.T * 0.6f);
        switch (e.Kind)
        {
            case EKind.Spike:
            {
                float k = Mathf.Clamp(e.T / 32f, 0, 1);
                DrawLine(pos, pos + G(e.Aim) * 400, DangerCol * new Color(1, 1, 1, 0.15f + 0.4f * k), 2 + 10 * k);
                break;
            }
            case EKind.Orb:
                DrawCircle(pos, e.Radius + 14 * (e.T / 30f), KindColor(EKind.Orb) * new Color(1, 1, 1, 0.35f));
                break;
            case EKind.Bastion:
            {
                float k = Mathf.Clamp(e.T / 46f, 0, 1);
                DrawCircle(pos, World.SlamRadius * k, DangerCol * new Color(1, 1, 1, 0.18f));
                DrawArc(pos, World.SlamRadius, 0, Mathf.Tau, 48, DangerCol * new Color(1, 1, 1, 0.45f + 0.4f * pulse), 3);
                break;
            }
            case EKind.Monolith:
                if (e.Mode == 0) DrawLine(pos, pos + G(e.Aim) * 900, DangerCol * new Color(1, 1, 1, 0.25f + 0.3f * pulse), e.Radius * 1.4f);
                else if (e.Mode == 1) DrawArc(pos, e.Radius + 10 + e.T * 2.5f, 0, Mathf.Tau, 48, DangerCol * new Color(1, 1, 1, 0.6f), 4);
                else DrawArc(pos, e.Radius + 30, e.T * 0.1f, e.T * 0.1f + Mathf.Tau * 0.75f, 32, KindColor(EKind.Spike), 4);
                break;
        }
    }

    void DrawEnemy(Enemy e)
    {
        var col = KindColor(e.Kind);
        var pos = G(e.Pos);
        float scale = 1f, alpha = 1f;
        if (e.State == EState.Spawn)
        {
            float k = Mathf.Clamp(e.T / 40f, 0, 1);
            scale = 0.3f + 0.7f * k; alpha = ((int)e.T % 6 < 3) ? k : k * 0.4f;
            DrawArc(pos, e.Radius * (2.5f - 1.5f * k), 0, Mathf.Tau, 32, new Color(0.9f, 0.8f, 0.6f, 0.5f), 2);
        }
        if (e.State == EState.Stun) pos += new Vector2(Mathf.Sin(e.T * 1.7f), Mathf.Cos(e.T * 2.3f)) * 2.5f;
        var fill = e.Flash > 0 ? new Color(1, 1, 1) : col;
        if (e.State == EState.Telegraph) fill = fill.Lerp(new Color(1f, 0.95f, 0.85f), 0.2f + 0.25f * Mathf.Sin(e.T * 0.8f));
        fill.A = alpha;
        var ink = new Color(Ink, alpha);
        var dark = new Color(col.R * 0.55f, col.G * 0.55f, col.B * 0.55f, 0.45f * alpha);
        float r = e.Radius * scale;

        Vector2[] pts;
        switch (e.Kind)
        {
            case EKind.Spike:
            {
                float a = e.State is EState.Act or EState.Telegraph ? G(e.Aim).Angle()
                    : e.Vel.LengthSquared() > 100 ? G(e.Vel).Angle() : (G(World.Player.Pos) - pos).Angle();
                pts = [pos + Vector2.FromAngle(a) * r * 1.5f, pos + Vector2.FromAngle(a + 2.45f) * r, pos + Vector2.FromAngle(a - 2.45f) * r];
                break;
            }
            case EKind.Orb:
                pts = Poly(pos, r, 20, 0);
                break;
            case EKind.Bastion:
                pts = Poly(pos, r, 6, World.Frame * 0.005f);
                break;
            default:
            {
                float a = World.Frame * 0.01f;
                var ax = Vector2.FromAngle(a); var ay = ax.Orthogonal();
                pts = [pos + ax * r * 1.3f, pos + ay * r * 0.85f, pos - ax * r * 1.3f, pos - ay * r * 0.85f];
                break;
            }
        }
        DrawColoredPolygon(pts, fill);
        // shaded inner shape offset down-right: a cheap sense of volume with light from the top left
        var shade = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) shade[i] = pos + (pts[i] - pos) * 0.62f + new Vector2(r * 0.12f, r * 0.16f);
        DrawColoredPolygon(shade, dark);
        DrawPolyline([.. pts, pts[0]], ink, e.Boss ? 5 : 3.5f, true);
        if (e.Kind == EKind.Orb) { DrawCircle(pos, r * 0.38f, ink); DrawCircle(pos + new Vector2(-1, -1), r * 0.24f, new Color(1f, 0.92f, 0.75f, alpha)); }
        if (e.MaxPoise > 0 && e.Poise < e.MaxPoise)
            DrawArc(pos, r + 7, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * e.Poise / e.MaxPoise, 24, new Color(0.95f, 0.85f, 0.4f, 0.9f), 3);
        if (e.Boss && e.Phase2) DrawArc(pos, r * 1.6f, 0, Mathf.Tau, 40, DangerCol * new Color(1, 1, 1, 0.35f), 2);
        if (e.State == EState.Stun) // dazed: little stars circling
            for (int i = 0; i < 3; i++)
                DrawCircle(pos + new Vector2(0, -r - 10) + Vector2.FromAngle(e.T * 0.15f + i * 2.1f) * new Vector2(14, 5), 2.5f, new Color(1f, 0.9f, 0.5f));

        // fracture: glowing cracks and orbiting shards, one per stack (magic, so HDR)
        for (int i = 0; i < e.Fracture; i++)
        {
            float a = World.Frame * 0.08f + i * Mathf.Tau / e.Fracture;
            var c = pos + Vector2.FromAngle(a) * (r + 14);
            var f = Vector2.FromAngle(a * 2) * 6;
            DrawColoredPolygon([c + f, c + f.Orthogonal() * 0.6f - f * 0.4f, c - f.Orthogonal() * 0.6f - f * 0.4f], MagicCol);
            var crack = Vector2.FromAngle(i * 2.2f + e.Id) * r * 0.9f;
            DrawLine(pos - crack, pos + crack * 0.6f, MagicCol, 2);
        }

        if (!e.Boss && e.Hp < e.MaxHp && e.State != EState.Spawn)
        {
            var bar = new Rect2(pos + new Vector2(-e.Radius, -e.Radius - 16), new Vector2(e.Radius * 2, 4));
            DrawRect(bar.Grow(1), Ink);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * e.Hp / e.MaxHp, 4)), new Color(0.85f, 0.25f, 0.2f));
        }
    }

    static Vector2[] Poly(Vector2 c, float r, int n, float rot)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) pts[i] = c + Vector2.FromAngle(rot + i * Mathf.Tau / n) * r;
        return pts;
    }

    void DrawTrail()
    {
        if (trail.Count < 2) return;
        var c0 = G(World.Player.Pos);
        for (int i = 0; i < trail.Count - 1; i++)
        {
            var (a0, r0, age0, heavy) = trail[i];
            var (a1, r1, age1, _) = trail[i + 1];
            if (Mathf.Abs(Mathf.AngleDifference(a0, a1)) > 1.6f) continue;
            var col = heavy ? HeavyCol : new Color(1f, 0.97f, 0.9f);
            const int sub = 4;
            for (int s = 0; s < sub; s++)
            {
                float t0 = s / (float)sub, t1 = (s + 1) / (float)sub;
                float aa = Mathf.LerpAngle(a0, a1, t0), ab = Mathf.LerpAngle(a0, a1, t1);
                float ra = Mathf.Lerp(r0, r1, t0), rb = Mathf.Lerp(r0, r1, t1);
                float age = Mathf.Lerp(age0, age1, t0);
                var c = col; c.A = Mathf.Clamp(1f - age / 7f, 0, 1) * 0.5f;
                const float inner = 26f;
                DrawColoredPolygon([c0 + Vector2.FromAngle(aa) * inner, c0 + Vector2.FromAngle(aa) * ra, c0 + Vector2.FromAngle(ab) * rb, c0 + Vector2.FromAngle(ab) * inner], c);
            }
        }
    }

    void DrawPlayer()
    {
        var p = World.Player;
        var pos = G(p.Pos);
        foreach (var (gp, gf, life) in ghosts)
            DrawSquare(gp, gf, 15, new Color(PlayerCol, life * 1.4f), new Color(Ink, life * 2f));

        bool blink = p.Invuln > 0 && p.State != PState.Dash && World.Frame % 6 < 3;
        var body = p.State == PState.Hurt ? new Color(1f, 0.45f, 0.4f) : PlayerCol;
        if (!p.Alive) body = new Color(0.4f, 0.38f, 0.36f);
        if (blink) body.A = 0.45f;

        if (p.State == PState.Charge)
        {
            float k = p.Charge / (float)p.Weapon.ChargeFrames;
            bool full = p.Weapon.FullCharge(p.Charge);
            var c = full ? new Color(1.4f, 1.05f, 0.5f) : HeavyCol;
            c.A = full ? 0.6f + 0.4f * Mathf.Sin(World.Frame * 0.6f) : 0.35f + 0.4f * k;
            DrawArc(pos, Mathf.Lerp(70, 26, k), 0, Mathf.Tau, 40, c, full ? 5 : 3);
        }

        // sword: hilt near the body, tapering to the tip, steel with an ink outline and a leather grip
        if (p.Alive)
        {
            var dir = Vector2.FromAngle(p.BladeAngle);
            var side = dir.Orthogonal();
            float len = (p.State == PState.Attack ? p.BladeReach : 92f) - 6f;
            var hilt = pos + dir * 14;
            var steel = p.State == PState.Charge && p.Weapon.FullCharge(p.Charge) ? new Color(1.3f, 1.0f, 0.55f)
                : p.BladeActive && p.HeavySwing ? new Color(1f, 0.85f, 0.55f) : new Color(0.82f, 0.84f, 0.86f);
            if (blink) steel.A = 0.45f;
            Vector2[] blade = [hilt + side * 5, pos + dir * len + side * 1.5f, pos + dir * (len + 12), pos + dir * len - side * 1.5f, hilt - side * 5];
            DrawColoredPolygon(blade, steel);
            DrawPolyline([.. blade, blade[0]], new Color(Ink, steel.A), 2, true);
            DrawLine(hilt - side * 11, hilt + side * 11, Ink, 6);
            DrawLine(hilt - side * 10, hilt + side * 10, new Color(0.55f, 0.38f, 0.2f), 3);
        }

        float facing = p.State == PState.Dash ? G(p.Vel).Angle() : p.Facing;
        DrawSquare(pos, facing, 16, body, new Color(Ink, body.A));
        if (p.Recover > 0 && World.Frame % 20 < 10) DrawArc(pos, 25, 0, Mathf.Tau, 24, new Color(0.6f, 0.9f, 0.5f, 0.5f), 2);
        DrawCircle(pos + Vector2.FromAngle(p.Facing) * 8, 3.5f, Ink);
    }

    void DrawSquare(Vector2 c, float rot, float half, Color fill, Color line)
    {
        var ax = Vector2.FromAngle(rot + Mathf.Pi / 4) * half * 1.414f;
        var ay = ax.Orthogonal();
        Vector2[] pts = [c + ax, c + ay, c - ax, c - ay];
        if (fill.A > 0) DrawColoredPolygon(pts, fill);
        DrawPolyline([.. pts, pts[0]], line, 3.5f, true);
    }
}
