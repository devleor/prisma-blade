using Godot;
using Prisma.Sim;
using V = System.Numerics.Vector2;

namespace Prisma;

/// <summary>
/// The game scene: reads input, steps the simulation at 60 Hz, turns its events into sound, shake, particles and
/// ink stains that stay on the map. The drawing (a hand-drawn sketchbook look, all in code) is in MainArt.cs.
/// </summary>
public partial class Main : Node2D
{
    /// <summary>Set by the headless check: no automatic run.</summary>
    public static bool Peaceful;

    public World World { get; private set; } = null!;
    public Camera2D Camera { get; private set; } = null!;
    public Fx Fx { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public Sfx Sfx { get; private set; } = null!;
    public Stains Stains { get; private set; } = null!;
    public Floor FloorArt { get; private set; } = null!;
    public WorldEnvironment Env { get; private set; } = null!;
    public readonly Dictionary<Ev, int> EventCounts = [];

    /// <summary>Tests aim with this instead of the mouse.</summary>
    public Vector2? AimOverride;
    /// <summary>When set, the sim's inputs come from here instead of the keyboard and pad (the devlog's autopilot).</summary>
    public System.Func<Inputs>? Pilot;
    /// <summary>Extra camera zoom and a shift of its focus, for filmed close-ups (1 and zero in play).</summary>
    public float ZoomMul = 1f;
    public Vector2 Focus;

    /// <summary>The offers on the altars as they stood last frame (the sim clears them as one is taken).</summary>
    Offer[] lastOffers = [];
    /// <summary>What the last altar gave, for the HUD.</summary>
    public Offer? LastTaken { get; private set; }

    bool mouseAim = true;
    float trauma;
    /// <summary>A directional camera kick toward the blow, on top of the random shake.</summary>
    Vector2 kick;
    readonly RandomNumberGenerator rng = new();

    /// <summary>World area kept on screen: the arena plus its walls, with room for the HUD bars.</summary>
    const float ViewW = Tune.HalfW * 2 + 120f, ViewH = Tune.HalfH * 2 + 150f;

    static Vector2 G(V v) => new(v.X, v.Y);

    public override void _Ready()
    {
        Controls.Register();
        Settings.Load();
        ProcessMode = ProcessModeEnum.Pausable; // freezes under the pause menu, whoever its parent is
        World = new World((uint)Time.GetTicksUsec() | 1u, waves: !Peaceful);

        // the whole arena on screen, centred and still, like a page; zoom is fitted to the window each frame
        AddChild(Camera = new Camera2D { Position = new Vector2(0, -12) });
        Camera.MakeCurrent();
        AddChild(FloorArt = new Floor(this));
        AddChild(Stains = new Stains());
        Stains.Show(World.Map.Floor, World.Room.X, World.Room.Y); // the start room keeps its ink too
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
        AddChild(WeatherFx = new Weather(this));
        WeatherFx.Set(Weather.Of(World.Map.Biome));
        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(Hud = new Hud(this));
        AddChild(Sfx = new Sfx());
        Sfx.Music("battle");
        if (!Peaceful) Hud.Show("PRISMA BLADE", "escolha uma porta · mate o guardião para abrir os portões", 3.5f);
    }

    /// <summary>Esc (or Start) opens the pause menu; the press that closes it is consumed by the menu, so it cannot reopen it.
    /// Its own action, not ui_cancel: ui_cancel also holds pad B, which is the shatter button in a fight.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Controls.Pause) && !Peaceful && Pause == null) { OpenPause(); GetViewport().SetInputAsHandled(); }
    }

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

        var before = G(p.Pos);
        long simT = Prof.Begin();
        World.Step(Pilot?.Invoke() ?? new Inputs
        {
            Move = new V(move.X, move.Y),
            Aim = new V(aim.X, aim.Y),
            Light = Input.IsActionJustPressed(Controls.Light),
            HeavyHeld = Input.IsActionPressed(Controls.Heavy),
            Dash = Input.IsActionJustPressed(Controls.Dash),
            Shatter = Input.IsActionJustPressed(Controls.Shatter),
            Special = Input.IsActionJustPressed(Controls.Special),
        });
        Prof.End("sim", simT);
        stride += (G(p.Pos) - before).Length();
        long reactT = Prof.Begin();
        foreach (var ev in World.Events) React(ev);
        Prof.End("react", reactT);
        if (World.Altars.Count > 0) lastOffers = [.. World.Altars.Select(a => a.Offer)];

