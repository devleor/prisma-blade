using Godot;
using Prisma.Sim;
using V = System.Numerics.Vector2;

namespace Prisma.Tests;

/// <summary>
/// Walks through the five floors and saves one screenshot of each biome (floor, walls, weather, a few creatures and
/// the new swords) to test-results/biome_N.png. Needs a window and a GPU; run it muted:
/// godot --path . --audio-driver Dummy --resolution 1280x720 res://scenes/tests/BiomeShots.tscn
/// </summary>
public partial class BiomeShots : Node
{
    Main main = null!;
    int frame, floor = -1;
    const int Each = 70;

    public override void _Ready()
    {
        Settings.Persist = false;
        main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(main);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://test-results"));
    }

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        var w = main.World;
        w.Player.Invuln = 100000;
        if (frame % Each == 1)
        {
            floor++;
            if (floor >= Dungeon.Floors) { GetTree().Quit(); return; }
            w.GoToFloor(floor);
            var fight = w.Map.Rooms.Values.First(r => r.Kind == RoomKind.Fight);
            w.EnterRoom(fight, Prisma.Sim.Side.S);
            // in play these follow the RoomEnter event inside World.Step; here the room is changed by hand
            main.FloorArt.Redraw();
            main.Stains.Show(floor, fight.X, fight.Y);
            main.WeatherFx.Set(Weather.Of(w.Map.Biome));
            w.Phase = Phase.Fighting;
            EKind[] kinds = floor switch
            {
                0 => [EKind.Spike, EKind.Orb, EKind.Bastion],
                1 => [EKind.Brood, EKind.Spiderling, EKind.Spiderling, EKind.Imp],
                2 => [EKind.Ogre, EKind.Bomber, EKind.Imp],
                3 => [EKind.Shield, EKind.Brood, EKind.Spiderling],
                _ => [EKind.Ogre, EKind.Orb, EKind.Shield],
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                var e = w.Spawn(kinds[i], new V(-300f + i * 200f, -120f + (i % 2) * 90f));
                e.State = EState.Idle; e.AttackCd = 1e6f; e.T = -1e6f; e.Speed = 0f;
            }
            w.Player.Weapon = Weapon.Of(floor % 2 == 0 ? WKind.Lanca : WKind.Martelo);
            w.Player.Pos = new V(0f, 180f);
            main.AimOverride = new Vector2(0.3f, -1f);
        }
        if (frame % Each == Each - 1)
        {
            var path = ProjectSettings.GlobalizePath($"res://test-results/biome_{floor}.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print("saved " + path);
        }
    }
}
