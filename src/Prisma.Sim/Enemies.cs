using System.Numerics;

namespace Prisma.Sim;

/// <summary>
/// Enemy behaviour. Every attack is telegraphed (a pause the view draws as a warning) and followed by a recovery
/// window to punish. ts is the time scale: 1, or less during the slow-mo after a perfect dodge.
///   Spike (triangle): aims, then charges in a straight line.
///   Orb (circle): keeps its distance and fires a 3-shot spread.
///   Bastion (hexagon): armoured, slow, slams the ground around it.
///   Monolith (diamond, boss): charge chains, radial bursts, summons Spikes; faster below half health.
/// </summary>
public sealed partial class World
{
    public Phase Phase;
    public int Wave = -1, PhaseT;
    public readonly bool AutoWaves;

    public static readonly EKind[][] Waves =
    [
        [EKind.Spike, EKind.Spike, EKind.Spike],
        [EKind.Spike, EKind.Spike, EKind.Orb, EKind.Orb],
        [EKind.Bastion, EKind.Spike, EKind.Spike, EKind.Orb, EKind.Orb],
        [EKind.Bastion, EKind.Bastion, EKind.Orb, EKind.Orb, EKind.Orb, EKind.Spike, EKind.Spike, EKind.Spike],
        [EKind.Monolith],
    ];

    public Enemy Spawn(EKind kind, Vector2 pos)
    {
        var e = Enemy.Make(kind, Tune.Clamp(pos, 40f), nextId++);
        e.Pos = Collide(e.Pos, e.Radius, out _);
        e.StrafeSign = Rng.F() < 0.5f ? -1f : 1f;
        Enemies.Add(e);
        return e;
    }

    void StepWaves()
    {
        if (!AutoWaves || Phase is Phase.Won or Phase.Lost) return;
        if (Phase == Phase.Intermission)
        {
            if (--PhaseT > 0) return;
            Wave++;
            Phase = Phase.Fighting;
            foreach (var kind in Waves[Wave])
            {
                var pos = kind == EKind.Monolith ? new Vector2(0f, -220f) : SpawnPoint();
                Spawn(kind, pos);
            }
            Emit(Ev.WaveStart, Vector2.Zero, 0f, Wave);
            return;
        }
        if (Enemies.Count > 0 || ChainRunning) return;
        Projs.RemoveAll(pr => !pr.Friendly); // stale shots must not hit during the pause or after victory
        if (Wave >= Waves.Length - 1)
        {
            Phase = Phase.Won;
            Emit(Ev.Victory, Player.Pos);
            return;
        }
        Phase = Phase.Intermission;
        PhaseT = 120;
        Player.Hp = Math.Min(Player.MaxHp, Player.Hp + 25);
        Player.Recover = Math.Min(Player.Recover, Player.MaxHp - Player.Hp);
        Emit(Ev.WaveClear, Player.Pos, 0f, Wave);
    }

    Vector2 SpawnPoint()
    {
        for (int i = 0; i < 20; i++)
        {
            var pos = Player.Pos + Tune.Dir(Rng.Range(0f, MathF.Tau)) * Rng.Range(260f, 420f);
            if (MathF.Abs(pos.X) < Tune.HalfW - 50f && MathF.Abs(pos.Y) < Tune.HalfH - 50f && !InsidePillar(pos, 60f)) return pos;
        }
        return new Vector2(-Player.Pos.X, -Player.Pos.Y * 0.5f);
    }

    void StepEnemy(Enemy e, float ts)
    {
        if (!e.Alive) return;
        float dt = Tune.Dt * ts;
        e.T += ts;
        e.AttackCd -= ts;
        if (e.Flash > 0f) e.Flash -= ts;
        if (e.MaxPoise > 0f && (e.PoiseIdle += ts) > 120f) e.Poise = MathF.Min(e.MaxPoise, e.Poise + 0.6f * ts);

        var p = Player;
        var toP = p.Pos - e.Pos;
        float dist = toP.Length();
        var dir = Tune.Norm(toP);

        switch (e.State)
        {
            case EState.Spawn:
                Slow(e, 10f, dt);
                if (e.T >= 40f) Go(e, EState.Idle);
                break;
            case EState.Stun:
                Slow(e, 7f, dt);
                if (e.T >= e.StunLen) { Go(e, EState.Idle); e.AttackCd = MathF.Max(e.AttackCd, 20f); }
                break;
            default:
                if (!p.Alive) { Slow(e, 6f, dt); break; }
                switch (e.Kind)
                {
                    case EKind.Spike: SpikeAi(e, dir, dist, dt); break;
                    case EKind.Orb: OrbAi(e, dir, dist, dt); break;
                    case EKind.Bastion: BastionAi(e, dir, dist, dt); break;
                    case EKind.Monolith: MonolithAi(e, dir, dist, dt); break;
                }
                break;
        }

        if (e.WallCd > 0f) e.WallCd -= ts;
        if (e.BowlCd > 0f) e.BowlCd -= ts;
        MoveEnemy(e, dt);
    }

