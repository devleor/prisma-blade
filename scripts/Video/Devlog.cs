using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Prisma.Sim;
using V = System.Numerics.Vector2;

namespace Prisma.Video;

/// <summary>
/// Films devlog 1 with Godot's Movie Maker, cut to the recorded narration: gameplay shots (the hero on an autopilot,
/// always with the two-handed sword) alternate with ink "chalkboard" animations of how it works (<see cref="Board"/>),
/// and an ink wipe hides every cut (<see cref="Wipe"/>). Nothing is captioned: the voice explains, and it plays
/// inside the film, so the AVI comes out with it in sync.
/// The narration is two files kept OUT of the repository (it is a person's cloned voice): a WAV and a JSON with the
/// start and end of every sentence (made by transcribing the WAV). Each shot is tied to a sentence by its opening
/// words, so a re-recorded narration re-times the film by itself. Render (the speakers stay silent):
/// godot --path . --audio-driver Dummy --resolution 1920x1080 --fixed-fps 60 --write-movie ~/Videos/PrismaBlade/devlog1.avi res://scenes/video/Devlog.tscn -- voice=narracao.wav timing=narracao.json
/// </summary>
public partial class Devlog : Node
{
    public const int Fps = 60;

    /// <summary>One spoken sentence: frames where it starts and ends, and what it says.</summary>
    readonly record struct Line(int From, int To, string Text);
    readonly List<Line> lines = [];

    static string Home => OS.GetEnvironment("USERPROFILE") is { Length: > 0 } h ? h : OS.GetEnvironment("HOME");

    void LoadNarration(string voice, string timing)
    {
        var json = Json.ParseString(Godot.FileAccess.GetFileAsString(timing)).AsGodotDictionary();
        foreach (var l in json["lines"].AsGodotArray())
        {
            var d = l.AsGodotDictionary();
            lines.Add(new Line((int)(d["t0"].AsDouble() * Fps), (int)(d["t1"].AsDouble() * Fps), d["text"].AsString()));
        }
        end = (int)(json["duration"].AsDouble() * Fps);
        if (Godot.FileAccess.FileExists(voice)) AddChild(narration = new AudioStreamPlayer { Stream = AudioStreamWav.LoadFromFile(voice) });
        else GD.PushWarning($"DEVLOG: no narration at {voice}, filming silent");
    }

    AudioStreamPlayer? narration;

    /// <summary>The frame <paramref name="frac"/> of the way through the sentence that starts with <paramref name="words"/>.</summary>
    int Say(string words, float frac = 0f)
    {
        foreach (var l in lines)
            if (l.Text.StartsWith(words, StringComparison.OrdinalIgnoreCase))
                return l.From + (int)((l.To - l.From) * frac);
        throw new ArgumentException($"DEVLOG: no sentence starts with '{words}'");
    }

    // ---------------------------------------------------------------- state

    int frame, end;
    Main? main;
    Menu? menu;
    Board board = null!;
    int pilotT;
    bool heavyMode, pilotOn = true;
    int dashEvery;
    bool choppy;
    int lastCam = -1000;
    bool flashShot;
    EKind[] refill = [];
    int keep;
    readonly RandomNumberGenerator rng = new();
    bool boardsOnly;
    static readonly (Board.Kind, int)[] AllBoards =
    [
        (Board.Kind.Boil, 0), (Board.Kind.Calls, 0), (Board.Kind.Cascade, 0), (Board.Kind.Strip, 0), (Board.Kind.Cascade, 1),
        (Board.Kind.Numbers, 0), (Board.Kind.Doom, 0), (Board.Kind.Doom, 1), (Board.Kind.Doom, 2),
        (Board.Kind.Review, 0), (Board.Kind.Review, 1), (Board.Kind.Source, 0),
    ];

