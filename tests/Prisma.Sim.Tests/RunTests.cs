using System.Numerics;
using Prisma.Sim;
using Xunit;

namespace Prisma.Sim.Tests;

/// <summary>The roguelike layer: the seeded run of rooms, elites, the altars between rooms, the swords and the relics.</summary>
public class RunTests
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
        press.Light = press.Dash = press.Shatter = press.Special = false;
        all.AddRange(Run(w, thenFrames, press));
        return all;
    }

    /// <summary>A world sitting at the reward step with one altar holding the given offer.</summary>
    static World WithAltar(Offer offer)
    {
        var w = Arena();
        w.Phase = Phase.Reward;
        w.Altars.Add(new Altar { Pos = w.Player.Pos + Up * 70f, Offer = offer });
        return w;
    }

    static World Holding(WKind kind)
    {
        var w = WithAltar(new Offer(OfferKind.Weapon, kind));
        w.TakeAltar(0);
        w.Phase = Phase.Fighting;
        return w;
    }

    /// <summary>A room's weight: what it would have cost from the generator's budget.</summary>
    static float Weight(Room r) => r.Enemies.Sum(k => k switch { EKind.Bastion => 2.5f, EKind.Monolith => 10f, _ => 1f });

    // ---------------------------------------------------------------- the run

    [Fact]
    public void A_run_is_the_same_for_a_seed_has_elite_rooms_and_ends_with_the_boss()
    {
        var a = World.MakeRun(11);
        var b = World.MakeRun(11);
        Assert.Equal(World.RunLength, a.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Kind, b[i].Kind);
            Assert.Equal(a[i].Enemies, b[i].Enemies);
        }
        Assert.Equal(RoomKind.Boss, a[^1].Kind);
        Assert.Equal([EKind.Monolith], a[^1].Enemies);
        foreach (int i in World.EliteRooms) Assert.Equal(RoomKind.Elite, a[i].Kind);
        Assert.Contains(Enumerable.Range(1, 30), s => !World.MakeRun((uint)s)[4].Enemies.SequenceEqual(a[4].Enemies));
    }

    [Fact]
    public void Rooms_get_heavier_deeper_and_the_first_rooms_hold_only_light_enemies()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            var run = World.MakeRun(seed);
            Assert.All(run[0].Enemies, k => Assert.Equal(EKind.Spike, k));
            Assert.DoesNotContain(EKind.Bastion, run[1].Enemies);
            Assert.True(Weight(run[5]) > Weight(run[0]) + 4f, $"seed {seed}: {Weight(run[0])} -> {Weight(run[5])}");
            Assert.All(run.Take(World.RunLength - 1), r => Assert.DoesNotContain(EKind.Monolith, r.Enemies));
        }
    }

    [Fact]
    public void An_elite_is_tougher_bigger_and_guarded_and_the_boss_cannot_be_made_elite()
    {
        var e = Enemy.Make(EKind.Spike, Vector2.Zero, 0);
        var plain = Enemy.Make(EKind.Spike, Vector2.Zero, 1);
        e.MakeElite();
        Assert.True(e.Elite);
        Assert.Equal((int)(plain.MaxHp * 2.2f), e.MaxHp);
        Assert.Equal(e.MaxHp, e.Hp);
        Assert.True(e.Radius > plain.Radius * 1.25f);
        Assert.True(e.MaxPoise >= 40f, "a spike has no guard, its elite does");
        var boss = Enemy.Make(EKind.Monolith, Vector2.Zero, 2);
        boss.MakeElite();
        Assert.False(boss.Elite);
        Assert.Equal(700, boss.MaxHp);
    }

    [Fact]
    public void An_elite_room_opens_with_exactly_one_elite()
    {
        var w = new World(seed: 5, waves: true);
        w.Wave = World.EliteRooms[0] - 1;
        w.Phase = Phase.Intermission;
        w.PhaseT = 1;
        Run(w, 1);
        Assert.Equal(World.EliteRooms[0], w.Wave);
        Assert.Equal(w.Rooms[w.Wave].Enemies.Length, w.Enemies.Count);
        Assert.Single(w.Enemies, e => e.Elite);
        Assert.True(w.Enemies[0].Elite, "the room's first enemy leads it");
    }

    // ---------------------------------------------------------------- altars

    [Fact]
    public void Altars_offer_a_sword_not_in_hand_and_two_different_relics()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            var w = new World(seed, waves: true);
            Run(w, 80);
            foreach (var e in w.Enemies) e.Hp = 0;
            Run(w, 2);
            Assert.Equal(3, w.Altars.Count);
            var o = w.Altars.Select(a => a.Offer).ToArray();
            Assert.Equal(OfferKind.Weapon, o[0].Kind);
            Assert.NotEqual(WKind.Longa, o[0].Weapon);
            Assert.Equal(OfferKind.Relic, o[1].Kind);
            Assert.Equal(OfferKind.Relic, o[2].Kind);
            Assert.NotEqual(o[1].Relic, o[2].Relic);
            Assert.All(o, x => Assert.False(string.IsNullOrEmpty(x.Title) || string.IsNullOrEmpty(x.Blurb)));
        }
    }

    [Fact]
    public void Badly_hurt_the_third_altar_holds_a_potion()
    {
        var w = new World(seed: 2, waves: true);
        Run(w, 80);
        w.Player.Hp = 20;
        foreach (var e in w.Enemies) e.Hp = 0;
        Run(w, 2);
        Assert.Equal(45, w.Player.Hp); // the room-clear heal
        Assert.Equal(OfferKind.Heal, w.Altars[2].Offer.Kind);
        w.TakeAltar(2);
        Assert.Equal(45 + Offer.HealAmount, w.Player.Hp);
    }

    [Fact]
    public void Cutting_an_altar_with_the_sword_takes_it_and_the_others_crumble()
    {
        var w = new World(seed: 3, waves: true);
        Run(w, 80);
        foreach (var e in w.Enemies) e.Hp = 0;
        Run(w, 2);
        var offer = w.Altars[1].Offer;
        w.Player.Pos = World.AltarSpots[1] + new Vector2(0f, 70f);
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 20);
        Assert.Contains(ev, x => x.Kind == Ev.Reward && x.I == 1);
        Assert.Empty(w.Altars);
        Assert.Equal(Phase.Intermission, w.Phase);
        Assert.Equal(1, w.Player.Has(offer.Relic));
    }

    [Fact]
    public void A_swing_outside_the_reward_step_takes_nothing()
    {
        var w = WithAltar(new Offer(OfferKind.Relic, Relic: RKind.Sede));
        w.Phase = Phase.Fighting;
        Tap(w, new Inputs { Light = true, Aim = Up }, 20);
        Assert.Single(w.Altars);
        Assert.Equal(0, w.Player.Has(RKind.Sede));
    }

    // ---------------------------------------------------------------- swords

    [Fact]
    public void The_twin_daggers_chain_five_quick_cuts()
    {
        var w = Holding(WKind.Adagas);
        Assert.Equal(WKind.Adagas, w.Player.Weapon.Kind);
        var swings = new List<int>();
        for (int i = 0; i < 5; i++)
            swings.AddRange(Tap(w, new Inputs { Light = true, Aim = Up }, 12).Where(x => x.Kind == Ev.Swing).Select(x => x.I));
        Assert.Equal([0, 1, 2, 3, 4], swings);
        Assert.Equal(14, w.Player.DashCooldown);
    }

    [Fact]
    public void The_greatsword_heavy_charges_sooner_hits_harder_and_reaches_farther()
    {
        var m = Weapon.Montante;
        Assert.True(m.FullCharge(40) && !Weapon.Longa.FullCharge(40));
        Assert.True(m.Heavy(40).Damage > Weapon.Longa.Heavy(Tune.ChargeMax).Damage * 1.3f);
        Assert.True(m.Heavy(40).Reach > Weapon.Longa.Heavy(Tune.ChargeMax).Reach + 20f);

        var w = Holding(WKind.Montante);
        var e = Dummy(w, EKind.Monolith, Up * 150f);
        int hp = e.Hp;
        var ev = Run(w, 45, new Inputs { HeavyHeld = true, Aim = Up });
        Assert.Contains(ev, x => x.Kind == Ev.ChargeFull);
        Run(w, 30, new Inputs { Aim = Up });
        Assert.Equal(hp - m.Heavy(40).Damage, e.Hp);
    }

    [Fact]
    public void The_greatsword_makes_stone_hurt_twice_as_much()
    {
        int Impact(WKind kind)
        {
            var w = Holding(kind);
            var e = Dummy(w, EKind.Spike, new Vector2(Tune.HalfW - 50f, 0f) - w.Player.Pos);
            e.State = EState.Stun; e.T = 0f; e.StunLen = 100f;
            e.Vel = new Vector2(700f, 0f);
            Run(w, 15);
            return e.MaxHp - e.Hp;
        }
        int plain = Impact(WKind.Longa), heavy = Impact(WKind.Montante);
        Assert.True(plain > 0);
        Assert.Equal(plain * 2, heavy);
    }

    [Fact]
    public void Every_hit_of_the_runic_blade_fractures()
    {
        var w = Holding(WKind.Runica);
        var e = Dummy(w, EKind.Bastion, Up * 70f);
        Tap(w, new Inputs { Light = true, Aim = Up }, 25);
        Assert.Equal(1, e.Fracture); // the Longa's first cut does not fracture

        var l = Holding(WKind.Longa);
        var f = Dummy(l, EKind.Bastion, Up * 70f);
        Tap(l, new Inputs { Light = true, Aim = Up }, 25);
        Assert.Equal(0, f.Fracture);
    }

    [Fact]
    public void The_katana_widens_the_perfect_window_and_its_echo_doubles_the_next_hit()
    {
        int late = Tune.PerfectFrames + 2; // past the Longa's window, inside the katana's

        var l = Holding(WKind.Longa);
        var ls = Dummy(l, EKind.Spike, new Vector2(100f, 0f));
        Tap(l, new Inputs { Dash = true, Move = Up }, late);
        l.HurtPlayer(12, ls.Pos, ls);
        Assert.Equal(0, l.SlowMo);

        var w = Holding(WKind.Katana);
        var s = Dummy(w, EKind.Spike, new Vector2(100f, 0f));
        Tap(w, new Inputs { Dash = true, Move = Up }, late);
        w.HurtPlayer(12, s.Pos, s);
        Assert.True(w.SlowMo > 0);
        Assert.True(w.Player.EchoReady);

        Run(w, 30);
        var e = Dummy(w, EKind.Bastion, Up * 70f);
        int hp = e.Hp;
        Tap(w, new Inputs { Light = true, Aim = Up }, 25);
        Assert.Equal(hp - 2 * Weapon.Katana.Light[0].Damage, e.Hp);
        Assert.False(w.Player.EchoReady);
    }

    [Fact]
    public void The_scythe_and_the_chalice_heal_on_kills()
    {
        var w = Holding(WKind.Foice);
        w.Player.Hp = 50;
        var a = Dummy(w, EKind.Spike, Up * 80f);
        a.Hp = 1;
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 30);
        Assert.Equal(53, w.Player.Hp);
        Assert.Contains(ev, x => x.Kind == Ev.Heal && x.A == 3f);

        w.Player.Relics[(int)RKind.Sede] = 1;
        var b = Dummy(w, EKind.Spike, Up * 80f);
        b.Hp = 1;
        Run(w, 40);
        Tap(w, new Inputs { Light = true, Aim = Up }, 30);
        Assert.Equal(53 + 3 + Relics.SedeHeal, w.Player.Hp);
    }

    // ---------------------------------------------------------------- relics

    [Fact]
    public void The_stone_heart_raises_max_health_and_fills_it()
    {
        var w = WithAltar(new Offer(OfferKind.Relic, Relic: RKind.Coracao));
        w.TakeAltar(0);
        Assert.Equal(120, w.Player.MaxHp);
        Assert.Equal(120, w.Player.Hp);
        Assert.Equal(1, w.Player.Has(RKind.Coracao));
    }

    [Fact]
    public void Whetstones_stack_damage()
    {
        var w = Arena();
        w.Player.Relics[(int)RKind.Afiacao] = 2;
        var e = Dummy(w, EKind.Bastion, Up * 70f);
        int hp = e.Hp;
        Tap(w, new Inputs { Light = true, Aim = Up }, 25);
        Assert.Equal(hp - (int)MathF.Round(Moves.Light[0].Damage * 1.3f), e.Hp);
    }

    [Fact]
    public void Wind_boots_shorten_the_dash_cooldown()
    {
        var w = Arena();
        w.Player.Relics[(int)RKind.PeLeve] = 1;
        Tap(w, new Inputs { Dash = true, Move = Up }, 0);
        Assert.Equal(14, w.Player.DashCd); // 22 x 0.65
    }

    [Fact]
    public void Ember_burns_for_two_seconds_after_a_hit()
    {
        var w = Arena();
        w.Player.Relics[(int)RKind.Brasa] = 1;
        var e = Dummy(w, EKind.Bastion, Up * 70f);
        Tap(w, new Inputs { Light = true, Aim = Up }, 10);
        int afterHit = e.Hp;
        var ev = Run(w, Relics.BurnFrames + 10);
        Assert.Equal(4, ev.Count(x => x.Kind == Ev.Burn));
        Assert.Equal(afterHit - 4 * Relics.BurnDamage, e.Hp);
        Run(w, 90);
        Assert.Equal(afterHit - 4 * Relics.BurnDamage, e.Hp);
    }

    [Fact]
    public void The_prism_eye_adds_half_again_to_focus_gained()
    {
        var w = Arena();
        w.Player.Relics[(int)RKind.Foco] = 1;
        Dummy(w, EKind.Bastion, Up * 70f);
        Tap(w, new Inputs { Light = true, Aim = Up }, 25);
        Assert.Equal(6f, w.Player.Focus, 3);
    }

    [Fact]
    public void Crystal_spikes_hurt_everything_near_a_perfect_dodge()
    {
        var w = Arena();
        w.Player.Relics[(int)RKind.Espinhos] = 1;
        var s = Dummy(w, EKind.Spike, new Vector2(100f, 0f));
        Tap(w, new Inputs { Dash = true, Move = Up }, 2);
        w.HurtPlayer(12, s.Pos, s);
        Assert.Equal(40 - Relics.EspinhosDamage, s.Hp);
    }
}
