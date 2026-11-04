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

    void Check(bool ok, string what)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok) failures++;
    }

    public override void _Ready()
    {
        Main.Peaceful = true;
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
                GD.Print(failures == 0 ? "PASSED" : $"FAILED ({failures})");
                GetTree().Quit(failures == 0 ? 0 : 1);
                break;
        }
    }
}
