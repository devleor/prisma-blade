using System.Numerics;

namespace Prisma.Sim;

/// <summary>
/// The whole game state, stepped once per 60 Hz frame. The player half lives here; enemies, projectiles and waves
/// are in the other partial files. Hitstop freezes everything but input buffering; slow-mo (after a perfect dodge)
/// slows only enemies and their shots.
/// </summary>
public sealed partial class World
{
    public readonly Player Player = new();
    public readonly List<Enemy> Enemies = [];
    public readonly List<Proj> Projs = [];
    public readonly List<GameEvent> Events = [];
    public readonly Rng Rng;

    public int Frame, Hitstop, SlowMo;
    int nextId, swingCounter, castCounter;

    public World(uint seed = 7, bool waves = true)
    {
        Rng = new Rng(seed);
        Player.Pos = new Vector2(0f, 120f);
        Player.Facing = -MathF.PI / 2f;
        AutoWaves = waves;
        Phase = waves ? Phase.Intermission : Phase.Fighting;
        PhaseT = 70;
    }

    void Emit(Ev kind, Vector2 pos, float a = 0f, int i = 0) => Events.Add(new GameEvent(kind, pos, a, i));

    public void Step(Inputs inp)
    {
        Events.Clear();
        Frame++;
        ReadButtons(inp);
        if (Hitstop > 0) { Hitstop--; return; }
        float ts = 1f;
        if (SlowMo > 0) { SlowMo--; ts = Tune.SlowTimeScale; }

        StepPlayer(inp);
        StepBursts();
        foreach (var e in Enemies.ToArray()) StepEnemy(e, ts);
        Separate();
        StepProjs(ts);
        Enemies.RemoveAll(e => !e.Alive);
        StepWaves();
    }

    // ---------------------------------------------------------------- player

    void ReadButtons(Inputs inp)
    {
        var p = Player;
        bool heavyPressed = inp.HeavyHeld && !p.PrevHeavy;
        p.PrevHeavy = inp.HeavyHeld;
        if (inp.Light) Buffer(Buffered.Light);
        if (heavyPressed) Buffer(Buffered.Heavy);
        if (inp.Shatter) Buffer(Buffered.Shatter);
        if (inp.Dash) Buffer(Buffered.Dash); // dash wins a same-frame tie: escaping matters most
    }

    void Buffer(Buffered b) { Player.Buffer = b; Player.BufferT = Tune.BufferFrames; }
    void Consume() { Player.Buffer = Buffered.None; Player.BufferT = 0; }

    void StepPlayer(Inputs inp)
    {
        var p = Player;
        p.T++;
        if (p.BufferT > 0 && --p.BufferT == 0) p.Buffer = Buffered.None;
        if (p.Invuln > 0) p.Invuln--;
        if (p.DashCd > 0) p.DashCd--;
        if (p.ComboTimer > 0) p.ComboTimer--;
        p.BladeActive = false;

        var move = inp.Move.LengthSquared() > 1f ? Vector2.Normalize(inp.Move) : inp.Move;
        float aim = inp.Aim.LengthSquared() > 1e-4f ? Tune.Angle(inp.Aim)
            : move.LengthSquared() > 0.04f ? Tune.Angle(move) : p.Facing;

        switch (p.State)
        {
            case PState.Free:
                Steer(move, 1f);
                p.Facing = aim;
                p.BladeAngle = p.Facing + 2.3f;
                if (inp.HeavyHeld && p.Buffer == Buffered.None) Buffer(Buffered.Heavy); // still holding after a cancel
                TryAct(aim, move, fromRecovery: false);
                break;

            case PState.Attack:
                StepSwing(move, aim);
                break;

            case PState.Charge:
                Steer(move, Tune.ChargeSpeedMul);
                p.Facing = aim;
                p.BladeAngle = p.Facing - 2.4f;
                if (p.Charge < Tune.ChargeMax && ++p.Charge == Tune.ChargeMax) Emit(Ev.ChargeFull, p.Pos);
                if (p.Buffer == Buffered.Dash && p.DashCd == 0) { StartDash(move); break; }
                if (!inp.HeavyHeld) StartSwing(Moves.Heavy(p.Charge), heavy: true, combo: 0, aim);
                break;

            case PState.Dash:
                float k = p.T <= Tune.DashFrames ? 1f : 1f - (p.T - Tune.DashFrames) / (float)Tune.DashTail;
                p.Vel = p.DashDir * Tune.DashSpeed * MathF.Max(0.25f, k);
                p.BladeAngle = Tune.Angle(p.DashDir) + 2.7f;
                if (p.T >= Tune.DashFrames + Tune.DashTail) { p.State = PState.Free; p.T = 0; p.Vel *= 0.35f; }
                break;

            case PState.Hurt:
                Friction(9f);
                if (p.T >= Tune.HurtFrames) { p.State = PState.Free; p.T = 0; }
                break;

            case PState.Dead:
                Friction(6f);
                break;
        }

        p.Pos = Tune.Clamp(p.Pos + p.Vel * Tune.Dt, Tune.PlayerRadius);
    }

