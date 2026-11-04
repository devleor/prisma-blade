using System.Numerics;
using Prisma.Sim;
using Xunit;

namespace Prisma.Sim.Tests;

public class CombatTests
{
    static readonly Vector2 Up = new(0f, -1f);

    /// <summary>An open world (no waves, no pillars); enemies placed by the test start awake and won't attack soon.</summary>
    static World Arena() => new(seed: 1, waves: false, pillars: false);

    static Enemy Dummy(World w, EKind kind, Vector2 offset)
    {
        var e = w.Spawn(kind, w.Player.Pos + offset);
        e.State = EState.Idle;
        e.AttackCd = 10_000f;
        e.T = -1e6f; // the boss picks patterns from its idle time, not its cooldown
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

    [Fact]
    public void Movement_has_inertia_but_reaches_full_speed()
    {
        var w = Arena();
        var inp = new Inputs { Move = new Vector2(1f, 0f) };
        Run(w, 1, inp);
        Assert.InRange(w.Player.Vel.X, 1f, Tune.MoveSpeed * 0.5f);
        Run(w, 40, inp);
        Assert.InRange(w.Player.Vel.X, Tune.MoveSpeed * 0.95f, Tune.MoveSpeed + 1f);
        Run(w, 3);
        Assert.True(w.Player.Vel.X > 0f, "still sliding a little after letting go");
        Run(w, 40);
        Assert.True(w.Player.Vel.Length() < 5f);
    }

    [Fact]
    public void A_light_swing_hits_an_enemy_in_front_once()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Bastion, Up * 70f);
        int hp = e.Hp;
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 30);
        Assert.Equal(1, ev.Count(x => x.Kind == Ev.Hit));
        Assert.Equal(hp - Moves.Light[0].Damage, e.Hp);
    }

    [Fact]
    public void An_enemy_behind_is_outside_the_first_swing()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Bastion, -Up * 80f);
        int hp = e.Hp;
        Tap(w, new Inputs { Light = true, Aim = Up }, 30);
        Assert.Equal(hp, e.Hp);
    }

    [Fact]
    public void Three_presses_in_rhythm_chain_the_three_light_swings()
    {
        var w = Arena();
        var swings = new List<int>();
        for (int i = 0; i < 3; i++)
            swings.AddRange(Tap(w, new Inputs { Light = true, Aim = Up }, 16).Where(x => x.Kind == Ev.Swing).Select(x => x.I));
        Run(w, 40);
        Assert.Equal([0, 1, 2], swings);
    }

    [Fact]
    public void The_chain_resets_after_a_pause()
    {
        var w = Arena();
        Tap(w, new Inputs { Light = true, Aim = Up }, 25 + Tune.ComboWindow + 5);
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 10);
        Assert.Equal(0, ev.Single(x => x.Kind == Ev.Swing).I);
    }

    [Fact]
    public void A_press_during_the_swing_is_buffered_and_dash_cancels_recovery()
    {
        var w = Arena();
        var s = Moves.Light[0];
        Tap(w, new Inputs { Light = true, Aim = Up }, s.Windup + 1); // inside the active frames
        Assert.True(w.Player.BladeActive);
        Tap(w, new Inputs { Dash = true, Move = new Vector2(1f, 0f) }, 0);
        Assert.Equal(PState.Attack, w.Player.State); // held, not acted on mid-cut
        Assert.Equal(Buffered.Dash, w.Player.Buffer);
        int t = w.Player.T;
        while (w.Player.State == PState.Attack && w.Player.T < 100) w.Step(new Inputs());
        Assert.Equal(PState.Dash, w.Player.State);
        Assert.True(t < s.Windup + s.Active + Tune.CancelDelay, "the press came before cancels opened");
    }

    [Fact]
    public void A_buffered_press_expires()
    {
        var w = Arena();
        Tap(w, new Inputs { Light = true, Aim = Up }, 0);
        w.Player.State = PState.Hurt; w.Player.T = -100; // stuck long enough for the buffer to lapse
        Run(w, Tune.BufferFrames + 1);
        Assert.Equal(Buffered.None, w.Player.Buffer);
    }

    [Fact]
    public void A_press_after_the_swing_ends_but_inside_the_window_continues_the_chain()
    {
        var w = Arena();
        Tap(w, new Inputs { Light = true, Aim = Up }, Moves.Light[0].Total + 2);
        Assert.Equal(PState.Free, w.Player.State);
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 10);
        Assert.Equal(1, ev.Single(x => x.Kind == Ev.Swing).I);
    }

    [Fact]
    public void Dash_covers_ground_fast_and_ignores_a_late_hit()
    {
        var w = Arena();
        float x0 = w.Player.Pos.X;
        Tap(w, new Inputs { Dash = true, Move = new Vector2(1f, 0f) }, Tune.PerfectFrames + 1);
        Assert.True(w.Player.Pos.X - x0 > 120f);
        w.HurtPlayer(20, w.Player.Pos + Up, null); // past the perfect window, still invulnerable
        Assert.Equal(100, w.Player.Hp);
        Assert.Equal(0, w.SlowMo);
    }

    [Fact]
    public void Getting_hit_without_dodging_hurts()
    {
        var w = Arena();
        var ev = new List<GameEvent>();
        w.HurtPlayer(20, w.Player.Pos + Up, null);
        Assert.Equal(80, w.Player.Hp);
        Assert.Equal(PState.Hurt, w.Player.State);
        w.HurtPlayer(20, w.Player.Pos + Up, null); // hurt invulnerability
        Assert.Equal(80, w.Player.Hp);
    }

    [Fact]
    public void A_perfect_dodge_slows_time_fractures_nearby_enemies_and_gives_focus()
    {
        var w = Arena();
        var spike = Dummy(w, EKind.Spike, new Vector2(100f, 0f));
        var far = Dummy(w, EKind.Spike, new Vector2(-600f, 0f));
        Tap(w, new Inputs { Dash = true, Move = Up }, 2);
        w.HurtPlayer(12, spike.Pos, spike);
        Assert.Equal(100, w.Player.Hp);
        Assert.True(w.SlowMo > 0);
        Assert.Equal(2, spike.Fracture); // the attacker plus the area mark
        Assert.Equal(0, far.Fracture);
        Assert.Equal(25f, w.Player.Focus);
    }

    [Fact]
    public void Slow_mo_slows_enemies_but_not_the_player()
    {
        (float enemy, float player) Moved(bool slow)
        {
            var w = Arena();
            var e = Dummy(w, EKind.Spike, new Vector2(300f, 0f));
            e.Vel = new Vector2(0f, 300f);
            e.State = EState.Stun; e.StunLen = 1e6f; // coasting: only time scale changes its motion
            if (slow) w.SlowMo = 30;
            var inp = new Inputs { Move = new Vector2(-1f, 0f) };
            float ey = e.Pos.Y, px = w.Player.Pos.X;
            Run(w, 1, inp); // one frame: friction has not compounded yet, so the ratio is the time scale
            return (e.Pos.Y - ey, w.Player.Pos.X - px);
        }
        var normal = Moved(false);
        var slowed = Moved(true);
        Assert.InRange(slowed.enemy / normal.enemy, 0.2f, 0.45f);
        Assert.Equal(normal.player, slowed.player, 3);
    }

    [Fact]
    public void A_full_charge_heavy_hits_harder_and_fractures_twice()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Monolith, Up * 120f);
        int hp = e.Hp;
        var hold = new Inputs { HeavyHeld = true, Aim = Up };
        Run(w, Tune.ChargeMax + 5, hold);
        Assert.Equal(PState.Charge, w.Player.State);
        var ev = Run(w, 30, new Inputs { Aim = Up });
        Assert.Contains(ev, x => x.Kind == Ev.Hit && x.I == 1);
        Assert.Equal(hp - Moves.Heavy(Tune.ChargeMax).Damage, e.Hp);
        Assert.Equal(2, e.Fracture);
        Assert.True(Moves.Heavy(Tune.ChargeMax).Damage > Moves.Heavy(0).Damage + 30);
    }

    [Fact]
    public void Shatter_bursts_fractured_enemies_and_the_chain_spreads_along_a_line()
    {
        var w = Arena();
        var a = Dummy(w, EKind.Bastion, new Vector2(200f, 0f));
        var b = Dummy(w, EKind.Bastion, new Vector2(340f, 0f));
        var c = Dummy(w, EKind.Bastion, new Vector2(480f, 0f));
        var lone = Dummy(w, EKind.Bastion, new Vector2(-500f, 0f));
        a.Fracture = 2;
        w.Player.Focus = Tune.FocusMax;
        var ev = Tap(w, new Inputs { Shatter = true }, 40);
        var depths = ev.Where(x => x.Kind == Ev.Shatter).Select(x => x.I).ToList();
        Assert.Equal([0, 1, 2], depths);
        Assert.True(a.Hp < 130 - 40 && b.Hp < 130 && c.Hp < 130);
        Assert.Equal(130, lone.Hp);
        Assert.Equal(Tune.FocusMax - Tune.ShatterCost, w.Player.Focus);
    }

    [Fact]
    public void Shatter_needs_focus()
    {
        var w = Arena();
        var a = Dummy(w, EKind.Bastion, new Vector2(100f, 0f));
        a.Fracture = 1;
        var ev = Tap(w, new Inputs { Shatter = true }, 20);
        Assert.DoesNotContain(ev, x => x.Kind == Ev.ShatterCast);
    }

    [Fact]
    public void Armour_ignores_light_flinch_until_poise_breaks()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Bastion, Up * 70f);
        Tap(w, new Inputs { Light = true, Aim = Up }, 8);
        Assert.NotEqual(EState.Stun, e.State);
        for (int i = 0; i < 4 && e.State != EState.Stun; i++)
        {
            e.Pos = w.Player.Pos + Up * 70f;
            Tap(w, new Inputs { Light = true, Aim = Up }, 8);
            Run(w, 12);
        }
        Assert.Equal(EState.Stun, e.State);
    }

    [Fact]
    public void Hitstop_freezes_the_world_for_a_few_frames()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Spike, Up * 60f);
        var inp = new Inputs { Light = true, Aim = Up };
        w.Step(inp);
        inp.Light = false;
        for (int i = 0; i < 30 && w.Events.All(x => x.Kind != Ev.Hit); i++) w.Step(inp);
        Assert.True(w.Hitstop >= Moves.Light[0].Hitstop, "the hit starts hitstop");
        var at = e.Pos;
        int playerT = w.Player.T, frame = w.Frame;
        w.Step(inp);
        Assert.Equal(frame + 1, w.Frame);
        Assert.Equal(at, e.Pos);
        Assert.Equal(playerT, w.Player.T);
    }

    [Fact]
    public void The_sword_reflects_enemy_shots()
    {
        var w = Arena();
        var shooter = Dummy(w, EKind.Orb, Up * 400f);
        shooter.Speed = 0f; // a still target, so the test is about the reflection, not about aiming
        w.Projs.Add(new Proj { Pos = w.Player.Pos + Up * 60f, Vel = -Up * 100f, Life = 3f, Damage = 8 });
        var ev = Tap(w, new Inputs { Light = true, Aim = Up }, 12);
        Assert.Contains(ev, x => x.Kind == Ev.Deflect);
        Assert.True(w.Projs.Count == 0 || w.Projs[0].Friendly);
        Run(w, 60);
        Assert.True(shooter.Hp < 30, "the reflected shot flies back and hurts its owner");
    }

    [Fact]
    public void A_spike_telegraphs_then_charges_into_the_player()
    {
        var w = Arena();
        var s = Dummy(w, EKind.Spike, new Vector2(250f, 0f));
        s.AttackCd = 0f;
        var ev = Run(w, 90);
        Assert.Contains(ev, x => x.Kind == Ev.Telegraph);
        Assert.True(w.Player.Hp < 100, "the charge connects with a player standing still");
    }

    [Fact]
    public void The_boss_cycles_charges_bursts_and_summons()
    {
        var w = Arena();
        var boss = Dummy(w, EKind.Monolith, Up * 300f);
        boss.T = 0f;
        w.Player.Invuln = 100_000;
        var ev = Run(w, 1200);
        Assert.Contains(ev, x => x.Kind == Ev.Shoot && x.A == 1f);
        Assert.Contains(w.Enemies, e => e.Kind == EKind.Spike);
        Assert.True(ev.Count(x => x.Kind == Ev.Telegraph && x.I == (int)EKind.Monolith) >= 4);
    }

    [Fact]
    public void Losing_all_health_ends_the_run()
    {
        var w = new World(seed: 2, waves: true);
        w.Player.Hp = 5;
        w.HurtPlayer(10, w.Player.Pos + Up, null);
        Assert.Equal(PState.Dead, w.Player.State);
        Assert.Equal(Phase.Lost, w.Phase);
    }

    [Fact]
    public void A_leftover_shot_cannot_turn_a_victory_into_a_defeat()
    {
        var w = new World(seed: 4, waves: true);
        w.GoToFloor(Dungeon.Floors - 1);
        w.EnterRoom(w.Map.Boss, Side.S);
        w.Wave = w.Room.Waves.Length;
        w.GuardianUp = true;
        w.Phase = Phase.Fighting;
        w.Player.Hp = 5;
        w.Projs.Add(new Proj { Pos = w.Player.Pos + new Vector2(200f, 0f), Vel = new Vector2(-300f, 0f), Life = 4f, Damage = 10 });
        Run(w, 2);
        Assert.Equal(Phase.Won, w.Phase);
        Assert.Empty(w.Projs);
        w.HurtPlayer(10, w.Player.Pos, null);
        Assert.Equal(Phase.Won, w.Phase);
        Assert.Equal(5 + World.RoomClearHeal + Offer.HealAmount, w.Player.Hp); // the guardian drops the red potion
    }

    [Fact]
    public void A_second_shatter_during_a_chain_does_not_burst_the_same_enemy_twice()
    {
        var w = Arena();
        var a = Dummy(w, EKind.Monolith, new Vector2(200f, 0f));
        var b = Dummy(w, EKind.Monolith, new Vector2(300f, 0f));
        a.Fracture = 1;
        w.Player.Focus = Tune.FocusMax;
        var ev = Tap(w, new Inputs { Shatter = true }, 0);
        for (int i = 0; i < 30 && ev.All(x => x.Kind != Ev.Shatter); i++) ev.AddRange(Run(w, 1));
        Assert.True(w.ChainRunning, "a has burst and b is queued in the chain");
        Assert.True(b.Fracture > 0);
        ev.AddRange(Tap(w, new Inputs { Shatter = true }, 60));
        Assert.Equal(2, ev.Count(x => x.Kind == Ev.Shatter)); // a once, b once
    }

    [Fact]
    public void An_enemy_touching_the_players_back_is_not_cut()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Bastion, -Up * 300f);
        int hp = e.Hp;
        Tap(w, new Inputs { Light = true, Aim = Up }, Moves.Light[0].Windup); // the swing starts on the press frame (T=0), so after this the edge goes live next frame
        e.Pos = w.Player.Pos - Up * 30f; // overlapping the player's back on exactly that frame
        Run(w, 12);
        Assert.Equal(hp, e.Hp);
    }

    [Fact]
    public void A_dodged_shot_is_used_up_and_cannot_hit_after_the_dash()
    {
        var w = Arena();
        Tap(w, new Inputs { Dash = true, Move = new Vector2(1f, 0f) }, 1);
        w.Projs.Add(new Proj { Pos = w.Player.Pos, Vel = new Vector2(1f, 0f), Life = 4f, Damage = 10 });
        Run(w, 40);
        Assert.Empty(w.Projs);
        Assert.Equal(100, w.Player.Hp);
    }

    [Fact]
    public void A_spike_that_charges_into_a_wall_is_hurt_and_dazed()
    {
        var w = Arena();
        w.Player.Pos = new Vector2(Tune.HalfW - 60f, 0f);
        var s = Dummy(w, EKind.Spike, new Vector2(-150f, 0f));
        s.State = EState.Act; s.T = 0f; s.Aim = new Vector2(1f, 0f);
        w.Player.Invuln = 1000; // the charge passes the player and meets the wall
        var ev = Run(w, 20);
        Assert.Contains(ev, x => x.Kind == Ev.WallHit && x.I == 1);
        Assert.Equal(EState.Stun, s.State);
        Assert.True(s.StunLen >= 60f);
        Assert.True(s.Hp < s.MaxHp);
    }

    [Fact]
    public void An_enemy_thrown_hard_into_a_wall_takes_impact_damage()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Spike, new Vector2(Tune.HalfW - 50f, 0f) - w.Player.Pos); // stun friction is strong: a throw only slams near a wall
        e.State = EState.Stun; e.T = 0f; e.StunLen = 100f;
        e.Vel = new Vector2(700f, 0f);
        var ev = Run(w, 15);
        Assert.Contains(ev, x => x.Kind == Ev.WallHit && x.I == 0);
        Assert.True(e.Hp < e.MaxHp);
        Assert.True(e.Vel.X <= 0f, "it bounces off");
    }

    [Fact]
    public void Walking_into_a_wall_does_no_harm()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Spike, new Vector2(Tune.HalfW - 60f, 0f) - w.Player.Pos);
        e.Vel = new Vector2(150f, 0f);
        var ev = Run(w, 30);
        Assert.DoesNotContain(ev, x => x.Kind == Ev.WallHit);
        Assert.Equal(e.MaxHp, e.Hp);
    }

    [Fact]
    public void The_boss_charging_into_a_wall_is_dazed_through_its_guard()
    {
        var w = Arena();
        var b = Dummy(w, EKind.Monolith, new Vector2(Tune.HalfW - 200f, 0f) - w.Player.Pos);
        b.State = EState.Act; b.T = 0f; b.Aim = new Vector2(1f, 0f);
        float poise = b.Poise;
        Run(w, 15);
        Assert.Equal(EState.Stun, b.State);
        Assert.True(b.Poise < poise);
    }

    [Fact]
    public void Pillars_block_the_player_and_stop_shots()
    {
        var w = new World(seed: 1, waves: false);
        var pl = w.Pillars[0];
        w.Player.Pos = pl.Pos + new Vector2(-120f, 0f);
        Run(w, 60, new Inputs { Move = new Vector2(1f, 0f) });
        Assert.True(Vector2.Distance(w.Player.Pos, pl.Pos) >= pl.Radius + Tune.PlayerRadius - 0.5f);
        w.Projs.Add(new Proj { Pos = pl.Pos + new Vector2(0f, -100f), Vel = new Vector2(0f, 300f), Life = 4f, Damage = 8 });
        Run(w, 30);
        Assert.Empty(w.Projs);
    }

    [Fact]
    public void Enemies_placed_on_a_pillar_are_pushed_out_of_it()
    {
        var w = new World(seed: 9, waves: true);
        for (int i = 0; i < 200; i++)
        {
            var e = w.Spawn(EKind.Bastion, w.Pillars[i % 4].Pos);
            Assert.False(w.InsidePillar(e.Pos, e.Radius - 0.5f));
        }
    }

    [Fact]
    public void The_spin_hits_all_around_costs_health_and_protects_while_turning()
    {
        var w = Arena();
        var front = Dummy(w, EKind.Bastion, Up * 70f);
        var back = Dummy(w, EKind.Bastion, -Up * 70f);
        var side = Dummy(w, EKind.Bastion, new Vector2(80f, 0f));
        var ev = Tap(w, new Inputs { Special = true, Aim = Up }, 3);
        Assert.Equal(100 - Moves.SpinCost, w.Player.Hp); // nothing hit yet: the windup is 3 frames
        w.HurtPlayer(20, w.Player.Pos + Up, null);
        Assert.Equal(PState.Attack, w.Player.State); // not knocked out of it
        Assert.Equal(100 - Moves.SpinCost, w.Player.Hp); // and the hit was ignored
        Assert.Equal(Moves.SpinCost, w.Player.Recover); // the cost is held as recoverable health
        ev.AddRange(Run(w, 20));
        Assert.Contains(ev, x => x.Kind == Ev.Swing && x.I == Moves.SpinIndex);
        Assert.True(front.Hp < front.MaxHp && back.Hp < back.MaxHp && side.Hp < side.MaxHp);
    }

    [Fact]
    public void Spin_health_comes_back_by_landing_hits_and_is_lost_when_hit()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Monolith, Up * 90f);
        Tap(w, new Inputs { Special = true, Aim = Up }, 30);
        Assert.Equal(100 - Moves.SpinCost + Tune.RecoverPerHit, w.Player.Hp); // the spin's own hit pays some back
        Assert.Equal(Moves.SpinCost - Tune.RecoverPerHit, w.Player.Recover);
        for (int i = 0; i < 2; i++) { e.Pos = w.Player.Pos + Up * 90f; Tap(w, new Inputs { Light = true, Aim = Up }, 25); }
        Assert.Equal(100 - Moves.SpinCost + 3 * Tune.RecoverPerHit, w.Player.Hp);
        w.HurtPlayer(5, w.Player.Pos + Up, null);
        Assert.Equal(0, w.Player.Recover);
    }

    [Fact]
    public void The_spin_cannot_be_used_at_low_health()
    {
        var w = Arena();
        w.Player.Hp = Moves.SpinCost;
        var ev = Tap(w, new Inputs { Special = true, Aim = Up }, 10);
        Assert.DoesNotContain(ev, x => x.Kind == Ev.Swing);
        Assert.Equal(Moves.SpinCost, w.Player.Hp);
    }

    [Fact]
    public void The_combo_counter_grows_with_hits_and_drops_after_a_pause_or_a_hit_taken()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Monolith, Up * 90f);
        for (int i = 0; i < 3; i++) { e.Pos = w.Player.Pos + Up * 90f; Tap(w, new Inputs { Light = true, Aim = Up }, 16); }
        Assert.Equal(3, w.Combo);
        w.HurtPlayer(5, w.Player.Pos + Up, null);
        Assert.Equal(0, w.Combo);
        Assert.Equal(3, w.BestCombo);
        e.Pos = w.Player.Pos + Up * 90f;
        Run(w, 60);
        Tap(w, new Inputs { Light = true, Aim = Up }, 20);
        Assert.Equal(1, w.Combo);
        Run(w, Tune.ComboDrop + 5);
        Assert.Equal(0, w.Combo);
    }

    [Fact]
    public void A_thrown_enemy_bowls_over_the_ones_behind_it()
    {
        var w = Arena();
        var thrown = Dummy(w, EKind.Spike, new Vector2(-200f, -100f));
        var pin = Dummy(w, EKind.Spike, new Vector2(-120f, -100f));
        var pin2 = Dummy(w, EKind.Spike, new Vector2(-75f, -100f));
        thrown.State = EState.Stun; thrown.T = 0f; thrown.StunLen = 60f;
        thrown.Vel = new Vector2(900f, 0f);
        var ev = Run(w, 20);
        Assert.Contains(ev, x => x.Kind == Ev.Bowl);
        Assert.True(pin.Hp < pin.MaxHp);
        Assert.True(pin2.Hp < pin2.MaxHp, "the knocked pin carries on into the next");
        Assert.True(w.Combo >= 2);
    }

    [Fact]
    public void A_walking_enemy_bumping_another_does_nothing()
    {
        var w = Arena();
        var a = Dummy(w, EKind.Spike, new Vector2(-200f, -100f));
        var b = Dummy(w, EKind.Spike, new Vector2(-170f, -100f));
        a.Vel = new Vector2(150f, 0f);
        var ev = Run(w, 20);
        Assert.DoesNotContain(ev, x => x.Kind == Ev.Bowl);
        Assert.Equal(b.MaxHp, b.Hp);
    }

    [Fact]
    public void Wave_spawns_never_land_inside_a_pillar()
    {
        for (uint seed = 1; seed <= 40; seed++)
        {
            var w = new World(seed, waves: true);
            var room = w.Map.Rooms.Values.First(r => r.Kind == RoomKind.Fight);
            room.Layout = seed % 2 == 0 ? 0 : 3; // layouts with pillars
            w.EnterRoom(room, Side.S);
            w.Player.Pos = w.Pillars[(int)(seed % (uint)w.Pillars.Count)].Pos + new Vector2(0f, 60f); // near stone, so the picker has to avoid it
            var checkedIds = new HashSet<int>();
            for (int i = 0; i < 600 && (w.Wave < 0 || w.Incoming.Count > 0); i++) // they step in group by group: check each as it lands
            {
                w.Step(new Inputs());
                foreach (var e in w.Enemies.Where(e => checkedIds.Add(e.Id)))
                    Assert.False(w.InsidePillar(e.Pos, 50f), $"seed {seed}: spawned at {e.Pos}");
            }
            Assert.NotEmpty(checkedIds);
        }
    }

    [Fact]
    public void A_charge_into_stone_is_not_the_players_hit()
    {
        var w = Arena();
        w.Player.Pos = new Vector2(Tune.HalfW - 60f, 0f);
        w.Player.Hp = 80; w.Player.Recover = 10;
        var s = Dummy(w, EKind.Spike, new Vector2(-150f, 0f));
        s.State = EState.Act; s.T = 0f; s.Aim = new Vector2(1f, 0f);
        w.Player.Invuln = 1000;
        var ev = Run(w, 20);
        Assert.Contains(ev, x => x.Kind == Ev.WallHit && x.I == 1);
        Assert.Equal(0, w.Combo);
        Assert.Equal(80, w.Player.Hp);
        Assert.Equal(10, w.Player.Recover);
    }

    [Fact]
    public void A_charge_brushing_along_a_wall_does_not_daze()
    {
        var w = Arena();
        var s = Dummy(w, EKind.Spike, new Vector2(-300f, 0f) - w.Player.Pos);
        s.Pos = new Vector2(-300f, Tune.HalfH - s.Radius); // touching the bottom wall (Spawn keeps a margin)
        s.State = EState.Act; s.T = 0f; s.Aim = Vector2.Normalize(new Vector2(1f, 0.02f));
        var ev = Run(w, 20);
        Assert.DoesNotContain(ev, x => x.Kind == Ev.WallHit);
        Assert.NotEqual(EState.Stun, s.State);
    }

    [Fact]
    public void A_wall_slam_never_shortens_a_longer_daze()
    {
        var w = Arena();
        var e = Dummy(w, EKind.Spike, new Vector2(Tune.HalfW - 50f, 0f) - w.Player.Pos);
        e.State = EState.Stun; e.T = 0f; e.StunLen = 200f;
        e.Vel = new Vector2(700f, 0f);
        var ev = Run(w, 10);
        Assert.Contains(ev, x => x.Kind == Ev.WallHit);
        Assert.True(e.StunLen - e.T > 150f, $"daze left {e.StunLen - e.T}");
    }

    [Fact]
    public void A_room_clear_heal_keeps_health_plus_recoverable_within_max()
    {
        var w = new World(seed: 5, waves: true);
        w.EnterRoom(w.Map.Rooms.Values.First(r => r.Kind == RoomKind.Fight), Side.S);
        w.Wave = w.Room.Waves.Length; w.GuardianUp = true; w.Phase = Phase.Fighting; w.Enemies.Clear();
        w.Player.Hp = 90; w.Player.Recover = 10;
        Run(w, 2);
        Assert.True(w.GatesOpen);
        Assert.Equal(100, w.Player.Hp);
        Assert.Equal(0, w.Player.Recover);
    }

    [Fact]
    public void The_spin_cannot_cancel_into_another_spin()
    {
        var w = Arena();
        var ev = Tap(w, new Inputs { Special = true, Aim = Up }, 18); // in the spin's recovery
        ev.AddRange(Tap(w, new Inputs { Special = true, Aim = Up }, 10)); // long enough to see a second spin's swing event
        Assert.Equal(1, ev.Count(x => x.Kind == Ev.Swing && x.I == Moves.SpinIndex));
        Run(w, Moves.SpinCooldown + 10);
        ev = Tap(w, new Inputs { Special = true, Aim = Up }, 6);
        Assert.Equal(1, ev.Count(x => x.Kind == Ev.Swing && x.I == Moves.SpinIndex));
    }
}
