using System.Numerics;
using Prisma.Sim;
using Xunit;

namespace Prisma.Sim.Tests;

/// <summary>The enemies added for the roguelike: Imp, Bomber (and its bombs), Shield.</summary>
public class EnemyTests
{
    static readonly Vector2 Up = new(0f, -1f);
    static readonly Vector2 Down = new(0f, 1f);

    static World Arena() => new(seed: 1, waves: false, pillars: false);

    static Enemy Dummy(World w, EKind kind, Vector2 offset)
    {
        var e = w.Spawn(kind, w.Player.Pos + offset);
        e.State = EState.Idle;
        e.AttackCd = 10_000f;
        e.T = -1e6f;
        return e;
    }

    static List<GameEvent> Run(World w, int frames, Inputs inp = default)
    {
        var all = new List<GameEvent>();
        for (int i = 0; i < frames; i++) { w.Step(inp); all.AddRange(w.Events); }
        return all;
    }

    static List<GameEvent> Tap(World w, Inputs press, int thenFrames)
    {
        var all = Run(w, 1, press);
        press.Light = press.Dash = press.Shatter = press.Special = false;
        all.AddRange(Run(w, thenFrames, press));
        return all;
    }

    // ---------------------------------------------------------------- imp

    [Fact]
    public void An_imp_is_small_fragile_and_hops_at_the_player_after_a_short_warning()
    {
        var w = Arena();
        var imp = Dummy(w, EKind.Imp, new Vector2(120f, 0f));
        Assert.True(imp.MaxHp < 20 && imp.Radius < 12f && imp.Speed > 200f);
        imp.AttackCd = 0f;
        var ev = Run(w, 40);
        var tele = ev.FindIndex(x => x.Kind == Ev.Telegraph && x.I == (int)EKind.Imp);
        Assert.True(tele >= 0);
        Assert.Equal(94, w.Player.Hp);
    }

    // ---------------------------------------------------------------- bomber

    [Fact]
    public void A_bomber_lobs_a_bomb_at_the_player_that_bursts_after_its_flight()
    {
        var w = Arena();
        var b = Dummy(w, EKind.Bomber, new Vector2(350f, 0f));
        b.AttackCd = 0f;
        var ev = Run(w, World.BomberWindup + 2);
        Assert.Contains(ev, x => x.Kind == Ev.Lob && x.I == 1);
        var bomb = Assert.Single(w.Bombs);
        Assert.True(Vector2.Distance(bomb.To, w.Player.Pos) < 30f, "aimed at where the player stands");
        Assert.Equal(100, w.Player.Hp); // nothing yet: it is still in the air
        ev = Run(w, (int)bomb.Flight + 2);
        Assert.Contains(ev, x => x.Kind == Ev.Explode && x.I == 0);
        Assert.Equal(100 - bomb.Damage, w.Player.Hp);
        Assert.Empty(w.Bombs);
    }

    [Fact]
    public void Stepping_out_of_the_marked_circle_avoids_the_bomb()
    {
        var w = Arena();
        w.Bombs.Add(new Bomb { From = w.Player.Pos + new Vector2(300f, 0f), To = w.Player.Pos });
        Run(w, 50, new Inputs { Move = new Vector2(-1f, 0f) });
        Assert.Equal(100, w.Player.Hp);
    }

    [Fact]
    public void Enemy_bombs_hurt_enemies_too_but_do_not_count_as_the_players_hits()
    {
        var w = Arena();
        var s = Dummy(w, EKind.Spike, new Vector2(300f, 0f));
        w.Bombs.Add(new Bomb { From = s.Pos + new Vector2(200f, 0f), To = s.Pos });
        Run(w, 50);
        Assert.Equal(40 - 14, s.Hp);
        Assert.Equal(0, w.Combo);
        Assert.Equal(0, s.Fracture);
    }

