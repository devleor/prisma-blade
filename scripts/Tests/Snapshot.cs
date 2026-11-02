using Godot;

namespace Prisma.Tests;

/// <summary>
/// Plays the real game for a few seconds with scripted input (walk, swing, dash) and saves screenshots to
/// test-results/ so the look can be checked without playing. Needs a window and a GPU.
/// Usage: godot --path . --resolution 1280x720 res://scenes/tests/Snapshot.tscn
/// </summary>
public partial class Snapshot : Node
{
    int frame;
    Main main = null!;
    static readonly int[] Shots = [200, 330, 420];

    public override void _Ready()
    {
        main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(main);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://test-results"));
    }

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        var enemy = main.World.Enemies.Count > 0 ? main.World.Enemies[0] : null;
        if (enemy != null)
        {
            var to = new Vector2(enemy.Pos.X - main.World.Player.Pos.X, enemy.Pos.Y - main.World.Player.Pos.Y);
            main.AimOverride = to;
            bool near = to.Length() < 140;
            Toggle(Controls.Right, !near && to.X > 30);
            Toggle(Controls.Left, !near && to.X < -30);
            Toggle(Controls.Down, !near && to.Y > 30);
            Toggle(Controls.Up, !near && to.Y < -30);
            Toggle(Controls.Light, near && frame % 14 == 0);
            Toggle(Controls.Dash, frame % 90 == 45);
        }
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