    static void Go(Enemy e, EState s) { e.State = s; e.T = 0f; }
    static void Slow(Enemy e, float k, float dt) => e.Vel *= MathF.Max(0f, 1f - k * dt);
    static void Seek(Enemy e, Vector2 vel, float dt) => e.Vel += (vel - e.Vel) * MathF.Min(1f, 7f * dt);

    void Telegraph(Enemy e, int mode = 0)
    {
        Go(e, EState.Telegraph);
        e.Mode = mode;
        Emit(Ev.Telegraph, e.Pos, 0f, (int)e.Kind);
    }

    void Contact(Enemy e, int dmg)
    {
        if (e.HitPlayerThisAct) return;
        if (Vector2.Distance(e.Pos, Player.Pos) > e.Radius + Tune.PlayerRadius + 4f) return;
        e.HitPlayerThisAct = true;
        HurtPlayer(dmg, e.Pos, e);
    }

    void SpikeAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        switch (e.State)
        {
            case EState.Idle:
                var side = new Vector2(-dir.Y, dir.X) * e.StrafeSign;
                Seek(e, (dir * 0.8f + side * 0.45f) * e.Speed, dt);
                if (dist < 340f && e.AttackCd <= 0f) Telegraph(e);
                break;
            case EState.Telegraph:
                Slow(e, 9f, dt);
                if (e.T < 24f) e.Aim = dir;
                if (e.T >= 32f) { Go(e, EState.Act); e.HitPlayerThisAct = false; }
                break;
            case EState.Act:
                e.Vel = e.Aim * 760f * (1f - e.T / 60f);
                Contact(e, 12);
                if (e.T >= 22f) Go(e, EState.Recover);
                break;
            case EState.Recover:
                Slow(e, 6f, dt);
                if (e.T >= 45f) { Go(e, EState.Idle); e.AttackCd = Rng.Range(45f, 90f); }
                break;
        }
    }

    void OrbAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        switch (e.State)
        {
            case EState.Idle:
                var side = new Vector2(-dir.Y, dir.X) * e.StrafeSign;
                var want = dist < 290f ? -dir : dist > 430f ? dir : side;
                Seek(e, want * e.Speed, dt);
                if (e.AttackCd <= 0f && dist < 650f) Telegraph(e);
                break;
            case EState.Telegraph:
                Slow(e, 9f, dt);
                e.Aim = dir;
                if (e.T >= 30f)
                {
                    float a = Tune.Angle(e.Aim);
                    for (int i = -1; i <= 1; i++) Fire(e.Pos + e.Aim * e.Radius, a + i * 0.22f, 320f, 8);
                    Emit(Ev.Shoot, e.Pos);
                    Go(e, EState.Recover);
                }
                break;
            case EState.Recover:
                Slow(e, 6f, dt);
                if (e.T >= 20f) { Go(e, EState.Idle); e.AttackCd = Rng.Range(90f, 130f); e.StrafeSign = -e.StrafeSign; }
                break;
        }
    }

    public const float SlamRadius = 135f;

    void BastionAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        switch (e.State)
        {
            case EState.Idle:
                Seek(e, dir * e.Speed, dt);
                if (dist < 150f && e.AttackCd <= 0f) Telegraph(e);
                break;
            case EState.Telegraph:
                Slow(e, 10f, dt);
                if (e.T >= 46f)
                {
                    if (dist < SlamRadius + Tune.PlayerRadius) HurtPlayer(18, e.Pos, e);
                    Emit(Ev.Slam, e.Pos, SlamRadius);
                    Go(e, EState.Recover);
                }
                break;
            case EState.Recover:
                Slow(e, 6f, dt);
                if (e.T >= 50f) { Go(e, EState.Idle); e.AttackCd = 40f; }
                break;
            case EState.Act:
                Go(e, EState.Idle);
                break;
        }
    }

    void MonolithAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        bool p2 = e.Phase2;
        switch (e.State)
        {
            case EState.Idle:
                var side = new Vector2(-dir.Y, dir.X) * e.StrafeSign;
                Seek(e, (dist > 260f ? dir : side) * e.Speed, dt);
                if (e.T >= (p2 ? 40f : 70f))
                {
                    int mode = e.Pattern++ % 3;
                    if (mode == 2 && Enemies.Count >= 6) mode = 1;
                    e.Sub = 0;
                    Telegraph(e, mode);
                }
                break;

            case EState.Telegraph:
                Slow(e, 8f, dt);
                float wind = e.Mode == 0 ? (e.Sub == 0 ? 36f : 22f) : 40f;
                if (e.Mode == 0 && e.T < wind - 8f) e.Aim = dir;
                if (e.T < wind) break;
                if (e.Mode == 0) { Go(e, EState.Act); e.HitPlayerThisAct = false; break; }
                if (e.Mode == 1)
                {
                    int n = p2 ? 24 : 16;
                    float off = e.Sub * 0.5f * MathF.Tau / n;
                    for (int i = 0; i < n; i++) Fire(e.Pos, off + i * MathF.Tau / n, p2 ? 260f : 220f, 10);
                    Emit(Ev.Shoot, e.Pos, 1f);
                    if (++e.Sub < (p2 ? 3 : 2)) { e.T = 18f; break; }
                }
                else
                {
                    for (int i = 0; i < (p2 ? 3 : 2); i++)
                        Spawn(EKind.Spike, e.Pos + Tune.Dir(i * 2.1f + Rng.F()) * 110f);
                    Emit(Ev.Telegraph, e.Pos, 1f, (int)EKind.Spike);
                }
                Go(e, EState.Recover);
                break;

            case EState.Act:
                e.Vel = e.Aim * (p2 ? 980f : 880f);
                Contact(e, 16);
                if (e.T >= 24f)
                {
                    e.Vel *= 0.2f;
                    if (++e.Sub < (p2 ? 4 : 3)) { Go(e, EState.Telegraph); Emit(Ev.Telegraph, e.Pos, 0f, (int)e.Kind); }
                    else Go(e, EState.Recover);
                }
                break;

            case EState.Recover:
                Slow(e, 5f, dt);
                if (e.T >= (p2 ? 40f : 60f)) { Go(e, EState.Idle); e.StrafeSign = -e.StrafeSign; }
                break;
        }
    }

    void Fire(Vector2 pos, float angle, float speed, int dmg) =>
        Projs.Add(new Proj { Pos = pos, Vel = Tune.Dir(angle) * speed, Life = 4f, Damage = dmg });

    void StepProjs(float ts)
    {
        var p = Player;
        for (int i = Projs.Count - 1; i >= 0; i--)
        {
            var pr = Projs[i];
            float s = pr.Friendly ? 1f : ts;
            pr.Pos += pr.Vel * Tune.Dt * s;
            pr.Life -= Tune.Dt * s;
            bool gone = pr.Life <= 0f || MathF.Abs(pr.Pos.X) > Tune.HalfW || MathF.Abs(pr.Pos.Y) > Tune.HalfH
                || InsidePillar(pr.Pos, pr.Radius); // stone stops shots, reflected ones too
            if (!gone && pr.Friendly)
            {
                foreach (var e in Enemies)
                {
                    if (!e.Hittable || Vector2.Distance(e.Pos, pr.Pos) > e.Radius + pr.Radius) continue;
                    e.Fracture = Math.Min(Tune.MaxFracture, e.Fracture + 1);
                    Damage(e, pr.Damage, Tune.Norm(pr.Vel), 200f, 14f, 14f);
                    Emit(Ev.Hit, e.Pos, pr.Damage, 0);
                    gone = true;
                    break;
                }
            }
            else if (!gone && p.Alive && Vector2.Distance(p.Pos, pr.Pos) < Tune.PlayerRadius + pr.Radius)
            {
                HurtPlayer(pr.Damage, pr.Pos - pr.Vel, null);
                gone = true; // consumed even when dodged, so it cannot hit after the i-frames end
            }
            if (gone) Projs.RemoveAt(i);
        }
    }

    /// <summary>A body thrown fast enough knocks over whoever it runs into, which can carry on down the line.</summary>
    void Bowl(Enemy thrown, Enemy other)
    {
        if (thrown.State != EState.Stun || other.BowlCd > 0f || !other.Hittable || !thrown.Alive) return;
        float speed = thrown.Vel.Length();
        if (speed < Tune.BowlSpeed) return;
        other.BowlCd = 30f;
        thrown.BowlCd = MathF.Max(thrown.BowlCd, 10f);
        Damage(other, 8, Tune.Norm(other.Pos - thrown.Pos), speed * 0.75f, 12f, 26f);
        thrown.Vel *= 0.6f;
        Hitstop = Math.Max(Hitstop, 3);
        Emit(Ev.Bowl, (thrown.Pos + other.Pos) * 0.5f, speed);
    }

    /// <summary>Bodies push apart. The player passes through enemies while dashing.</summary>
    void Separate()
    {
        for (int i = 0; i < Enemies.Count; i++)
        {
            var a = Enemies[i];
            for (int j = i + 1; j < Enemies.Count; j++)
            {
                var b = Enemies[j];
                var d = b.Pos - a.Pos;
                float min = a.Radius + b.Radius, len = d.Length();
                if (len >= min || len < 1e-3f) continue;
                Bowl(a, b); Bowl(b, a);
                var push = d / len * (min - len) * 0.5f;
                a.Pos -= push; b.Pos += push;
            }
            var p = Player;
            if (p.State == PState.Dash || !p.Alive || a.State == EState.Spawn) continue;
            var dp = p.Pos - a.Pos;
            float m = a.Radius + Tune.PlayerRadius, l = dp.Length();
            if (l < m && l > 1e-3f) p.Pos = Collide(a.Pos + dp / l * m, Tune.PlayerRadius, out _);
        }
        foreach (var e in Enemies) e.Pos = Collide(e.Pos, e.Radius, out _); // crowd pushing must not shove into stone
    }
}