    [Fact]
    public void A_cut_bats_a_bomb_back_onto_the_nearest_enemy()
    {
        var w = Arena();
        var s = Dummy(w, EKind.Spike, Up * 320f);
        w.Bombs.Add(new Bomb { From = w.Player.Pos + Up * 200f, To = w.Player.Pos + Up * 20f, T = 30f });
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 12);
        Assert.Contains(ev, x => x.Kind == Ev.Deflect);
        var bomb = Assert.Single(w.Bombs);
        Assert.True(bomb.Friendly);
        ev = Run(w, 40);
        Assert.Contains(ev, x => x.Kind == Ev.Explode && x.I == 1);
        Assert.Equal(40 - 24, s.Hp);
        Assert.Equal(1, s.Fracture);
        Assert.Equal(100, w.Player.Hp);
    }

    [Fact]
    public void An_elite_bomber_throws_three_bombs()
    {
        var w = Arena();
        var b = Dummy(w, EKind.Bomber, new Vector2(350f, 0f));
        b.MakeElite();
        b.AttackCd = 0f;
        Run(w, World.BomberWindup + 2);
        Assert.Equal(3, w.Bombs.Count);
    }

    // ---------------------------------------------------------------- shield

    [Fact]
    public void The_shield_stops_light_cuts_from_the_front()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Shield, Up * 70f);
        e.Aim = Down; // facing the player
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 25);
        Assert.Contains(ev, x => x.Kind == Ev.Block);
        Assert.DoesNotContain(ev, x => x.Kind == Ev.Hit);
        Assert.Equal(e.MaxHp, e.Hp);
        Assert.Equal(0, w.Combo);
    }

    [Fact]
    public void A_cut_from_behind_lands()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Shield, Up * 70f);
        e.Aim = Up; // turned away
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 3 + Moves.Light[0].Windup);
        Assert.DoesNotContain(ev, x => x.Kind == Ev.Block);
        Assert.Equal(e.MaxHp - Moves.Light[0].Damage, e.Hp);
    }

    [Fact]
    public void The_heavy_breaks_through_the_shield()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Shield, Up * 80f);
        e.Aim = Down;
        Run(w, Tune.ChargeMax + 2, new Inputs { HeavyHeld = true, Aim = Up });
        var ev = Run(w, 30, new Inputs { Aim = Up });
        Assert.DoesNotContain(ev, x => x.Kind == Ev.Block);
        Assert.Equal(e.MaxHp - Moves.Heavy(Tune.ChargeMax).Damage, e.Hp);
    }

    [Fact]
    public void The_shield_turns_slowly_so_quick_footwork_gets_behind_it()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Shield, Up * 120f);
        e.Aim = Up; // half a turn away from the player
        Run(w, 15);
        Assert.False(World.Guards(e, w.Player.Pos), "not around yet after a quarter second");
        Run(w, 60);
        Assert.True(World.Guards(e, w.Player.Pos), "it does come around");
    }

    [Fact]
    public void A_shield_bash_hurts_and_leaves_the_shield_down()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Shield, Up * 90f);
        e.Aim = Down;
        e.AttackCd = 0f;
        var ev = Run(w, 50); // 26 warning + 10 bash + the hitstop of the hit
        Assert.Contains(ev, x => x.Kind == Ev.Telegraph && x.I == (int)EKind.Shield);
        Assert.Equal(90, w.Player.Hp);
        Assert.Equal(EState.Recover, e.State);
        Assert.False(World.Guards(e, w.Player.Pos));
    }

    // ---------------------------------------------------------------- in the run

    [Fact]
    public void The_new_enemies_show_up_in_runs()
    {
        var seen = new HashSet<EKind>();
        for (uint seed = 1; seed <= 30; seed++)
            for (int f = 0; f < Dungeon.Floors; f++)
                foreach (var r in Dungeon.Make(seed, f).Rooms.Values) { seen.UnionWith(r.Waves.SelectMany(x => x)); seen.UnionWith(r.Guardians); }
        Assert.Contains(EKind.Imp, seen);
        Assert.Contains(EKind.Bomber, seen);
        Assert.Contains(EKind.Shield, seen);
        Assert.All(Dungeon.Make(7, 0).Rooms.Values.Where(r => r.Depth <= 1), r => Assert.DoesNotContain(EKind.Shield, r.Waves.SelectMany(x => x)));
    }
}
