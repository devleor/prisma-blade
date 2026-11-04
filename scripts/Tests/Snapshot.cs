using Godot;
using Prisma.Sim;
using V = System.Numerics.Vector2;

namespace Prisma.Tests;

/// <summary>
/// Plays the real game for a few seconds with scripted input (walk, swing, dash) and saves screenshots to
/// test-results/ so the look can be checked without playing: two of the first room, one of the altars after it,
/// and a line-up of every creature (with an elite and a bomb in the air). Needs a window and a GPU.
/// Usage: godot --path . --resolution 1280x720 res://scenes/tests/Snapshot.tscn
/// </summary>
public partial class Snapshot : Node
{
    int frame;
    Main main = null!;
    static readonly int[] Shots = [200, 330, 420, 520, 600, 680];
    Menu? menu;

    public override void _Ready()
    {
        main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(main);
        var fight = main.World.Map.Rooms.Values.First(r => r.Kind == RoomKind.Fight);
        main.World.EnterRoom(fight, Prisma.Sim.Side.S); // straight into a fight (the start room is empty)
        main.FloorArt.Redraw();
        main.Stains.Show(0, fight.X, fight.Y);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://test-results"));
    }

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        if (frame == 530) { main.QueueFree(); AddChild(menu = new Menu { Live = false }); }
        if (frame == 570) menu!.Move(1);
        if (frame == 610) menu!.Confirm();
        if (frame == 640) menu!.Move(3);
        if (frame >= 530) { Shoot(); return; }
        var w = main.World;
        var p = w.Player;
        if (frame < 340)
        {
            p.Invuln = 1000; // the shots must not end on a defeat
            var enemy = w.Enemies.Count > 0 ? w.Enemies[0] : null;
            if (enemy != null)
            {
                var to = new Vector2(enemy.Pos.X - p.Pos.X, enemy.Pos.Y - p.Pos.Y);
                main.AimOverride = to;
                bool near = to.Length() < 140;
                Toggle(Controls.Right, !near && to.X > 30);
                Toggle(Controls.Left, !near && to.X < -30);
                Toggle(Controls.Down, !near && to.Y > 30);
                Toggle(Controls.Up, !near && to.Y < -30);
                Toggle(Controls.Light, near && frame % 14 == 0);
                Toggle(Controls.Dash, frame % 90 == 45);
            }
        }
        if (frame == 340)
        {
            foreach (var a in Controls.All) Input.ActionRelease(a);
            foreach (var e in w.Enemies) e.Hp = 0;
            w.Wave = w.Room.Waves.Length; w.GuardianUp = true; w.Phase = Phase.Fighting; // the guardian falls: gates open, altars rise
            p.Pos = new V(0f, 150f);
            main.AimOverride = new Vector2(0, -1);
        }
        if (frame == 430)
        {
            if (w.Altars.Count > 0) w.TakeAltar(0); // a new sword
            w.Phase = Phase.Fighting;
            p.Pos = new V(0f, 200f);
            p.Invuln = 100000;
            EKind[] kinds = [EKind.Spike, EKind.Orb, EKind.Bastion, EKind.Imp, EKind.Bomber, EKind.Shield];
            for (int i = 0; i < kinds.Length; i++)
            {
                var e = w.Spawn(kinds[i], new V(-450f + i * 180f, -40f));
                e.State = EState.Idle; e.AttackCd = 1e6f; e.T = -1e6f; e.Speed = 0f;
                if (i == 5) e.MakeElite();
                if (i == 2) e.Fracture = 2;
            }
            var boss = w.Spawn(EKind.Monolith, new V(0f, -220f));
            boss.State = EState.Idle; boss.T = -1e6f; boss.Speed = 0f;
            w.Bombs.Add(new Bomb { From = new V(300f, -40f), To = new V(150f, 180f), T = 20f, Flight = 1e6f });
        }
        if (frame > 430 && frame < 520 && frame % 25 == 0) { Input.ActionPress(Controls.Light); }
        else if (frame > 430) Input.ActionRelease(Controls.Light);
        Shoot();
    }

    void Shoot()
    {
        if (Array.IndexOf(Shots, frame) >= 0)
        {
            var path = ProjectSettings.GlobalizePath($"res://test-results/snapshot_{frame}.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print("saved " + path);
        }
        if (frame > Shots[^1]) GetTree().Quit();
    }

    static void Toggle(string action, bool on)
    {
        if (on) Input.ActionPress(action); else Input.ActionRelease(action);
    }
}
