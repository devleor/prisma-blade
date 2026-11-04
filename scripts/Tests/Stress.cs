using Godot;
using Prisma.Sim;
using V = System.Numerics.Vector2;

namespace Prisma.Tests;

/// <summary>
/// A crowded room for checking the frame time: a full horde plus the biggest normal wave, with the player cutting
/// through them (sparks, numbers, stains). Prints the average and worst frame time. Run muted, with a window:
/// godot --path . --audio-driver Dummy --resolution 1280x720 res://scenes/tests/Stress.tscn
/// </summary>
public partial class Stress : Node
{
    Main main = null!;
    int frame;
    double total, worst;
    int counted, gc0, gc2;
    long alloc0;

    public override void _Ready()
    {
        Settings.Persist = false;
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(main);
    }

    public override void _Process(double delta)
    {
        frame++;
        var w = main.World;
        w.Player.Invuln = 100000;
        if (frame == 5)
        {
            w.GoToFloor(1);
            var fight = w.Map.Rooms.Values.First(r => r.Kind == RoomKind.Fight);
            w.EnterRoom(fight, Prisma.Sim.Side.S);
            main.FloorArt.Redraw();
            var args = OS.GetCmdlineUserArgs();
            if (!args.Contains("noweather")) main.WeatherFx.Set(Weather.Of(w.Map.Biome)); // the rain is on, as in a real forest room
            if (args.Contains("nostains")) main.Stains.Visible = false;
            if (args.Contains("noenemies")) { GD.Print("enemies: 0"); return; }
            w.Phase = Phase.Fighting;
            var rng = new Rng(3);
            foreach (var k in Dungeon.HordeWave(12).Concat(Enumerable.Repeat(EKind.Spike, 10)).Concat(Enumerable.Repeat(EKind.Orb, 8)))
                w.Spawn(k, new V(rng.Range(-500f, 500f), rng.Range(-250f, 250f)));
            GD.Print($"enemies: {w.Enemies.Count}");
        }
        if (frame == 60) { Prof.On = true; gc0 = System.GC.CollectionCount(0); gc2 = System.GC.CollectionCount(2); alloc0 = System.GC.GetTotalAllocatedBytes(); }
        if (frame > 60 && frame <= 660)
        {
            total += delta; worst = System.Math.Max(worst, delta); counted++;
            // keep the fight going: refill what dies so the room stays crowded
            if (w.Enemies.Count < 30 && !OS.GetCmdlineUserArgs().Contains("noenemies")) w.Spawn(EKind.Imp, new V(0f, -200f));
            if (frame % 18 == 0) Input.ActionPress(Controls.Light);
            if (frame % 18 == 2) Input.ActionRelease(Controls.Light);
            if (frame % 150 == 0) Input.ActionPress(Controls.Special);
            if (frame % 150 == 2) Input.ActionRelease(Controls.Special);
        }
        if (frame == 661)
        {
            double secs = total;
            foreach (var kv in Prof.Ms.OrderByDescending(k => k.Value))
                GD.Print($"  {kv.Key,-14} {kv.Value / counted,7:F2} ms/frame  ({Prof.Calls[kv.Key] / (double)counted:F1} calls/frame)");
            GD.Print($"  physics steps/frame {Prof.Calls.GetValueOrDefault("sim") / (double)counted:F1} | GC gen0 {System.GC.CollectionCount(0) - gc0}, gen2 {System.GC.CollectionCount(2) - gc2} | alloc {(System.GC.GetTotalAllocatedBytes() - alloc0) / 1048576.0 / secs:F1} MB/s | cores {System.Environment.ProcessorCount} | draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} | primitives {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)}");
            GD.Print($"STRESS avg {total / counted * 1000:F2} ms ({counted / total:F0} fps), worst {worst * 1000:F2} ms, enemies {w.Enemies.Count}");
            GetTree().Quit();
        }
    }
}