    void Steer(Vector2 move, float mul)
    {
        var p = Player;
        var target = move * Tune.MoveSpeed * mul;
        p.Vel += (target - p.Vel) * MathF.Min(1f, Tune.MoveSharp * Tune.Dt);
    }

    void Friction(float k) => Player.Vel *= MathF.Max(0f, 1f - k * Tune.Dt);

    /// <summary>Acts on the buffered press. From Free anything goes; from a swing's recovery only cancels allowed.</summary>
    bool TryAct(float aim, Vector2 move, bool fromRecovery)
    {
        var p = Player;
        switch (p.Buffer)
        {
            case Buffered.Dash when p.DashCd == 0:
                StartDash(move);
                return true;
            case Buffered.Shatter when p.Focus >= Tune.ShatterCost:
                Consume();
                CastShatter();
                return true;
            case Buffered.Heavy:
                Consume();
                p.State = PState.Charge; p.T = 0; p.Charge = 0;
                Emit(Ev.Charge, p.Pos);
                return true;
            case Buffered.Light:
                int idx = fromRecovery ? (p.HeavySwing ? 0 : p.Combo + 1) : (p.ComboTimer > 0 ? p.NextCombo : 0);
                if (idx >= Moves.Light.Length) return false; // the finisher must recover; the press stays buffered
                Consume();
                StartSwing(Moves.Light[idx], heavy: false, combo: idx, aim);
                return true;
        }
        return false;
    }

    void StartSwing(Swing s, bool heavy, int combo, float aim)
    {
        var p = Player;
        p.State = PState.Attack; p.T = 0;
        p.Swing = s; p.HeavySwing = heavy; p.Combo = combo;
        p.Facing = aim;
        p.BladeReach = s.Reach;
        p.BladeAngle = p.Facing + s.ArcStart * 1.12f;
    }

    void StepSwing(Vector2 move, float aim)
    {
        var p = Player;
        var s = p.Swing;
        int activeStart = s.Windup + 1, activeEnd = s.Windup + s.Active;

        if (p.T < activeStart)
        {
            // windup: still turning toward the aim a little, and a dash can still bail out
            p.Facing += Tune.Wrap(aim - p.Facing) * 0.35f;
            p.BladeAngle = p.Facing + s.ArcStart * 1.12f;
            Friction(10f);
            if (p.Buffer == Buffered.Dash && p.DashCd == 0) { StartDash(move); return; }
            return;
        }

        if (p.T == activeStart)
        {
            p.SwingId = ++swingCounter;
            p.Vel = Tune.Dir(p.Facing) * s.Lunge + move * Tune.MoveSpeed * Tune.AttackSteer;
            Emit(Ev.Swing, p.Pos, p.Facing, p.HeavySwing ? 3 : p.Combo);
        }

        if (p.T <= activeEnd)
        {
            float prog = (p.T - s.Windup) / (float)s.Active;
            float ease = 1f - (1f - prog) * (1f - prog);
            float cur = s.ArcStart + (s.ArcEnd - s.ArcStart) * ease;
            p.BladeAngle = p.Facing + cur;
            p.BladeActive = true;
            Friction(7f);
            Cut(MathF.Min(s.ArcStart, cur), MathF.Max(s.ArcStart, cur));
            return;
        }

        // recovery: the blade rests at the end of the arc; cancels open after a short delay
        p.BladeAngle = p.Facing + s.ArcEnd;
        Steer(move, Tune.AttackSteer * 2f);
        Friction(9f);
        if (p.T >= activeEnd + Tune.CancelDelay && TryAct(aim, move, fromRecovery: true)) return;
        if (p.T >= s.Total)
        {
            p.State = PState.Free; p.T = 0;
            p.NextCombo = p.HeavySwing ? 0 : (p.Combo + 1) % Moves.Light.Length;
            p.ComboTimer = p.NextCombo == 0 ? 0 : Tune.ComboWindow;
        }
    }

