using System.Numerics;
using Prisma.Sim;
using Xunit;

namespace Prisma.Sim.Tests;

/// <summary>The five floors and their biomes, the sword choice on the way down, and the spear and the hammer.</summary>
public class FloorTests
{
    static readonly Vector2 Up = new(0f, -1f);

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
        press.Light = false;
        all.AddRange(Run(w, thenFrames, press));
        return all;
    }

    [Fact]
    public void A_run_goes_through_five_floors_each_in_its_own_biome()
    {
        Assert.Equal(5, Dungeon.Floors);
        var biomes = Enumerable.Range(0, Dungeon.Floors).Select(Dungeon.BiomeOf).ToArray();
        Assert.Equal(Dungeon.Floors, biomes.Distinct().Count());
        Assert.Equal(Biome.Masmorra, biomes[0]);
        Assert.Equal(Biome.Floresta, Dungeon.Make(3, 1).Biome);
    }

    [Fact]
    public void The_first_floor_starts_without_offers()
    {
        var w = new World(seed: 2, waves: true);
        Assert.Empty(w.Altars);
    }

    [Fact]
    public void Down_the_portal_three_different_swords_you_do_not_hold_wait_in_the_first_room()
    {
        for (uint seed = 1; seed <= 15; seed++)
        {
            var w = new World(seed, waves: true);
            w.GoToFloor(1);
            Assert.Same(w.Map.Start, w.Room);
            Assert.Equal(3, w.Altars.Count);
            var kinds = w.Altars.Select(a => a.Offer).ToArray();
            Assert.All(kinds, o => Assert.Equal(OfferKind.Weapon, o.Kind));
            Assert.Equal(3, kinds.Select(o => o.Weapon).Distinct().Count());
            Assert.DoesNotContain(w.Player.Weapon.Kind, kinds.Select(o => o.Weapon));
        }
    }

    [Fact]
    public void Cutting_one_of_them_swaps_the_sword_and_the_others_crumble()
    {
        var w = new World(seed: 5, waves: true);
        w.GoToFloor(1);
        var pick = w.Altars[1].Offer.Weapon;
        w.Player.Pos = World.AltarSpots[1] + new Vector2(0f, 70f);
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 20);
        Assert.Contains(ev, x => x.Kind == Ev.Reward && x.I == 1);
        Assert.Equal(pick, w.Player.Weapon.Kind);
        Assert.Empty(w.Altars);
    }

    [Fact]
    public void The_spear_reaches_farther_than_the_longsword_but_only_straight_ahead()
    {
        var spear = Weapon.Of(WKind.Lanca);
        Assert.True(spear.Light[0].Reach > Weapon.Longa.Light[0].Reach + 50f);
        Assert.True(spear.Light[0].ArcEnd - spear.Light[0].ArcStart < 1f);

        var w = Arena();
        w.Player.Weapon = spear;
        var far = Dummy(w, EKind.Bastion, Up * 170f);
        var side = Dummy(w, EKind.Bastion, new Vector2(80f, -40f));
        int farHp = far.Hp, sideHp = side.Hp;
        Tap(w, new Inputs { Light = true, Aim = Up }, 25);
        Assert.True(far.Hp < farHp, "the thrust reaches 170 px");
        Assert.Equal(sideHp, side.Hp);
    }

    [Fact]
    public void The_hammer_breaks_a_golem_guard_the_longsword_only_dents()
    {
        EState After(Weapon weapon)
        {
            var w = Arena();
            w.Player.Weapon = weapon;
            var golem = Dummy(w, EKind.Bastion, Up * 70f);
            Run(w, 1, new Inputs { Light = true, Aim = Up });
            for (int i = 0; i < 16 && golem.State != EState.Stun; i++) w.Step(new Inputs { Aim = Up });
            return golem.State;
        }
        Assert.Equal(EState.Stun, After(Weapon.Of(WKind.Martelo)));
        Assert.NotEqual(EState.Stun, After(Weapon.Longa));
    }

    [Fact]
    public void Breaking_a_guard_is_announced_once_with_the_kind_so_it_can_be_heard()
    {
        var w = Arena();
        w.Player.Weapon = Weapon.Of(WKind.Martelo);
        var golem = Dummy(w, EKind.Bastion, Up * 70f);
        var seen = new List<GameEvent>();
        w.Step(new Inputs { Light = true, Aim = Up }); seen.AddRange(w.Events);
        for (int i = 0; i < 30; i++) { w.Step(new Inputs { Aim = Up }); seen.AddRange(w.Events); }
        var breaks = seen.Where(e => e.Kind == Ev.GuardBreak).ToList();
        Assert.Single(breaks);
        Assert.Equal((int)EKind.Bastion, breaks[0].I);
        Assert.Equal(EState.Stun, golem.State);
    }

    [Fact]
    public void The_blow_that_kills_names_its_target_and_breaks_no_guard()
    {
        var w = Arena();
        w.Player.Weapon = Weapon.Of(WKind.Martelo);
        var golem = Dummy(w, EKind.Bastion, Up * 70f);
        golem.Hp = 1;
        var seen = new List<GameEvent>();
        w.Step(new Inputs { Light = true, Aim = Up }); seen.AddRange(w.Events);
        for (int i = 0; i < 30; i++) { w.Step(new Inputs { Aim = Up }); seen.AddRange(w.Events); }
        Assert.False(golem.Alive);
        var hit = Assert.Single(seen, e => e.Kind == Ev.Hit);
        Assert.Equal((int)EKind.Bastion, hit.K); // the corpse is already gone from World.Enemies when the view hears it
        Assert.DoesNotContain(seen, e => e.Kind == Ev.GuardBreak);
    }
}
