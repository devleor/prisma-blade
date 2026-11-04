using Godot;
using Prisma.Sim;
using V = System.Numerics.Vector2;

namespace Prisma.Tests;

/// <summary>
/// Saves the new screen effects to test-results/impact_N.png so they can be looked at: a critical number with 会心
/// and the 破 stamp (0), ink on the screen edges, the low-health vignette and speed lines (1), and the ink-negative
/// impact frame (2). Needs a window and a GPU; run it muted:
/// godot --path . --audio-driver Dummy --resolution 1280x720 res://scenes/tests/ImpactShots.tscn
/// </summary>
public partial class ImpactShots : Node
{
    Main main = null!;
    int frame, impactAt;

    public override void _Ready()
    {
        Settings.Persist = false;
        main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(main);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://test-results"));
    }

    void Save(int n)
    {
        var path = ProjectSettings.GlobalizePath($"res://test-results/impact_{n}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print("saved " + path);
    }

    public override void _Process(double delta)
    {
        frame++;
        var w = main.World;
        w.Player.Invuln = 100000;
        switch (frame)
        {
            case 20:
                foreach (var (k, x) in new[] { (EKind.Ogre, -220f), (EKind.Bastion, 160f) })
                {
                    var e = w.Spawn(k, new V(x, -60f));
                    e.State = EState.Idle; e.AttackCd = 1e6f; e.T = -1e6f; e.Speed = 0f;
                }
                break;
            case 40:
                main.React(new GameEvent(Ev.Crit, new V(160f, -60f), 29f, 1, (int)EKind.Bastion));
                main.React(new GameEvent(Ev.Hit, new V(160f, -60f), 29f, 1, (int)EKind.Bastion));
                main.React(new GameEvent(Ev.GuardBreak, new V(-220f, -60f), 40f, (int)EKind.Ogre));
                // a few plain cuts across the floor, light and heavy, to see the marks they leave
                for (int i = 0; i < 6; i++)
                    main.React(new GameEvent(Ev.Hit, new V(-120f + i * 60f, 120f + (i % 2) * 40f), 9f, i % 3 == 0 ? 1 : 0, (int)EKind.Spike));
                break;
            case 46: Save(0); break;
            case 60:
                w.Player.Hp = 18;
                main.React(new GameEvent(Ev.PlayerHurt, w.Player.Pos, 10f));
                main.React(new GameEvent(Ev.Dash, w.Player.Pos));
                break;
            case 63: Save(1); break;
        }
        // the impact frame refuses a second one within a second of the guard break above, so wait for it
        Settings.ImpactFrames = true;
        if (frame >= 100 && impactAt == 0 && main.Hud.Impact()) impactAt = frame;
        if (impactAt > 0 && frame == impactAt + 2) { Save(2); GetTree().Quit(); }
    }
}