    public override void _Ready()
    {
        Settings.Persist = false;
        Settings.ImpactFrames = true;
        rng.Seed = 11;
        string voice = Home + "/Videos/PrismaBlade/narracao.wav", timing = Home + "/Videos/PrismaBlade/narracao.json";
        float until = float.MaxValue;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("voice=")) voice = arg[6..];
            if (arg.StartsWith("timing=")) timing = arg[7..];
            if (arg == "boards") boardsOnly = true; // test: just the chalkboard animations, 7 s each
            if (arg.StartsWith("until=")) float.TryParse(arg[6..], System.Globalization.CultureInfo.InvariantCulture, out until); // quick test renders
        }
        LoadNarration(voice, timing);
        if (until < float.MaxValue) end = Math.Min(end, (int)(until * Fps));
        // the game's own sounds sit well under the voice
        Settings.Music = 0.03f; // not saved: Persist is off
        Settings.Apply();
        // the game's sounds never spike over the voice: a limiter on their bus, and ducking under every sentence (Duck)
        int sfx = AudioServer.GetBusIndex(Settings.SfxBus);
        AudioServer.AddBusEffect(sfx, new AudioEffectHardLimiter { CeilingDb = -6f, PreGainDb = 0f });
        var layer = new CanvasLayer { Layer = 40 };
        AddChild(layer);
        layer.AddChild(board = new Board());
        var top = new CanvasLayer { Layer = 60 };
        AddChild(top);
        top.AddChild(wipe = new Wipe());
        if (!boardsOnly) { Cues(); PrintScript(); }
        ShowMenu();
    }

    // ---------------------------------------------------------------- stages

    void Clear()
    {
        InkBatch.Enabled = true;
        choppy = false;
        heavyMode = false;
        pilotOn = true;
        dashEvery = 0;
        flashShot = false;
        keep = 0;
        if (main != null) { main.QueueFree(); main = null; }
        if (menu != null) { menu.QueueFree(); menu = null; }
    }

    void ShowMenu()
    {
        Clear();
        AddChild(menu = new Menu { Live = false, Size = GetViewport().GetVisibleRect().Size });
    }

    /// <summary>A fresh run on <paramref name="floor"/>, standing in a fight room, with these enemies placed around.
    /// The hero always holds the two-handed sword (the user's call for this film).</summary>
    Main Arena(int floor, params (EKind kind, float x, float y)[] foes)
    {
        Clear();
        Main.Peaceful = true; // no waves, no intro banner: the film places its own enemies
        main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(main);
        var w = main.World;
        w.GoToFloor(floor);
        var fight = w.Map.Rooms.Values.First(r => r.Kind == RoomKind.Fight);
        w.EnterRoom(fight, Prisma.Sim.Side.S);
        main.FloorArt.Redraw();
        main.Stains.Show(floor, fight.X, fight.Y);
        main.WeatherFx.Set(Weather.Of(w.Map.Biome));
        w.Player.Weapon = Weapon.Of(WKind.Montante);
        w.Player.Pos = new V(0f, 170f);
        foreach (var (k, x, y) in foes) Foe(k, x, y);
        refill = [.. foes.Select(f => f.kind)];
        keep = foes.Length; // the two-handed sword clears a room fast: keep it populated while the shot lasts
        main.Pilot = Bot;
        pilotT = 0;
        return main;
    }

    Enemy Foe(EKind k, float x, float y)
    {
        var e = main!.World.Spawn(k, new V(x, y));
        e.AttackCd = 120f + rng.RandfRange(0f, 120f); // they fight back, but slowly enough to keep the shot readable
        return e;
    }

    /// <summary>Tops the room back up from its edges, one enemy at a time, so no shot runs out of things to cut.</summary>
    void Refill()
    {
        if (main == null || keep <= 0 || refill.Length == 0 || frame % 35 != 0) return;
        if (main.World.Enemies.Count >= keep) return;
        float side = rng.Randf() < 0.5f ? -1f : 1f;
        Foe(refill[rng.RandiRange(0, refill.Length - 1)], side * rng.RandfRange(380f, 520f), rng.RandfRange(-240f, 120f));
    }

    /// <summary>Walk up to the nearest enemy, face it and cut; heavy mode charges and releases instead.</summary>
    Inputs Bot()
    {
        var w = main!.World;
        var p = w.Player;
        p.Invuln = 1_000_000;
        p.Hp = Math.Max(p.Hp, 60);
        pilotT++;
        var inp = new Inputs();
        if (!pilotOn) return inp;
        Enemy? t = null;
        float best = float.MaxValue;
        foreach (var e in w.Enemies)
        {
            if (!e.Hittable) continue;
            float d = V.DistanceSquared(e.Pos, p.Pos);
            if (d < best) { best = d; t = e; }
        }
        if (t != null)
        {
            var d = t.Pos - p.Pos;
            float dist = d.Length();
            var dir = dist > 1e-3f ? d / dist : new V(0, -1);
            inp.Aim = dir;
            if (dist > 70f + t.Radius) inp.Move = dir;
            if (heavyMode) inp.HeavyHeld = pilotT % 70 < 48 && dist < 160f + t.Radius;
            else if (pilotT % 15 == 0 && dist < 130f + t.Radius) inp.Light = true;
        }
        if (dashEvery > 0 && pilotT % dashEvery == 0) inp.Dash = true;
        return inp;
    }

    void Show(Board.Kind kind, int phase = 0) { board.Show(kind, phase, frame); Silence(kind != Board.Kind.None); }
    void Hide() => Show(Board.Kind.None);

    /// <summary>While an animation covers the screen the game behind it holds still and makes no sound.</summary>
    void Silence(bool on)
    {
        boardOn = on;
        int bus = AudioServer.GetBusIndex(Settings.SfxBus);
        if (bus >= 0) AudioServer.SetBusMute(bus, on);
    }

    bool boardOn;

    /// <summary>The game always sits low under the voice (and is muted under the animations, see Silence).</summary>
    const float GameDb = -30f;
    float sfxDb = GameDb;

    void Duck()
    {
        sfxDb = GameDb;
        int bus = AudioServer.GetBusIndex(Settings.SfxBus);
        if (bus >= 0) AudioServer.SetBusVolumeDb(bus, sfxDb);
    }

    /// <summary>The stress room: a horde plus a big wave, kept full, the hero cutting through.</summary>
    void Crowd()
    {
        Arena(1);
        var r = new Rng(3);
        foreach (var k in Dungeon.HordeWave(12).Concat(Enumerable.Repeat(EKind.Spike, 10)).Concat(Enumerable.Repeat(EKind.Orb, 8)))
            Foe(k, r.Range(-500f, 500f), r.Range(-250f, 250f));
        refill = [EKind.Imp, EKind.Spike, EKind.Orb];
        keep = 30;
    }

    void Biome(int i)
    {
        EKind[] kinds = i switch
        {
            0 => [EKind.Spike, EKind.Bastion, EKind.Orb],
            1 => [EKind.Brood, EKind.Spiderling, EKind.Imp],
            2 => [EKind.Ogre, EKind.Bomber, EKind.Imp],
            3 => [EKind.Shield, EKind.Brood, EKind.Spiderling],
            _ => [EKind.Ogre, EKind.Orb, EKind.Shield],
        };
        Arena(i, (kinds[0], -260, -80), (kinds[1], 0, -170), (kinds[2], 260, -60));
    }

    void Crit(EKind kind)
    {
        var e = main?.World.Enemies.FirstOrDefault(x => x.Kind == kind) ?? main?.World.Enemies.FirstOrDefault();
        if (e == null) return;
        main!.React(new GameEvent(Ev.Crit, e.Pos, 27f, 1, (int)e.Kind));
        main.React(new GameEvent(Ev.Hit, e.Pos, 27f, 1, (int)e.Kind));
    }

    // ---------------------------------------------------------------- the shots, one per sentence

    /// <summary>What happens at a frame; a <c>cut</c> changes the whole picture, so an ink wipe hides it.</summary>
    readonly record struct Cue(int Frame, Action Do, bool Cut);
    readonly List<Cue> cues = [];
    Wipe wipe = null!;

    void On(int at, Action a, bool cut = false) => cues.Add(new Cue(at, a, cut));
    void CutAt(int at, Action a) => On(at, a, true);

    void Cues()
    {
        // ---- abertura: the menu, then a fight in the rain
        CutAt(Say("um hack"), () =>
        {
            Arena(1, (EKind.Imp, -260, -60), (EKind.Imp, 240, -100), (EKind.Spike, -60, -180), (EKind.Shield, 300, 60), (EKind.Spike, 120, -40));
            dashEvery = 140;
        });
        On(Say("O herói", 0.2f), () => Crit(EKind.Shield));
        On(Say("O herói", 0.75f), () => main?.React(new GameEvent(Ev.GuardBreak, main.World.Enemies.FirstOrDefault()?.Pos ?? new V(0, -100), 45f, (int)EKind.Ogre)));
        CutAt(Say("Esse projeto"), () => Show(Board.Kind.Source)); // "o código vai ser aberto": the real code, typed

        // ---- a ideia
        CutAt(Say("A ideia é parecer"), () =>
        {
            Hide();
            var m = Arena(0, (EKind.Bastion, 110, 90));
            keep = 0;
            pilotOn = false;
            m.ZoomMul = 2.3f;
        });
        On(Say("A ideia é parecer") + 2, () => { if (main != null) main.Focus = new Vector2(55, 140); }); // between the hero and the bastion
        CutAt(Say("Não tem sprites"), () => Show(Board.Kind.Boil));
        for (int i = 0; i < 5; i++) { int b = i; CutAt(Say("São cinco fases", i / 5f), () => { Hide(); Biome(b); }); }
        CutAt(Say("E são oito armas"), () => { Arena(0, (EKind.Bastion, -150, -60), (EKind.Spike, 150, -60), (EKind.Imp, 0, -180), (EKind.Shield, 260, -160)); dashEvery = 90; });
        On(Say("A foice"), () => heavyMode = true);

        // ---- o travamento
        CutAt(Say("O primeiro bug"), Crowd);
        On(Say("Com 30 monstros"), () => { InkBatch.Enabled = false; choppy = true; });
        CutAt(Say("Cada traço de tinta"), () => Show(Board.Kind.Calls));
        CutAt(Say("Quando um quadro demorava"), () => Show(Board.Kind.Cascade, 0));
        CutAt(Say("A solução foi juntar"), () => { Hide(); Crowd(); }); // batching back on: the same room runs smooth
        CutAt(Say("Em vez de mandar"), () => Show(Board.Kind.Strip));
        CutAt(Say("Em vez de mandar", 0.58f), () => Show(Board.Kind.Cascade, 1)); // "e limitamos quantas atualizações"
        CutAt(Say("O quadro caiu"), () => Show(Board.Kind.Numbers));

        // ---- o DOOM
        CutAt(Say("E isso não é novidade"), () => { Hide(); Crowd(); heavyMode = true; });
        CutAt(Say("O DOOM"), () => Show(Board.Kind.Doom, 0));
        CutAt(Say("O mapa dele", 0.4f), () => Show(Board.Kind.Doom, 1)); // "e o jogo dividia esse mapa em pedaços"
        CutAt(Say("E a lógica do DOOM"), () => Show(Board.Kind.Doom, 2));
        CutAt(Say("É a mesma ideia"), () => { Hide(); Arena(4, (EKind.Ogre, -200, -80), (EKind.Shield, 220, -60), (EKind.Orb, 0, -200)); dashEvery = 120; });

        // ---- o bug do revisor
        CutAt(Say("E claro"), () => Arena(2, (EKind.Imp, -200, -60), (EKind.Imp, 200, -60), (EKind.Spike, 0, -180), (EKind.Bastion, 0, -260)));
        CutAt(Say("E o mais interessante"), () => Show(Board.Kind.Review, 0));
        CutAt(Say("O flash de impacto"), () =>
        {
            Hide();
            var m = Arena(4, (EKind.Imp, -160, -60), (EKind.Imp, 160, -60), (EKind.Spike, 0, -170));
            heavyMode = true;
            foreach (var e in m.World.Enemies) e.Hp = Math.Min(e.Hp, 8);
            keep = 0; // this shot waits for the room to be clear, for the kill cam
            flashShot = true;
        });
        CutAt(Say("A correção foi simples"), () => Show(Board.Kind.Review, 1));

        // ---- fechamento
        CutAt(Say("No fim"), () => { Hide(); Crowd(); heavyMode = true; dashEvery = 100; });
        CutAt(Say("O código vai estar aberto"), () => Show(Board.Kind.Source, 1));
        CutAt(Say("Até o próximo"), () => { Hide(); ShowMenu(); });

        cues.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        wipe.Cuts = [.. cues.Where(c => c.Cut).Select(c => c.Frame)];
    }

    /// <summary>The narration with the second and frame each sentence starts at.</summary>
    void PrintScript()
    {
        foreach (var l in lines) GD.Print($"FALA {Stamp(l.From)}  q{l.From,5}  {l.Text}");
        GD.Print($"FALA fim {Stamp(end)}");
    }

    static string Stamp(int f) => $"{f / Fps / 60}:{f / (float)Fps % 60:00.0}";

    public override void _Process(double delta)
    {
        frame++;
        wipe.Frame = frame;
        if (boardsOnly)
        {
            int k = (frame - 1) / (7 * Fps);
            if (k >= AllBoards.Length) { GetTree().Quit(); return; }
            if ((frame - 1) % (7 * Fps) == 0) Show(AllBoards[k].Item1, AllBoards[k].Item2);
            return;
        }
        if (frame == 1) narration?.Play();
        Refill();
        foreach (var c in cues) if (c.Frame == frame) c.Do();
        Duck();
        if (main != null)
            main.ProcessMode = boardOn ? ProcessModeEnum.Disabled // hidden under an animation: frozen and silent
                : choppy && frame % 7 != 0 ? ProcessModeEnum.Disabled // ~8 fps, as it was
                : ProcessModeEnum.Inherit;

        if (flashShot && main != null && main.World.Enemies.Count == 0 && !main.KillCamOn && frame - lastCam > 90)
        {
            // the room is clear: the last blow gets the kill cam and the impact frame, then a new trio steps in
            lastCam = frame;
            main.React(new GameEvent(Ev.GatesOpen, main.World.Player.Pos, 0f, 0));
            main.Hud.Impact();
            for (int i = 0; i < 3; i++) Foe(EKind.Imp, -200 + i * 200, -150).Hp = 8;
        }
        if (frame >= end) GetTree().Quit();
    }
}