    void StartDash(Vector2 move)
    {
        var p = Player;
        Consume();
        p.State = PState.Dash; p.T = 0;
        p.DashDir = move.LengthSquared() > 0.04f ? Tune.Norm(move) : Tune.Dir(p.Facing);
        p.DashCd = Tune.DashCooldown;
        p.PerfectUsed = false;
        p.Charge = 0;
        Emit(Ev.Dash, p.Pos, Tune.Angle(p.DashDir));
    }

    /// <summary>Everything the edge passed through this swing: enemies inside the swept arc, and enemy shots.</summary>
    void Cut(float lo, float hi)
    {
        var p = Player;
        var s = p.Swing;
        foreach (var e in Enemies)
        {
            if (!e.Hittable || e.LastSwing == p.SwingId) continue;
            if (InArc(e.Pos, e.Radius, s.Reach, lo, hi)) HitEnemy(e, s);
        }
        foreach (var pr in Projs)
        {
            if (pr.Friendly || !InArc(pr.Pos, pr.Radius, s.Reach, lo, hi)) continue;
            pr.Friendly = true;
            pr.Vel = Tune.Norm(pr.Pos - p.Pos) * MathF.Max(520f, pr.Vel.Length() * 1.7f);
            pr.Life = 2f;
            pr.Damage = 14;
            p.Focus = MathF.Min(Tune.FocusMax, p.Focus + 5f);
            Emit(Ev.Deflect, pr.Pos);
        }
    }

    bool InArc(Vector2 pos, float radius, float reach, float lo, float hi)
    {
        var p = Player;
        var d = pos - p.Pos;
        float dist = d.Length();
        if (dist > reach + radius) return false;
        float rel = Tune.Wrap(Tune.Angle(d) - p.Facing);
        if (dist < radius + Tune.PlayerRadius) return MathF.Abs(rel) < MathF.PI / 2f; // touching, in front: caught
        float pad = MathF.Asin(MathF.Min(1f, radius / dist));
        return rel + pad >= lo && rel - pad <= hi;
    }

    void HitEnemy(Enemy e, Swing s)
    {
        var p = Player;
        e.LastSwing = p.SwingId;
        bool heavy = p.HeavySwing;
        var dir = Tune.Norm(e.Pos - p.Pos);
        if (dir == Vector2.Zero) dir = Tune.Dir(p.Facing);
        Damage(e, s.Damage, dir, s.Knock, s.Poise, heavy ? 28f : 16f);
        e.Fracture = Math.Min(Tune.MaxFracture, e.Fracture + s.Fracture);
        Hitstop = Math.Max(Hitstop, s.Hitstop);
        p.Focus = MathF.Min(Tune.FocusMax, p.Focus + (heavy ? 10f : 4f));
        Emit(Ev.Hit, e.Pos, s.Damage, heavy ? 1 : 0);
    }

    /// <summary>Damage, poise and knockback for any source. Armoured enemies only flinch once their poise breaks.</summary>
    void Damage(Enemy e, int dmg, Vector2 dir, float knock, float poiseDmg, float stun)
    {
        e.Hp -= dmg;
        e.Flash = 6f;
        bool armoured = false;
        if (e.MaxPoise > 0f)
        {
            e.Poise -= poiseDmg;
            e.PoiseIdle = 0f;
            if (e.Poise <= 0f) { e.Poise = e.MaxPoise; stun = e.Boss ? 80f : 55f; }
            else armoured = true;
        }
        float scale = e.Boss ? 0.35f : 1f;
        e.Vel += dir * knock * (armoured ? 0.12f : 1f) * scale;
        if (!armoured) { e.State = EState.Stun; e.T = 0f; e.StunLen = stun; }
        if (e.Hp <= 0) Kill(e);
    }

    void Kill(Enemy e)
    {
        if (e.State == EState.Dead) return;
        e.State = EState.Dead;
        Emit(Ev.Kill, e.Pos, e.Radius, (int)e.Kind);
    }

