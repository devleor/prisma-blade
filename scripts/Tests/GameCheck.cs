using Godot;
using Prisma.Sim;
using V = System.Numerics.Vector2;

namespace Prisma.Tests;

/// <summary>
/// Headless check of the real scene driven by real input actions: walking, a light swing landing on an enemy,
/// a dash, a charged heavy, audio and glow wired up. Prints PASSED and exits 0 when everything holds.
/// Usage: godot --headless --path . res://scenes/tests/GameCheck.tscn
/// </summary>
public partial class GameCheck : Node
{
    int failures, frame;
    Main main = null!;
    float startX;
    Enemy? target;
    Menu? menu;
    long frozenAt;
    float effectsBefore, shakeBefore;

    void Check(bool ok, string what)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok) failures++;
    }

    public override void _Ready()
    {
        Main.Peaceful = true;
        Settings.Persist = false; // never touch the player's settings file
        ProcessMode = ProcessModeEnum.Always; // keeps checking while the game is paused
        main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(main);
        main.AimOverride = new Vector2(0, -1);
        startX = main.World.Player.Pos.X;
        Input.ActionPress(Controls.Right);
    }

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        var w = main.World;
        var p = w.Player;
        switch (frame)
        {
            case 40:
                Input.ActionRelease(Controls.Right);
                Check(p.Pos.X > startX + 150f, $"holding move_right walks right ({startX:0} -> {p.Pos.X:0})");
                Check(Controls.All.All(a => InputMap.HasAction(a)), "all input actions are registered");
                break;
            case 60:
                target = w.Spawn(EKind.Bastion, p.Pos + new V(0, -70));
                target.State = EState.Idle;
                target.AttackCd = 1e6f;
                Input.ActionPress(Controls.Light);
                break;
            case 61: Input.ActionRelease(Controls.Light); break;
            case 90:
                Check(target!.Hp < target.MaxHp, $"attack_light swings and hits the hexagon in front ({target.Hp}/{target.MaxHp})");
                Check(main.EventCounts.GetValueOrDefault(Ev.Swing) == 1, "one swing event");
                Check(main.Fx.Count > 0, "the hit spawns sparks / numbers");
                Check(main.Stains.Count > 0, $"the hit flicks ink that stays on the floor ({main.Stains.Count} stains)");
                Check(main.Sfx.Loaded >= 18, $"sound effects load from assets/audio ({main.Sfx.Loaded})");
                Check(main.Sfx.Played >= 2, "swing and hit play sounds");
                Check(main.Sfx.CurrentMusic == "battle", "battle music is selected");
                Check(main.Env.Environment.GlowEnabled, "glow environment is on");
                Input.ActionPress(Controls.Dash);
                break;
            case 91: Input.ActionRelease(Controls.Dash); break;
            case 94: Check(p.State == PState.Dash, "dash starts on the dash action"); break;
            case 130: Input.ActionPress(Controls.Heavy); break;
            case 190:
                Check(p.State == PState.Charge && p.Charge == Tune.ChargeMax, "holding attack_heavy charges to full");
                Input.ActionRelease(Controls.Heavy);
                break;
            case 230:
                Check(main.EventCounts.GetValueOrDefault(Ev.ChargeFull) == 1, "full charge is signalled");
                Check(main.EventCounts.GetValueOrDefault(Ev.Swing) == 2, "releasing attack_heavy swings the heavy");
                Check(main.Hud.Size.X > 0, "HUD fills the screen");
                var seen = main.GetViewportRect().Size / main.Camera.Zoom;
                Check(main.Camera.Zoom.X > 0.5f && seen.X >= Tune.HalfW * 2 + 60 && seen.Y >= Tune.HalfH * 2 + 60,
                    $"the camera shows the whole arena and its walls ({seen.X:0}x{seen.Y:0} of {Tune.HalfW * 2:0}x{Tune.HalfH * 2:0})");
                Check(main.World.Pillars.Count == World.PillarLayout.Length, "the stone pillars are in the arena");
                Input.ActionPress(Controls.Special);
                break;
            case 231: Input.ActionRelease(Controls.Special); break;
            case 262:
                Check(main.EventCounts.GetValueOrDefault(Ev.Swing) == 3, "the special action spins");
                Check(main.World.Player.Hp + main.World.Player.Recover == main.World.Player.MaxHp, "its cost is shown as recoverable health");
                main.Hud.Round(1, "ONDA 1/2");
                Check(main.Hud.SplashLeft > 0 && main.Hud.SplashLabel == "ONDA 1/2", "the round splash starts");
                Check(Hud.Kanji.GetStringSize("第一波").X > 0 && Ink.Hand.GetStringSize("onda").X > 0, "the brush and handwriting fonts load");
                main.OpenPause();
                break;
            case 264:
                Check(GetTree().Paused && main.Pause != null, "the pause menu opens and freezes the game");
                Check(main.Pause?.Items.FirstOrDefault() == "CONTINUAR", "it offers to continue");
                frozenAt = main.World.Frame;
                break;
            case 274:
                Check(main.World.Frame == frozenAt, "nothing moves while paused");
                main.Pause!.Cancel(); // Esc in the pause menu goes back to the game
                break;
            case 277:
                Check(!GetTree().Paused && main.Pause == null, "Esc closes the pause menu");
                Check(main.World.Frame > frozenAt, "the game runs again");
                AddChild(menu = new Menu { Live = false, Size = new Vector2(1280, 720) });
                break;
            case 279:
                Check(menu!.Current == Menu.Page.Title && menu.Items[0] == "JOGAR", "the title menu starts on JOGAR");
                menu.Move(1);
                Check(menu.Selected == 1, "down moves the choice");
                menu.Confirm();
                Check(menu.Current == Menu.Page.Settings, "CONFIGURAÇÕES opens the settings page");
                effectsBefore = Settings.Effects; shakeBefore = Settings.Shake;
                menu.Move(2); menu.Adjust(-1); menu.Adjust(-1); menu.Adjust(-1);
                Check(Mathf.IsEqualApprox(Settings.Effects, Mathf.Snapped(effectsBefore - 0.3f, 0.05f)), $"left lowers the effects volume ({effectsBefore} -> {Settings.Effects})");
                Check(Mathf.IsEqualApprox(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(Settings.SfxBus)), Mathf.LinearToDb(Settings.Effects)), "the effects bus follows it");
                Check(main.Sfx.GetChildren().OfType<AudioStreamPlayer>().All(a => a.Bus == Settings.SfxBus || a.Bus == Settings.MusicBus), "every player sits on the music or effects bus");
                menu.Move(1); menu.Adjust(-1); menu.Adjust(-1);
                Check(Settings.Shake < shakeBefore, "screen shake can be turned down");
                break;
            case 330:
                Check(menu!.StainCount > 0, $"choosing flicks ink that stays on the page ({menu.StainCount})");
                menu.Cancel();
                Check(menu.Current == Menu.Page.Title, "Esc goes back from settings");
                Check(menu.Selected == 0, "back on the title page the choice is JOGAR again");
                menu.Confirm();
                Check(menu.LastAction == "play", "JOGAR starts the game");
                Settings.Effects = effectsBefore; Settings.Shake = shakeBefore; Settings.Apply();
                GD.Print(failures == 0 ? "PASSED" : $"FAILED ({failures})");
                GetTree().Quit(failures == 0 ? 0 : 1);
                break;
        }
    }
}
