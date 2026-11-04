using System.Numerics;
using Prisma.Sim;
using Xunit;

namespace Prisma.Sim.Tests;

/// <summary>The big monsters: the Ogre (club slam and charge), the Brood (spider mother) and her spiderlings.</summary>
public class BigEnemyTests
{
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

    [Fact]
    public void The_ogre_and_the_brood_are_big_tough_and_guarded_and_spiderlings_are_tiny()
    {
        var ogre = Enemy.Make(EKind.Ogre, Vector2.Zero, 0);
        var brood = Enemy.Make(EKind.Brood, Vector2.Zero, 1);
        var golem = Enemy.Make(EKind.Bastion, Vector2.Zero, 2);
        var spider = Enemy.Make(EKind.Spiderling, Vector2.Zero, 3);
        Assert.True(ogre.Radius > golem.Radius && ogre.MaxHp > golem.MaxHp && ogre.MaxPoise > golem.MaxPoise);
        Assert.True(brood.Radius > 30f && brood.MaxPoise > 0f);
        Assert.True(spider.Radius < 10f && spider.MaxHp <= 10 && spider.Speed > 200f);
    }

    [Fact]
    public void The_ogre_slams_its_club_where_it_points_after_a_long_windup()
    {
        var w = Arena();
        var ogre = Dummy(w, EKind.Ogre, new Vector2(0f, -130f));
        ogre.AttackCd = 0f;
        var ev = Run(w, World.OgreWindup + 4);
        var slam = Assert.Single(ev, x => x.Kind == Ev.Slam);
        Assert.True(Vector2.Distance(slam.Pos, w.Player.Pos) < World.OgreSlamRadius, "the club lands on the player standing still");
        Assert.Equal(100 - World.OgreSlamDamage, w.Player.Hp);
    }

    [Fact]
    public void Stepping_out_of_the_circle_during_the_windup_dodges_the_ogre_slam()
    {
        var w = Arena();
        var ogre = Dummy(w, EKind.Ogre, new Vector2(0f, -130f));
        ogre.AttackCd = 0f;
        Run(w, 24); // it has committed its aim
        var ev = Run(w, World.OgreWindup + 4 - 24 + 10, new Inputs { Move = new Vector2(1f, 0f) });
        Assert.Contains(ev, x => x.Kind == Ev.Slam); // the club did come down...
        Assert.Equal(100, w.Player.Hp);              // ...where the player no longer was
    }

    [Fact]
    public void A_far_ogre_can_charge_across_the_room()
    {
        var w = Arena();
        var ogre = Dummy(w, EKind.Ogre, new Vector2(0f, -360f));
        ogre.AttackCd = 0f;
        ogre.State = EState.Telegraph; ogre.Mode = 1; ogre.T = 0f;
        Run(w, 34 + 48);
        Assert.Equal(100 - World.OgreChargeDamage, w.Player.Hp);
    }

    [Fact]
    public void Big_ones_shake_the_floor_as_they_walk_small_ones_do_not()
    {
        var w = Arena();
        var ogre = Dummy(w, EKind.Ogre, new Vector2(0f, -360f));
        var spike = Dummy(w, EKind.Spike, new Vector2(300f, -300f));
        var ev = Run(w, 120);
        Assert.Contains(ev, x => x.Kind == Ev.Stomp && x.I == (int)EKind.Ogre);
        Assert.DoesNotContain(ev, x => x.Kind == Ev.Stomp && x.I == (int)EKind.Spike);
    }

    [Fact]
    public void The_brood_lays_spiderlings_but_never_more_than_five_at_once()
    {
        var w = Arena();
        var brood = Dummy(w, EKind.Brood, new Vector2(0f, -300f));
        w.Player.Invuln = 100000;
        int hatched = 0;
        for (int i = 0; i < 6; i++)
        {
            brood.AttackCd = 0f;
            brood.State = EState.Idle;
            var ev = Run(w, 45);
            hatched += ev.Where(x => x.Kind == Ev.Hatch).Sum(x => x.I);
            Assert.True(w.Enemies.Count(e => e.Kind == EKind.Spiderling && e.Alive) <= World.BroodMax);
        }
        Assert.Equal(World.BroodMax, w.Enemies.Count(e => e.Kind == EKind.Spiderling && e.Alive));
        Assert.Equal(World.BroodMax, hatched);
    }

    [Fact]
    public void Come_close_and_the_brood_bites()
    {
        var w = Arena();
        var brood = Dummy(w, EKind.Brood, new Vector2(0f, -80f));
        brood.AttackCd = 0f;
        Run(w, 34);
        Assert.Equal(88, w.Player.Hp);
        Assert.Empty(w.Enemies.Where(e => e.Kind == EKind.Spiderling));
    }

    [Fact]
    public void A_spiderling_nips_for_a_little()
    {
        var w = Arena();
        var s = Dummy(w, EKind.Spiderling, new Vector2(0f, -50f));
        s.AttackCd = 0f;
        Run(w, 24);
        Assert.Equal(96, w.Player.Hp);
    }

    [Fact]
    public void Big_monsters_appear_from_the_middle_floors_and_guard_rooms()
    {
        var seen = new HashSet<EKind>();
        var guards = new HashSet<EKind>();
        for (uint seed = 1; seed <= 40; seed++)
            for (int f = 0; f < Dungeon.Floors; f++)
                foreach (var r in Dungeon.Make(seed, f).Rooms.Values)
                {
                    seen.UnionWith(r.Waves.Where((_, i) => i != r.Horde).SelectMany(x => x));
                    guards.UnionWith(r.Guardians);
                    if (f == 0) Assert.DoesNotContain(EKind.Ogre, r.Waves.SelectMany(x => x).Concat(r.Guardians));
                }
        Assert.Contains(EKind.Ogre, seen);
        Assert.Contains(EKind.Brood, seen);
        Assert.Contains(EKind.Ogre, guards);
        Assert.DoesNotContain(EKind.Spiderling, seen); // outside a horde they only hatch
    }
}