    /// <summary>
    /// Shatter: every fractured enemy bursts. A burst hurts and fractures its neighbours, which burst a few frames
    /// later, so a crowd collapses in a chain. With nothing fractured it is a short nova that fractures what is near.
    /// </summary>
    void CastShatter()
    {
        var p = Player;
        p.Focus -= Tune.ShatterCost;
        p.Invuln = Math.Max(p.Invuln, 30);
        int cast = ++castCounter;
        Hitstop = Math.Max(Hitstop, 6);
        Emit(Ev.ShatterCast, p.Pos);
        bool any = false;
        foreach (var e in Enemies)
        {
            if (!e.Hittable || e.Fracture <= 0) continue;
            any = true;
            if (pending.Any(b => b.E == e)) continue; // already bursting in a running chain
            e.BurstCast = cast;
            pending.Add(new Burst(e, 2, 0));
        }
        if (any) return;
        foreach (var e in Enemies)
        {
            if (!e.Hittable || Vector2.Distance(e.Pos, p.Pos) > 170f + e.Radius) continue;
            e.Fracture = Math.Min(Tune.MaxFracture, e.Fracture + 1);
            Damage(e, 10, Tune.Norm(e.Pos - p.Pos), 300f, 15f, 16f);
            Emit(Ev.Hit, e.Pos, 10, 1);
        }
    }

    record struct Burst(Enemy E, int Delay, int Depth);
    readonly List<Burst> pending = [];
    public bool ChainRunning => pending.Count > 0;

    void StepBursts()
    {
        if (pending.Count == 0) return;
        var now = new List<Burst>();
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var b = pending[i];
            if (b.Delay > 1) { pending[i] = b with { Delay = b.Delay - 1 }; continue; }
            pending.RemoveAt(i);
            now.Add(b);
        }
        foreach (var (e, _, depth) in now)
        {
            if (!e.Alive) continue;
            int stacks = Math.Max(1, e.Fracture);
            e.Fracture = 0;
            Damage(e, 14 + 20 * stacks, Vector2.Zero, 0f, 30f * stacks, 30f);
            Emit(Ev.Shatter, e.Pos, stacks, depth);
            Hitstop = Math.Max(Hitstop, 3);
            foreach (var n in Enemies)
            {
                if (n == e || !n.Hittable || n.BurstCast == e.BurstCast) continue;
                if (Vector2.Distance(n.Pos, e.Pos) > Tune.ShatterChainRadius + n.Radius) continue;
                n.BurstCast = e.BurstCast;
                n.Fracture = Math.Min(Tune.MaxFracture, n.Fracture + 1);
                Damage(n, 8, Tune.Norm(n.Pos - e.Pos), 260f, 10f, 20f);
                pending.Add(new Burst(n, Tune.ShatterChainDelay, depth + 1));
            }
        }
    }

    /// <summary>An enemy attack reaches the player. A dash in its first frames turns it into a perfect dodge.</summary>
    public void HurtPlayer(int dmg, Vector2 from, Enemy? src)
    {
        var p = Player;
        if (!p.Alive || Phase == Phase.Won) return;
        if (p.State == PState.Dash && p.T <= Tune.PerfectFrames)
        {
            if (!p.PerfectUsed) PerfectDodge(src);
            return;
        }
        if (p.Invulnerable) return;
        p.Hp -= dmg;
        p.Invuln = Tune.HurtInvuln;
        p.Vel = Tune.Norm(p.Pos - from) * 420f;
        p.ComboTimer = 0;
        p.Charge = 0;
        Hitstop = Math.Max(Hitstop, 5);
        Emit(Ev.PlayerHurt, p.Pos, dmg);
        if (p.Hp <= 0)
        {
            p.Hp = 0;
            p.State = PState.Dead; p.T = 0;
            Phase = Phase.Lost;
            Emit(Ev.Defeat, p.Pos);
        }
        else { p.State = PState.Hurt; p.T = 0; }
    }

    void PerfectDodge(Enemy? src)
    {
        var p = Player;
        p.PerfectUsed = true;
        SlowMo = Tune.PerfectSlowFrames;
        Hitstop = Math.Max(Hitstop, 4);
        p.Focus = MathF.Min(Tune.FocusMax, p.Focus + 25f);
        p.Invuln = Math.Max(p.Invuln, Tune.DashFrames + Tune.DashTail + 12);
        p.DashCd = 0;
        if (src is { Hittable: true }) src.Fracture = Math.Min(Tune.MaxFracture, src.Fracture + 1);
        foreach (var e in Enemies)
            if (e.Hittable && Vector2.Distance(e.Pos, p.Pos) < Tune.PerfectMarkRadius + e.Radius)
                e.Fracture = Math.Min(Tune.MaxFracture, e.Fracture + 1);
        Emit(Ev.Perfect, p.Pos);
    }
}