        Ink.Boil = World.Frame / 6; // the ink lines are redrawn with a new wobble 10 times a second
        long artT = Prof.Begin();
        StepArt();
        Prof.End("stepart", artT);
        drawDirty = true;
    }

    /// <summary>The sim may step several times in one rendered frame; the picture is redrawn once per frame.</summary>
    bool drawDirty = true;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (drawDirty) { drawDirty = false; QueueRedraw(); }
        AgeGhosts(dt);
        trauma = Mathf.Max(0, trauma - 1.8f * dt);
        float s = trauma * trauma * 22f;
        var view = GetViewportRect().Size;
        float zoom = Mathf.Min(view.X / ViewW, view.Y / ViewH);
        float lean = 0f;
        if (killCamUntil != 0)
        {
            ulong now = Time.GetTicksMsec();
            if (now >= killCamUntil) { killCamUntil = 0; Engine.TimeScale = 1.0; }
            else lean = Mathf.Sin(Mathf.Pi * (1f - (killCamUntil - now) / (float)KillCamMs));
        }
        zoom *= (1f + 0.12f * lean) * ZoomMul;
        Camera.Zoom = new Vector2(zoom, zoom);
        kick = kick.Lerp(Vector2.Zero, 1f - Mathf.Exp(-14f * dt));
        Camera.Offset = new Vector2(rng.RandfRange(-s, s), rng.RandfRange(-s, s)) + kick + (lastKill - Camera.Position) * 0.35f * lean + Focus;
    }

    public Weather WeatherFx { get; private set; } = null!;

    /// <summary>A small shake from outside the fight (thunder).</summary>
    public void Rumble(float amount) => Shake(amount);

    /// <summary>Each sword has its own voice: daggers whip high, the spear hisses short, heavy blades groan low,
    /// the katana rings; the last cut of a chain is lower and louder.</summary>
    void SwingSound(WKind kind, bool finisher)
    {
        float pitch = kind switch
        {
            WKind.Adagas => 1.4f, WKind.Katana => 1.2f, WKind.Lanca => 1.3f, WKind.Runica => 1.0f,
            WKind.Montante => 0.68f, WKind.Martelo => 0.6f, WKind.Foice => 0.85f, _ => 1.05f,
        };
        if (finisher) pitch *= 0.82f;
        Sfx.Play("swing", finisher ? 0f : -2f, pitch);
        switch (kind)
        {
            case WKind.Montante or WKind.Martelo: Sfx.Play("heavy_swing", -6f, pitch * 0.8f); break;
            case WKind.Katana: Sfx.Play("deflect", -16f, 1.9f, 0.03f); break; // a thin ring of steel
            case WKind.Runica: Sfx.Play("perfect", -18f, 1.6f, 0.05f); break; // the runes hum
        }
    }

    /// <summary>What the blade bit into: stone thuds, metal clangs, flesh chops.</summary>
    void HitSound(EKind? kind, bool heavy)
    {
        switch (kind)
        {
            case EKind.Bastion or EKind.Monolith:
                Sfx.Play("stone", heavy ? 2f : -2f, heavy ? 0.85f : 1.05f);
                break;
            case EKind.Shield:
                Sfx.Play("clash", heavy ? -2f : -5f, heavy ? 0.95f : 1.2f);
                break;
            default:
                Sfx.Play("chop", heavy ? 0f : -3f, heavy ? 0.85f : 1.1f);
                Sfx.Play("hit", -8f, 1.2f);
                break;
        }
        if (heavy) Sfx.Play("heavy_hit", -1f, 0.9f);
    }

    void Shake(float amount) => trauma = Mathf.Min(1f, trauma + amount * Settings.Shake);

    static readonly Color CritCol = new(0.78f, 0.07f, 0.05f);
    bool critNext;
    Vector2 lastKill;
    ulong killCamUntil;
    const ulong KillCamMs = 550;

    /// <summary>The last blow of a room: time slows to a crawl for half a second and the camera leans in on it.</summary>
    void KillCam()
    {
        Engine.TimeScale = 0.25;
        killCamUntil = Time.GetTicksMsec() + KillCamMs;
    }

    public bool KillCamOn => killCamUntil != 0;

    public override void _ExitTree() => Engine.TimeScale = 1.0; // never leave the next scene in slow motion

    void Kick(float angle, float amount) => kick += Vector2.FromAngle(angle) * amount * Settings.Shake;

    /// <summary>The pause menu, over a frozen game (Esc).</summary>
    public Menu? Pause { get; private set; }

    public void OpenPause()
    {
        killCamUntil = 0; Engine.TimeScale = 1.0; // the pause menu must not run in slow motion
        var layer = new CanvasLayer { Layer = 10, ProcessMode = ProcessModeEnum.Always };
        layer.AddChild(Pause = new Menu { Paused = true });
        layer.TreeExited += () => Pause = null;
        AddChild(layer);
        GetTree().Paused = true;
    }

    public void React(GameEvent ev)
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
                    Fx.Ring(pos, 30, Moves.Spin.Reach, 0.25f, new Color(Ink.Line, 0.7f), 5f);
                    Shake(0.15f);
                }
                else if (ev.I == Moves.HeavyIndex)
                {
                    Sfx.Play("heavy_swing", 2f, p.Weapon.Kind is WKind.Montante or WKind.Martelo ? 0.45f : 0.6f);
                    Sfx.Play("swing", -4f, 0.8f);
                    Shake(0.12f);
                }
                else SwingSound(p.Weapon.Kind, ev.I == p.Weapon.Light.Length - 1);
                break;
            case Ev.Crit:
                critNext = true;
                break;
            case Ev.Hit:
            {
                bool heavy = ev.I == 1;
                bool crit = critNext;
                critNext = false;
                float a = (pos - G(p.Pos)).Angle();
                // the event carries the kind: a killing blow's target is already gone from World.Enemies
                EKind? struck = ev.K >= 0 ? (EKind)ev.K : null;
                HitSound(struck, heavy);
                Fx.Sparks(pos, a, 0.7f, heavy ? HeavyCol : Ink.Line, heavy ? 14 : 8, heavy ? 700 : 520);
                if (crit)
                {
                    // a critical cut: a big red number under 会心, a ring of steel, more shake, a burst of ink
                    Fx.Number(pos, ((int)ev.A).ToString(), CritCol, heavy ? 46 : 38, "会心");
                    Sfx.Play("deflect", -4f, 1.7f);
                    Fx.Ring(pos, 10f, 70f, 0.22f, CritCol, 5f);
                    Stains.Drops(pos, CritCol, a, 5);
                    Shake(0.25f);
                    if (heavy) Hud.Impact();
                }
                else Fx.Number(pos, ((int)ev.A).ToString(), heavy ? HeavyCol : Ink.Line, heavy ? 30 : 22);
                Stains.Drops(pos, struck is { } sk ? KindColor(sk) : Ink.Line, a, heavy ? 6 : 3);
                Stains.Cut(pos, a + Mathf.Pi / 2f, heavy, crit); // the blade scores the floor across its swing
                Shake(heavy ? 0.32f : 0.14f);
                Kick(a, heavy ? 16f : 6f);
                if (heavy && p.HeavySwing && p.Weapon.FullCharge(p.Charge)) Hud.Flash(new Color(1f, 0.9f, 0.7f), 0.12f);
                break;
            }
            case Ev.Kill:
            {
                var kind = (EKind)ev.I;
                var col = KindColor(kind);
                lastKill = pos;
                bool boss = kind == EKind.Monolith;
                Fx.Shards(pos, col, boss ? 80 : 18, boss ? 900 : 520, ev.A * 0.45f);
                Fx.Ring(pos, ev.A, ev.A * 4f, 0.45f, new Color(Ink.Line, 0.6f), 4f);
                Stains.Splat(pos, ev.A * (boss ? 1.6f : 1.1f), col, (pos - G(p.Pos)).Angle(), boss ? 16 : 7);
                Sfx.Play("enemy_die", 0f, boss ? 0.5f : kind == EKind.Imp ? 1.4f : 1f);
                Kick((pos - G(p.Pos)).Angle(), boss ? 40f : 12f);
                if (boss) { Sfx.Play("shatter", 3f, 0.6f); Shake(1f); Hud.Flash(new Color(1f, 1f, 1f), 0.3f); }
                else Shake(0.25f);
                break;
            }
            case Ev.Dash:
                Hud.Speed();
                Sfx.Play("dash", -3f, 1.05f);         // a bamboo swish...
                Sfx.Play("dash_air", -12f, 1.25f);    // ...and the air it leaves behind
                Fx.Puff(pos, new Color(0.55f, 0.48f, 0.4f, 0.5f), 0.35f, 3);
                break;
            case Ev.WallHit:
            {
                bool charge = ev.I == 1;
                Sfx.Play("wall", charge ? 2f : -2f, charge ? 0.85f : 1f);
                if (charge) Sfx.Play("heavy_hit", -2f, 0.55f);
                Fx.Puff(pos, new Color(0.55f, 0.5f, 0.42f, 0.75f), charge ? 0.8f : 0.5f, charge ? 6 : 4);
                Fx.Shards(pos, new Color(0.55f, 0.5f, 0.42f), charge ? 16 : 8, 380, 6f);
                Stains.Drops(pos, Ink.Line, 0, charge ? 5 : 2);
                if (charge) Fx.Number(pos, "NA PAREDE!", HeavyCol, 22);
                Shake(charge ? 0.4f : 0.25f);
                break;
            }
            case Ev.Bowl:
                Sfx.Play("hit", 0f, 0.75f);
                Sfx.Play("heavy_hit", -6f, 1.2f);
                Fx.Puff(pos, new Color(0.55f, 0.5f, 0.42f, 0.6f), 0.45f, 3);
                Fx.Number(pos, "STRIKE", HeavyCol, 20);
                Shake(0.2f);
                break;
            case Ev.Perfect:
                Sfx.Play("perfect", -2f, 1f, 0f);       // a clear bell...
                Sfx.Play("perfect_morph", -6f, 1f, 0f); // ...and time bending around the dodge
                Fx.Ring(pos, 20, Tune.PerfectMarkRadius, 0.5f, MagicCol, 6f);
                Hud.Perfect();
                Hud.Speed();
                Shake(0.2f);
                break;
            case Ev.PlayerHurt:
                Sfx.Play("player_hurt", 0f);
                Fx.Shards(pos, CapeCol, 8, 400, 7f);
                Stains.Drops(pos, CapeCol, rng.RandfRange(0, Mathf.Tau), 5);
                Hud.Hurt();
                Shake(0.45f);
                break;
            case Ev.Shoot:
                Sfx.Play("enemy_shoot", ev.A > 0 ? -2f : -8f, ev.A > 0 ? 0.7f : 1f);
                break;
            case Ev.Deflect:
                Sfx.Play("deflect", -1f, 1.1f);
                Fx.Sparks(pos, (pos - G(p.Pos)).Angle(), 0.5f, Ink.Line, 10, 600);
                Fx.Number(pos, "REBATIDO", Ink.Line, 16);
                break;
            case Ev.Block:
                Sfx.Play("clash", -2f, 1.15f);
                Sfx.Play("deflect", -10f, 1.4f);
                Fx.Sparks(pos, (pos - G(p.Pos)).Angle() + Mathf.Pi, 0.9f, new Color(1f, 0.9f, 0.6f), 10, 500);
                Fx.Number(pos, "BLOQUEOU", new Color(0.45f, 0.42f, 0.4f), 18);
                Shake(0.1f);
                break;
            case Ev.Lob:
                Sfx.Play("enemy_shoot", -6f, 0.55f);
                break;
            case Ev.Explode:
            {
                bool ours = ev.I == 1;
                Sfx.Play("heavy_hit", 2f, 0.5f);
                Sfx.Play("shatter", -6f, 0.7f);
                Fx.Ring(pos, 10, ev.A, 0.3f, ours ? HeavyCol : DangerCol, 7f);
                Fx.Puff(pos, new Color(0.2f, 0.18f, 0.16f, 0.7f), 0.9f, 6);
                Fx.Shards(pos, Ink.Line, 12, 500, 6f);
                Stains.Scorch(pos, ev.A);
                Shake(0.45f);
                Hud.Flash(new Color(1f, 0.7f, 0.4f), 0.08f);
                break;
            }
            case Ev.ShatterCast:
                Sfx.Play("charge", 0f, 1.7f, 0f);
                Fx.Ring(pos, 10, 200, 0.35f, MagicCol, 8f);
                Shake(0.25f);
                break;
            case Ev.Shatter:
                Sfx.Play("shatter", 0f, 1f + ev.I * 0.09f);
                Fx.Shards(pos, MagicCol, 22, 650, 9f);
                Fx.Ring(pos, 10, Tune.ShatterChainRadius, 0.3f, MagicCol, 5f);
                Fx.Number(pos, "ESTILHAÇO", new Color(0.45f, 0.3f, 0.7f), 18);
                Hud.Flash(new Color(0.8f, 0.7f, 1f), 0.06f);
                Stains.Drops(pos, new Color(0.45f, 0.35f, 0.75f), rng.RandfRange(0, Mathf.Tau), 4);
                Shake(0.3f);
                break;
            case Ev.Telegraph:
                if ((EKind)ev.I == EKind.Monolith) Sfx.Play("charge", -6f, 0.55f, 0f);
                break;
            case Ev.GuardBreak:
                Sfx.Play("guard_break", 0f, ev.A > 30f ? 0.8f : 1f);
                // 破 (break) slammed onto the arena over the stunned one, sized to it
                Fx.Stamp(pos + new Vector2(0, -ev.A - 30f), "破", 64f + ev.A * 1.4f, CritCol);
                if (ev.A > 30f || World.Enemies.Count <= 6) Hud.Impact();
                Fx.Ring(pos, ev.A, ev.A * 2.2f, 0.25f, new Color(0.95f, 0.8f, 0.3f), 5f);
                Shake(0.3f);
                Kick((pos - G(p.Pos)).Angle(), 10f);
                break;
            case Ev.Stomp:
            {
                // a big one's footfall: the floor jolts (more when it is near you) and a dusty print stays
                float near = 1f - Mathf.Clamp((pos - G(p.Pos)).Length() / 700f, 0f, 1f);
                Shake(0.03f + 0.07f * near * ev.A / 40f);
                Fx.Puff(pos, new Color(0.6f, 0.52f, 0.42f), 0.4f + ev.A / 80f, 3);
                Stains.Drops(pos, new Color(0.35f, 0.3f, 0.26f), rng.RandfRange(0, Mathf.Tau), 2);
                Sfx.Play("heavy_hit", -16f, 0.35f);
                break;
            }
            case Ev.Hatch:
                Sfx.Play("enemy_die", -6f, 1.6f);
                Fx.Shards(pos, new Color(0.88f, 0.86f, 0.74f), 8, 160, 4f);
                Stains.Splat(pos, 14f, new Color(0.62f, 0.66f, 0.42f), rng.RandfRange(0, Mathf.Tau), 4);
                break;
            case Ev.Slam:
                Sfx.Play("heavy_hit", 0f, 0.55f);
                Fx.Ring(pos, 20, ev.A, 0.3f, new Color(Ink.Line, 0.7f), 7f);
                Fx.Shards(pos, KindColor(ev.I == (int)EKind.Ogre ? EKind.Ogre : EKind.Bastion), 10, 300, 6f);
                if (ev.I == (int)EKind.Ogre) { Shake(0.25f); Kick((pos - G(p.Pos)).Angle() + Mathf.Pi, 14f); }
                Stains.Scorch(pos, ev.A * 0.7f);
                Shake(0.3f);
                break;
            case Ev.Charge:
                Sfx.Play("charge", -6f, 0.8f, 0f);
                break;
            case Ev.ChargeFull:
                Sfx.Play("perfect", -5f, 1.5f, 0f);
                Fx.Ring(pos, 70, 20, 0.25f, HeavyCol, 5f);
                break;
            case Ev.Burn:
                Fx.Number(pos, ((int)ev.A).ToString(), new Color(0.95f, 0.45f, 0.1f), 16);
                break;
            case Ev.SpawnMark:
            {
                // an inked ring closes in on the spot where it will step in, in the colour of what is coming
                var col = KindColor((EKind)ev.I);
                float secs = ev.A / 60f;
                Fx.Ring(pos, 44f, 12f, secs, new Color(col, 0.85f), 3f);
                Fx.Ring(pos, 30f, 8f, secs, new Color(Ink.Line, 0.6f), 1.5f);
                Stains.Drops(pos, new Color(Ink.Line, 0.5f), rng.RandfRange(0, Mathf.Tau), 1);
                break;
            }
            case Ev.Heal:
                Fx.Number(pos + new Vector2(0, -20), $"+{(int)ev.A}", new Color(0.3f, 0.6f, 0.25f), 20);
                break;
            case Ev.RoomEnter:
                FloorArt.Redraw();
                Stains.Show(World.Map.Floor, World.Room.X, World.Room.Y);
                WeatherFx.Set(Weather.Of(World.Map.Biome));
                trail.Clear(); ghosts.Clear();
                if ((RoomKind)(int)ev.A == RoomKind.Treasure) Hud.Show("TESOURO", "golpeie o altar que você quer levar", 2.2f);
                else if (!World.Room.Cleared) Sfx.Play("heavy_hit", -4f, 0.5f); // the gates slam shut behind you
                break;
            case Ev.WaveStart:
            {
                Sfx.Play("wave_start", -2f, 1f, 0f);
                int waves = World.Room.Waves.Length;
                string kind = World.BossRoom ? "SALA DO CHEFE" : World.Room.Kind == RoomKind.Elite ? "SALA ELITE" : $"ONDA {ev.I + 1}/{waves}";
                Hud.Round(ev.I + 1, kind, World.Map.Floor == 0 && ev.I == 0 && World.Room.Depth <= 1 ? "dash no último instante de um ataque = PERFEITO" : "");
                Shake(0.35f);
                break;
            }
            case Ev.WaveClear:
                Fx.Ring(pos, 10, 120, 0.6f, new Color(0.35f, 0.6f, 0.3f), 6f);
                break;
            case Ev.Guardian:
            {
                var g = (EKind)ev.I;
                Sfx.Play("wave_start", 2f, 0.6f, 0f);
                Sfx.Play("heavy_hit", 0f, 0.4f);
                if (g == EKind.Monolith) { Sfx.Music("boss"); Hud.Boss("MONÓLITO", $"fase {World.Map.Floor + 1} · quebre a guarda dele (barra amarela)"); }
                else Hud.Boss(ev.A > 1 ? $"{KindName(g).ToUpperInvariant()} E COMPANHIA" : $"GUARDIÃO: {KindName(g).ToUpperInvariant()}", "mate o guardião para abrir os portões");
                Shake(0.6f);
                break;
            }
            case Ev.GatesOpen:
                Sfx.Play("victory", -8f, 1.3f, 0f);
                Sfx.Play("heavy_hit", -2f, 0.6f);
                KillCam();
                Hud.Splash("開門", "PORTÕES ABERTOS", $"+{(int)ev.A} de vida · corte uma oferenda e siga", new Color(0.85f, 0.65f, 0.15f), 1.8f);
                Hud.Flash(new Color(1f, 0.95f, 0.8f), 0.25f);
                Shake(0.8f);
                if (World.Map.Floor >= 0 && !World.BossRoom) Sfx.Music("battle");
                break;
            case Ev.Portal:
                Sfx.Music("calm");
                Hud.Show("PORTAL ABERTO", "entre no portal para descer à próxima fase", 3f);
                Fx.Ring(pos, 10, 200, 0.8f, MagicCol, 8f);
                break;
            case Ev.NextFloor:
                Sfx.Music("battle");
                Sfx.Play("shatter", 0f, 0.5f);
            {
                var biome = Dungeon.BiomeOf(ev.I);
                Hud.Splash(BiomeKanji(biome), $"FASE {ev.I + 1} · {BiomeName(biome).ToUpper()}", "três espadas esperam na primeira sala", new Color(0.72f, 0.1f, 0.08f), 2.2f);
                Shake(0.6f);
                break;
            }
            case Ev.Altars:
                Sfx.Play("wave_start", -6f, 1.4f, 0f);
                break;
            case Ev.Reward:
            {
                Sfx.Play("perfect", 0f, 0.9f, 0f);
                Fx.Ring(pos, 10, 160, 0.5f, MagicCol, 6f);
                if (ev.I >= 0 && ev.I < lastOffers.Length)
                {
                    LastTaken = lastOffers[ev.I];
                    Hud.Show(LastTaken.Value.Title.ToUpperInvariant(), LastTaken.Value.Blurb, 2.2f);
                }
                lastOffers = [];
                break;
            }
            case Ev.Victory:
                KillCam();
                Hud.Impact();
                Sfx.Music("calm");
                Sfx.Play("victory", 0f, 1f, 0f);
                Hud.Show("VITÓRIA", $"maior combo: {World.BestCombo} hits · R para jogar de novo", 999f);
                break;
            case Ev.Defeat:
                Sfx.Music("calm");
                Sfx.Play("defeat", 0f, 1f, 0f);
                Hud.Show("ESTILHAÇADO", $"caiu na fase {World.Map.Floor + 1} de {Dungeon.Floors} · R para tentar de novo", 999f);
                break;
        }
    }
}
