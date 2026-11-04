using System.Numerics;

namespace Prisma.Sim;

/// <summary>
/// Enemy behaviour. Every attack is telegraphed (a pause the view draws as a warning) and followed by a recovery
/// window to punish. ts is the time scale: 1, or less during the slow-mo after a perfect dodge.
///   Spike (triangle): aims, then charges in a straight line.
///   Orb (circle): keeps its distance and fires a 3-shot spread.
///   Bastion (hexagon): armoured, slow, slams the ground around it.
///   Monolith (diamond, boss): charge chains, radial bursts, summons Spikes; faster below half health.
///   Imp: small and fast, comes in numbers, hops at the player.
///   Bomber: keeps away and lobs bombs at where the player is; the landing spot is marked; bombs hurt enemies too.
///   Shield: a shield toward the player stops light cuts from the front; flank it, use the heavy, or shatter it.
/// </summary>
public sealed partial class World
{
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
        if (e.Burn > 0)
        {
            e.Burn--;
            if (++e.BurnT >= Relics.BurnTick)
            {
                e.BurnT = 0;
                e.Hp -= Relics.BurnDamage;
                e.Flash = MathF.Max(e.Flash, 3f);
                Emit(Ev.Burn, e.Pos, Relics.BurnDamage);
                if (e.Hp <= 0) { Kill(e); return; }
            }
        }
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
                    case EKind.Imp: ImpAi(e, dir, dist, dt); break;
                    case EKind.Bomber: BomberAi(e, dir, dist, dt); break;
                    case EKind.Shield: ShieldAi(e, dir, dist, dt); break;
                    case EKind.Ogre: OgreAi(e, dir, dist, dt); break;
                    case EKind.Brood: BroodAi(e, dir, dist, dt); break;
                    case EKind.Spiderling: SpiderlingAi(e, dir, dist, dt); break;
                }
                break;
        }

        if (e.WallCd > 0f) e.WallCd -= ts;
        if (e.BowlCd > 0f) e.BowlCd -= ts;
        var before = e.Pos;
        MoveEnemy(e, dt);
        if (e.Kind is EKind.Ogre or EKind.Brood or EKind.Monolith || e.Elite && e.Radius >= 30f)
        {
            e.Stride += Vector2.Distance(before, e.Pos);
            if (e.Stride >= e.Radius * 2.2f) { e.Stride = 0f; Emit(Ev.Stomp, e.Pos, e.Radius, (int)e.Kind); }
        }
    }

    // ---------------------------------------------------------------- the big ones

    public const float OgreReach = 90f, OgreSlamRadius = 105f;
    public const int OgreWindup = 56, OgreSlamDamage = 22, OgreChargeDamage = 16;

    /// <summary>
    /// Ogre: lumbers in and brings its club down where it points (a circle 90 px ahead, after a long windup it can
    /// still be dodged out of); from far away it sometimes lowers its head and charges.
    /// </summary>
    void OgreAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        switch (e.State)
        {
            case EState.Idle:
                Seek(e, dir * e.Speed, dt);
                if (e.AttackCd <= 0f)
                {
                    if (dist < 170f) Telegraph(e, 0);
                    else if (dist > 320f && Rng.F() < 0.02f) Telegraph(e, 1);
                }
                break;
            case EState.Telegraph:
                Slow(e, 10f, dt);
                if (e.Mode == 0)
                {
                    if (e.T < 22f) e.Aim = dir; // it tracks you, then commits: about half a second to walk out
                    if (e.T >= OgreWindup)
                    {
                        var at = e.Pos + e.Aim * OgreReach;
                        if (Vector2.Distance(Player.Pos, at) < OgreSlamRadius + Tune.PlayerRadius) HurtPlayer(OgreSlamDamage, at, e);
                        Emit(Ev.Slam, at, OgreSlamRadius, (int)EKind.Ogre);
                        Go(e, EState.Recover);
                    }
                }
                else
                {
                    if (e.T < 22f) e.Aim = dir;
                    if (e.T >= 34f) { Go(e, EState.Act); e.HitPlayerThisAct = false; }
                }
                break;
            case EState.Act: // the charge
                e.Vel = e.Aim * 600f;
                Contact(e, OgreChargeDamage);
                if (e.T >= 48f) Go(e, EState.Recover);
                break;
            case EState.Recover:
                Slow(e, 6f, dt);
                if (e.T >= 60f) { Go(e, EState.Idle); e.AttackCd = 45f; }
                break;
        }
    }

    public const int BroodMax = 5, BroodClutch = 2;

    /// <summary>
    /// Brood (spider mother): keeps her distance and lays egg sacs that hatch spiderlings (never more than five at
    /// once); come too close and she bites.
    /// </summary>
    void BroodAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        switch (e.State)
        {
            case EState.Idle:
                var side = new Vector2(-dir.Y, dir.X) * e.StrafeSign;
                Seek(e, (dist < 240f ? -dir : dist > 380f ? dir : side) * e.Speed, dt);
                if (e.AttackCd > 0f) break;
                if (dist < 110f) Telegraph(e, 1);
                else if (Enemies.Count(x => x.Kind == EKind.Spiderling && x.Alive) < BroodMax) Telegraph(e, 0);
                break;
            case EState.Telegraph:
                Slow(e, 9f, dt);
                if (e.Mode == 0)
                {
                    if (e.T >= 40f)
                    {
                        int room = BroodMax - Enemies.Count(x => x.Kind == EKind.Spiderling && x.Alive);
                        int n = Math.Min(BroodClutch, room);
                        var back = e.Pos - dir * (e.Radius + 6f);
                        for (int i = 0; i < n; i++)
                        {
                            var s = Spawn(EKind.Spiderling, back + new Vector2(-dir.Y, dir.X) * (i == 0 ? -14f : 14f));
                            s.T = 26f; // hatches quickly
                        }
                        Emit(Ev.Hatch, back, 0f, n);
                        Go(e, EState.Recover);
                    }
                }
                else
                {
                    if (e.T < 8f) e.Aim = dir;
                    if (e.T >= 14f) { Go(e, EState.Act); e.HitPlayerThisAct = false; }
                }
                break;
            case EState.Act: // the bite
                e.Vel = e.Aim * 420f * MathF.Max(0f, 1f - e.T / 20f);
                Contact(e, 12);
                if (e.T >= 14f) Go(e, EState.Recover);
                break;
            case EState.Recover:
                Slow(e, 7f, dt);
                if (e.T >= 30f) { Go(e, EState.Idle); e.AttackCd = e.Mode == 0 ? 150f : 60f; }
                break;
        }
    }

    /// <summary>Spiderling: a fast little biter that weaves in and nips.</summary>
    void SpiderlingAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        switch (e.State)
        {
            case EState.Idle:
                var side = new Vector2(-dir.Y, dir.X) * e.StrafeSign;
                Seek(e, (dir + side * 0.6f * MathF.Sin(e.T * 0.2f + e.Id)) * e.Speed, dt);
                if (dist < 60f && e.AttackCd <= 0f) { Telegraph(e); e.Aim = dir; }
                break;
            case EState.Telegraph:
                Slow(e, 12f, dt);
                if (e.T >= 8f) { Go(e, EState.Act); e.HitPlayerThisAct = false; }
                break;
            case EState.Act:
                e.Vel = e.Aim * 380f * MathF.Max(0f, 1f - e.T / 14f);
                Contact(e, 4);
                if (e.T >= 12f) Go(e, EState.Recover);
                break;
            case EState.Recover:
                Slow(e, 8f, dt);
                if (e.T >= 16f) { Go(e, EState.Idle); e.AttackCd = 45f; }
                break;
        }
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

    void ImpAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        switch (e.State)
        {
            case EState.Idle:
                var side = new Vector2(-dir.Y, dir.X) * e.StrafeSign;
                float wobble = MathF.Sin(e.T * 0.15f + e.Id);
                Seek(e, (dir * 0.75f + side * (0.5f + 0.4f * wobble)) * e.Speed, dt);
                if (dist < 125f && e.AttackCd <= 0f) Telegraph(e);
                break;
            case EState.Telegraph:
                Slow(e, 12f, dt);
                if (e.T < 10f) e.Aim = dir;
                if (e.T >= 14f) { Go(e, EState.Act); e.HitPlayerThisAct = false; }
                break;
            case EState.Act:
                e.Vel = e.Aim * 640f * (1f - e.T / 30f); // about 130 px: reaches from where it decides to hop
                Contact(e, 6);
                if (e.T >= 18f) Go(e, EState.Recover);
                break;
            case EState.Recover:
                Slow(e, 7f, dt);
                if (e.T >= 24f) { Go(e, EState.Idle); e.AttackCd = Rng.Range(40f, 80f); e.StrafeSign = -e.StrafeSign; }
                break;
        }
    }

    public const int BomberWindup = 34;

    void BomberAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        switch (e.State)
        {
            case EState.Idle:
                var side = new Vector2(-dir.Y, dir.X) * e.StrafeSign;
                var want = dist < 300f ? -dir : dist > 460f ? dir : side;
                Seek(e, want * e.Speed, dt);
                if (e.AttackCd <= 0f && dist < 620f) Telegraph(e);
                break;
            case EState.Telegraph:
                Slow(e, 9f, dt);
                e.Aim = dir;
                if (e.T >= BomberWindup)
                {
                    var lead = Player.Pos + Player.Vel * 0.35f; // aims a little ahead of a moving player
                    int n = e.Elite ? 3 : 1;
                    for (int i = 0; i < n; i++)
                    {
                        var off = i == 0 ? Vector2.Zero : Tune.Dir(Tune.Angle(dir) + (i == 1 ? 1.6f : -1.6f)) * 110f;
                        Bombs.Add(new Bomb { From = e.Pos, To = Tune.Clamp(lead + off, 20f) });
                    }
                    Emit(Ev.Lob, e.Pos, 0f, n);
                    Go(e, EState.Recover);
                }
                break;
            case EState.Recover:
                Slow(e, 6f, dt);
                if (e.T >= 30f) { Go(e, EState.Idle); e.AttackCd = Rng.Range(100f, 150f); e.StrafeSign = -e.StrafeSign; }
                break;
        }
    }

    /// <summary>How far (radians) to either side of its aim the shield covers.</summary>
    public const float ShieldCover = 1.2f;
    const float ShieldTurn = 2.4f; // radians per second: fast footwork beats it

    /// <summary>True when the shield stands between this enemy and a blow coming from <paramref name="from"/>.</summary>
    public static bool Guards(Enemy e, Vector2 from)
    {
        if (e.Kind != EKind.Shield || e.State is EState.Stun or EState.Spawn or EState.Recover) return false;
        var d = from - e.Pos;
        if (d.LengthSquared() < 1e-4f) return true;
        return MathF.Abs(Tune.Wrap(Tune.Angle(d) - Tune.Angle(e.Aim))) < ShieldCover;
    }

    void ShieldAi(Enemy e, Vector2 dir, float dist, float dt)
    {
        if (e.Aim == Vector2.Zero) e.Aim = dir;
        if (e.State is EState.Idle or EState.Telegraph)
        {
            float a = Tune.Angle(e.Aim), want = Tune.Angle(dir);
            float step = Math.Clamp(Tune.Wrap(want - a), -ShieldTurn * dt, ShieldTurn * dt);
            e.Aim = Tune.Dir(a + step);
        }
        switch (e.State)
        {
            case EState.Idle:
                Seek(e, dir * e.Speed * (dist > 90f ? 1f : 0.2f), dt);
                if (dist < 115f && e.AttackCd <= 0f) Telegraph(e);
                break;
            case EState.Telegraph:
                Slow(e, 10f, dt);
                if (e.T >= 26f) { Go(e, EState.Act); e.HitPlayerThisAct = false; }
                break;
            case EState.Act:
                e.Vel = e.Aim * 600f * (1f - e.T / 20f);
                Contact(e, 10);
                if (e.T >= 10f) Go(e, EState.Recover);
                break;
            case EState.Recover: // the bash leaves it open: shield down
                Slow(e, 6f, dt);
                if (e.T >= 40f) { Go(e, EState.Idle); e.AttackCd = Rng.Range(50f, 90f); }
                break;
        }
    }

    public readonly List<Bomb> Bombs = [];

    void StepBombs(float ts)
    {
        for (int i = Bombs.Count - 1; i >= 0; i--)
        {
            var b = Bombs[i];
            b.T += b.Friendly ? 1f : ts;
            if (b.T < b.Flight) continue;
            Bombs.RemoveAt(i);
            Emit(Ev.Explode, b.To, b.Radius, b.Friendly ? 1 : 0);
            Hitstop = Math.Max(Hitstop, 3);
            foreach (var e in Enemies.ToArray())
            {
                if (!e.Hittable || Vector2.Distance(e.Pos, b.To) > b.Radius + e.Radius) continue;
                if (b.Friendly) e.Fracture = Math.Min(Tune.MaxFracture, e.Fracture + 1);
                Damage(e, b.Damage, Tune.Norm(e.Pos - b.To), 380f, 20f, 30f, byPlayer: b.Friendly);
                Emit(Ev.Hit, e.Pos, b.Damage, 1, (int)e.Kind);
            }
            if (!b.Friendly && Player.Alive && Vector2.Distance(Player.Pos, b.To) < b.Radius + Tune.PlayerRadius)
                HurtPlayer(b.Damage, b.To, null);
        }
    }

    /// <summary>The sword bats a bomb back: it flies at the nearest enemy (or straight on) and hits harder.</summary>
    void BatBomb(Bomb b)
    {
        var p = Player;
        var at = b.Ground;
        var target = Enemies.Where(e => e.Hittable).OrderBy(e => Vector2.DistanceSquared(e.Pos, at)).FirstOrDefault();
        b.Friendly = true;
        b.From = at;
        b.To = target?.Pos ?? Tune.Clamp(at + Tune.Dir(p.Facing) * 260f, 20f);
        b.T = 0f;
        b.Flight = 28f;
        b.Damage = 24;
        AddFocus(6f);
        Emit(Ev.Deflect, at);
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
                    Emit(Ev.Hit, e.Pos, pr.Damage, 0, (int)e.Kind);
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
