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

    /// <summary>A world in a cleared room (gates open) with one altar holding the given offer.</summary>
    static World WithAltar(Offer offer)
    {
        var w = Arena();
        w.Phase = Phase.Explore;
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
    static float Weight(EKind[] wave) => wave.Sum(k => k switch
    {
        EKind.Bastion => 2.5f, EKind.Shield => 2f, EKind.Bomber => 1.5f, EKind.Imp => 0.5f, EKind.Monolith => 10f, _ => 1f,
    });

    // ---------------------------------------------------------------- the dungeon

    static World InFight(uint seed, RoomKind kind = RoomKind.Fight)
    {
        var w = new World(seed, waves: true);
        var room = w.Map.Rooms.Values.First(r => r.Kind == kind);
        w.EnterRoom(room, Side.S);
        return w;
    }

    /// <summary>Puts the room at its guardians with every enemy gone, so the next step clears it.</summary>
    static void GuardiansDown(World w)
    {
        w.Wave = w.Room.Waves.Length;
        w.GuardianUp = true;
        w.Phase = Phase.Fighting;
        w.Enemies.Clear();
    }

    [Fact]
    public void A_floor_is_the_same_for_a_seed_and_a_connected_tree_with_the_boss_at_its_deepest_dead_end()
    {
        for (uint seed = 1; seed <= 25; seed++)
            for (int floor = 0; floor < Dungeon.Floors; floor++)
            {
                var a = Dungeon.Make(seed, floor);
                var b = Dungeon.Make(seed, floor);
                Assert.Equal(Dungeon.RoomCount(floor), a.Rooms.Count);
                Assert.Equal(a.Rooms.Keys.OrderBy(k => k), b.Rooms.Keys.OrderBy(k => k));
                foreach (var (k, ra) in a.Rooms)
                {
                    var rb = b.Rooms[k];
                    Assert.Equal((ra.Kind, ra.Layout, ra.Seed, ra.Depth), (rb.Kind, rb.Layout, rb.Seed, rb.Depth));
                    Assert.Equal(ra.Guardians, rb.Guardians);
                    Assert.Equal(ra.Waves.Length, rb.Waves.Length);
                    for (int i = 0; i < ra.Waves.Length; i++) Assert.Equal(ra.Waves[i], rb.Waves[i]);
                }
                Assert.Equal(RoomKind.Start, a.Start.Kind);
                Assert.Equal(0, a.Start.Depth);
                // a tree: every room but the start has exactly one door toward the start
                Assert.All(a.Rooms.Values.Where(r => r != a.Start), r =>
                    Assert.Equal(1, a.Doors(r).Count(s => a.Neighbor(r, s)!.Depth == r.Depth - 1)));
                Assert.Equal(a.Rooms.Count - 1, a.Rooms.Values.Sum(r => a.Doors(r).Count()) / 2);
                Assert.Equal(RoomKind.Boss, a.Boss.Kind);
                Assert.Single(a.Rooms.Values, r => r.Kind == RoomKind.Boss);
                Assert.Equal(a.Rooms.Values.Max(r => r.Depth), a.Boss.Depth);
                Assert.Contains(a.Rooms.Values, r => r.Kind == RoomKind.Treasure);
                Assert.Equal([EKind.Monolith], a.Boss.Guardians);
            }
    }

    [Fact]
    public void Rooms_deeper_and_on_later_floors_get_heavier_and_the_first_rooms_hold_only_light_enemies()
    {
        for (uint seed = 1; seed <= 25; seed++)
        {
            var f0 = Dungeon.Make(seed, 0);
            var f2 = Dungeon.Make(seed, Dungeon.Floors - 1);
            foreach (var r in f0.Rooms.Values.Where(r => r.Depth == 1 && r.Kind == RoomKind.Fight))
                Assert.All(r.Waves.SelectMany(x => x), k => Assert.Contains(k, new[] { EKind.Spike, EKind.Imp, EKind.Orb }));
            float W(Dungeon d) => d.Rooms.Values.Where(r => r.Kind == RoomKind.Fight).Average(r => r.Waves.Sum(Weight));
            Assert.True(W(f2) > W(f0) + 3f, $"seed {seed}: {W(f0)} -> {W(f2)}");
            Assert.All(f0.Rooms.Values.Where(r => r.Kind != RoomKind.Boss), r => Assert.DoesNotContain(EKind.Monolith, r.Waves.SelectMany(x => x)));
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
    public void The_start_room_is_open_and_the_run_waits_there_until_you_take_a_door()
    {
        var w = new World(seed: 4, waves: true);
        Assert.Same(w.Map.Start, w.Room);
        Assert.True(w.GatesOpen);
        Run(w, 300);
        Assert.Empty(w.Enemies);
        Assert.Same(w.Map.Start, w.Room);
    }

    [Fact]
    public void Walking_into_an_open_doorway_enters_the_room_behind_it_at_the_facing_door()
    {
        var w = new World(seed: 6, waves: true);
        var side = w.Map.Doors(w.Room).First();
        var next = w.Map.Neighbor(w.Room, side)!;
        w.Player.Pos = Sides.Door(side) + Sides.Inward(side) * 40f;
        var walk = new Inputs { Move = Vector2.Normalize(-Sides.Inward(side)) };
        bool entered = false;
        for (int i = 0; i < 40 && !entered; i++) { w.Step(walk); entered = w.Events.Any(x => x.Kind == Ev.RoomEnter); }
        Assert.True(entered, "walking into the doorway changes room");
        Assert.Same(next, w.Room);
        Assert.True(next.Seen);
        var back = Sides.Opposite(side);
        Assert.True(Vector2.Distance(w.Player.Pos, Sides.Door(back)) < 120f, $"enters by the facing door, at {w.Player.Pos}");
    }

    [Fact]
    public void The_gates_stay_shut_while_the_room_still_fights()
    {
        var w = InFight(7);
        var room = w.Room;
        Run(w, 60);
        Assert.Equal(Phase.Fighting, w.Phase);
        Assert.False(w.GatesOpen);
        var side = w.Map.Doors(room).First();
        w.Player.Pos = Sides.Door(side) + Sides.Inward(side) * 20f;
        w.Player.Invuln = 100000;
        Run(w, 30, new Inputs { Move = Vector2.Normalize(-Sides.Inward(side)) });
        Assert.Same(room, w.Room);
    }

    [Fact]
    public void A_fight_room_runs_its_waves_then_its_guardian_whose_death_opens_the_gates()
    {
        var w = InFight(3);
        var room = w.Room;
        Run(w, 52);
        Assert.Equal(0, w.Wave);
        Assert.Equal(room.Waves[0].Length, w.Enemies.Count);
        for (int i = 0; i < room.Waves.Length; i++)
        {
            Assert.False(w.GuardianUp);
            foreach (var e in w.Enemies) e.Hp = 0;
            var ev = Run(w, 2);
            Assert.Contains(ev, x => x.Kind == Ev.WaveClear);
            Assert.Equal(Phase.Intermission, w.Phase);
            Run(w, 72);
        }
        Assert.True(w.GuardianUp);
        Assert.Equal(room.Guardians.Length, w.Enemies.Count);
        Assert.All(w.Enemies, e => Assert.True(e.Elite && e.Guardian));
        w.Player.Hp = 50;
        foreach (var e in w.Enemies) e.Hp = 0;
        var open = Run(w, 2);
        Assert.Contains(open, x => x.Kind == Ev.GatesOpen);
        Assert.True(w.GatesOpen);
        Assert.True(room.Cleared);
        Assert.Equal(50 + World.RoomClearHeal, w.Player.Hp);
        Assert.Equal(3, w.Altars.Count);
    }

    [Fact]
    public void An_elite_room_is_guarded_by_two_elites()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            var w = new World(seed, waves: true);
            var room = w.Map.Rooms.Values.FirstOrDefault(r => r.Kind == RoomKind.Elite);
            if (room == null) continue;
            Assert.Equal(2, room.Guardians.Length);
            w.EnterRoom(room, Side.S);
            w.Wave = room.Waves.Length - 1; w.Phase = Phase.Intermission; w.PhaseT = 1;
            Run(w, 1);
            Assert.Equal(2, w.Enemies.Count(e => e.Elite && e.Guardian));
            return;
        }
        Assert.Fail("no seed in 1..20 made an elite room");
    }

    [Fact]
    public void A_treasure_room_raises_altars_without_a_fight()
    {
        var w = InFight(2, RoomKind.Treasure);
        Assert.True(w.GatesOpen);
        Assert.True(w.Room.Cleared);
        Assert.Equal(3, w.Altars.Count);
        Run(w, 200);
        Assert.Empty(w.Enemies);
    }

    [Fact]
    public void Offers_left_on_their_altars_wait_for_you_and_a_taken_one_does_not_come_back()
    {
        var w = InFight(9);
        var room = w.Room;
        GuardiansDown(w);
        Run(w, 2);
        var offers = w.Altars.Select(a => a.Offer).ToArray();
        w.EnterRoom(w.Map.Start, Side.N);
        Assert.Empty(w.Altars);
        w.EnterRoom(room, Side.S);
        Assert.Equal(offers, w.Altars.Select(a => a.Offer).ToArray());
        w.TakeAltar(1);
        w.EnterRoom(w.Map.Start, Side.N);
        w.EnterRoom(room, Side.S);
        Assert.Empty(w.Altars);
    }

    [Fact]
    public void A_sweep_over_two_altars_takes_the_one_aimed_at()
    {
        var w = InFight(3);
        GuardiansDown(w);
        Run(w, 2);
        w.Pillars.Clear();
        var relic = w.Altars[1].Offer;
        // between altars 0 and 1, aiming a little toward 1: the first Longa cut (-1.4..+1.3 rad) covers both
        w.Player.Pos = new Vector2(-80f, -130f);
        float aim = -80f * MathF.PI / 180f;
        float Rel(Vector2 at) => Tune.Wrap(Tune.Angle(at - w.Player.Pos) - aim);
        Assert.InRange(Rel(World.AltarSpots[0]), Moves.Light[0].ArcStart, Moves.Light[0].ArcEnd);
        Assert.InRange(Rel(World.AltarSpots[1]), Moves.Light[0].ArcStart, Moves.Light[0].ArcEnd);
        Assert.True(MathF.Abs(Rel(World.AltarSpots[1])) < MathF.Abs(Rel(World.AltarSpots[0])));
        Assert.True(Vector2.Distance(w.Player.Pos, World.AltarSpots[0]) < Moves.Light[0].Reach + Altar.Radius - 12f); // margin for the step back at the windup
        Assert.True(Vector2.Distance(w.Player.Pos, World.AltarSpots[1]) < Moves.Light[0].Reach + Altar.Radius - 12f); // margin for the step back at the windup
        var ev = Tap(w, new Inputs { Light = true, Aim = new Vector2(MathF.Cos(aim), MathF.Sin(aim)) }, 20);
        Assert.Contains(ev, x => x.Kind == Ev.Reward && x.I == 1);
        Assert.Equal(1, w.Player.Has(relic.Relic));
    }

    [Fact]
    public void Only_your_own_kills_heal_an_enemy_bomb_killing_a_foe_does_not()
    {
        var w = Holding(WKind.Foice);
        w.Player.Relics[(int)RKind.Sede] = 1;
        w.Player.Hp = 50;
        var far = new Vector2(0f, -300f);
        var e = Dummy(w, EKind.Spike, far);
        e.Hp = 1;
        w.Bombs.Add(new Bomb { From = e.Pos + new Vector2(100f, 0f), To = e.Pos, T = 45f });
        Run(w, 3);
        Assert.Equal(EState.Dead, e.State);
        Assert.Equal(50, w.Player.Hp);
        var mine = Dummy(w, EKind.Spike, Up * 70f);
        mine.Hp = 1;
        Tap(w, new Inputs { Light = true, Aim = Up }, 25);
        Assert.Equal(EState.Dead, mine.State);
        Assert.Equal(50 + w.Player.Weapon.KillHeal + Relics.SedeHeal, w.Player.Hp);
    }

    [Fact]
    public void A_sword_taken_mid_chain_starts_from_its_first_cut()
    {
        var w = WithAltar(new Offer(OfferKind.Weapon, Weapon: WKind.Adagas));
        w.Player.Combo = 1; w.Player.NextCombo = 2; w.Player.ComboTimer = 10; // the Longa finisher comes next
        Tap(w, new Inputs { Light = true, Aim = Up }, 40); // the cut takes the sword
        Assert.Equal(WKind.Adagas, w.Player.Weapon.Kind);
        Assert.Equal(0, w.Player.NextCombo);
        Assert.Equal(0, w.Player.ComboTimer);
    }

    [Fact]
    public void A_cleared_room_stays_open_and_empty_when_you_come_back()
    {
        var w = InFight(8);
        var room = w.Room;
        GuardiansDown(w);
        Run(w, 2);
        w.EnterRoom(w.Map.Start, Side.N);
        w.EnterRoom(room, Side.S);
        Assert.True(w.GatesOpen);
        Run(w, 200);
        Assert.Empty(w.Enemies);
    }

    [Fact]
    public void The_boss_opens_a_portal_and_the_portal_leads_to_the_next_floor()
    {
        var w = new World(seed: 11, waves: true);
        w.EnterRoom(w.Map.Boss, Side.S);
        GuardiansDown(w);
        var ev = Run(w, 2);
        Assert.Contains(ev, x => x.Kind == Ev.Portal);
        Assert.True(w.PortalOpen);
        Assert.Empty(w.Altars);
        w.Player.Pos = World.PortalPos;
        ev = Run(w, 2);
        Assert.Contains(ev, x => x.Kind == Ev.NextFloor && x.I == 1);
        Assert.Equal(1, w.Map.Floor);
        Assert.Same(w.Map.Start, w.Room);
    }

    [Fact]
    public void The_boss_grows_tougher_floor_by_floor_and_the_last_one_wins_the_run()
    {
        var w = new World(seed: 12, waves: true);
        w.GoToFloor(Dungeon.Floors - 1);
        w.EnterRoom(w.Map.Boss, Side.S);
        w.Wave = w.Room.Waves.Length - 1; w.Phase = Phase.Intermission; w.PhaseT = 1;
        Run(w, 1);
        var boss = Assert.Single(w.Enemies, e => e.Boss);
        Assert.Equal((int)(700 * (1f + 0.35f * (Dungeon.Floors - 1))), boss.MaxHp);
        GuardiansDown(w);
        var ev = Run(w, 2);
        Assert.Equal(Phase.Won, w.Phase);
        Assert.Contains(ev, x => x.Kind == Ev.Victory);
    }

    // ---------------------------------------------------------------- altars

    [Fact]
    public void Altars_offer_a_sword_not_in_hand_and_two_different_relics()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            var w = InFight(seed);
            GuardiansDown(w);
            Run(w, 2);
            Assert.Equal(3, w.Altars.Count);
            var o = w.Altars.Select(a => a.Offer).ToArray();
            Assert.Equal(OfferKind.Weapon, o[0].Kind);
            Assert.NotEqual(WKind.Longa, o[0].Weapon);
            Assert.Equal(OfferKind.Relic, o[1].Kind);
            Assert.Equal(OfferKind.Relic, o[2].Kind);
            Assert.NotEqual(o[1].Relic, o[2].Relic);
            Assert.All(o, x => Assert.False(string.IsNullOrEmpty(x.Title) || string.IsNullOrEmpty(x.Blurb)));
            Assert.All(w.Altars, a => Assert.False(w.InsidePillar(a.Pos, Altar.Radius), $"seed {seed}: altar on a pillar"));
        }
    }

    [Fact]
    public void Badly_hurt_the_third_altar_holds_a_potion()
    {
        var w = InFight(2);
        GuardiansDown(w);
        w.Player.Hp = 20;
        Run(w, 2);
        Assert.Equal(20 + World.RoomClearHeal, w.Player.Hp);
        Assert.Equal(OfferKind.Heal, w.Altars[2].Offer.Kind);
        w.TakeAltar(2);
        Assert.Equal(20 + World.RoomClearHeal + Offer.HealAmount, w.Player.Hp);
    }

    [Fact]
    public void Cutting_an_altar_with_the_sword_takes_it_and_the_others_crumble()
    {
        var w = InFight(3);
        GuardiansDown(w);
        Run(w, 2);
        var offer = w.Altars[1].Offer;
        w.Pillars.Clear();
        w.Player.Pos = World.AltarSpots[1] + new Vector2(0f, 70f);
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 20);
        Assert.Contains(ev, x => x.Kind == Ev.Reward && x.I == 1);
        Assert.Empty(w.Altars);
        Assert.True(w.GatesOpen, "the gates stay open after the pick");
        Assert.Equal(1, w.Player.Has(offer.Relic));
    }

    [Fact]
    public void A_swing_while_the_room_still_fights_takes_nothing()
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
        Assert.Equal(hp - (int)MathF.Ceiling(Moves.Light[0].Damage * 1.3f - 0.001f), e.Hp);
    }

    [Fact]
    public void One_whetstone_adds_damage_even_to_the_daggers_small_cuts()
    {
        foreach (var cut in Weapon.Of(WKind.Adagas).Light)
            Assert.True((int)MathF.Ceiling(cut.Damage * 1.15f - 0.001f) > cut.Damage, $"a {cut.Damage} cut");
        var w = Arena();
        w.Player.Weapon = Weapon.Of(WKind.Adagas);
        w.Player.Relics[(int)RKind.Afiacao] = 1;
        var e = Dummy(w, EKind.Bastion, Up * 60f);
        int hp = e.Hp;
        Tap(w, new Inputs { Light = true, Aim = Up }, 25);
        Assert.Equal(hp - (Weapon.Of(WKind.Adagas).Light[0].Damage + 1), e.Hp);
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
