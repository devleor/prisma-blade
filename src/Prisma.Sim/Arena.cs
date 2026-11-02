using System.Numerics;

namespace Prisma.Sim;

public readonly record struct Pillar(Vector2 Pos, float Radius);

/// <summary>
/// The walls and stone pillars. Bodies are pushed out of them and slide along; an enemy that hits one hard
/// (thrown by a big hit, or charging) is hurt and stunned, so luring a charge into stone is a real tactic.
/// </summary>
public sealed partial class World
{
    public readonly List<Pillar> Pillars = [];

    public static readonly Pillar[] PillarLayout =
    [
        new(new Vector2(-300f, -150f), 34f), new(new Vector2(300f, -150f), 34f),
        new(new Vector2(-300f, 160f), 34f), new(new Vector2(300f, 160f), 34f),
    ];

    /// <summary>Above this speed into a wall, a thrown enemy is hurt by the impact.</summary>
    public const float WallSlamSpeed = 240f; // a combo finisher or heavy near a wall gets there

    /// <summary>A charge must meet stone about head-on to daze; brushing along a wall does nothing.</summary>
    public const float ChargeSlamSpeed = 300f;

    /// <summary>Pushes a circle out of walls and pillars. normal points away from the last surface touched.</summary>
    public Vector2 Collide(Vector2 pos, float r, out Vector2 normal)
    {
        normal = Vector2.Zero;
        var c = Tune.Clamp(pos, r);
        if (c != pos) normal = Tune.Norm(c - pos);
        pos = c;
        foreach (var pl in Pillars)
        {
            var d = pos - pl.Pos;
            float min = r + pl.Radius, len = d.Length();
            if (len >= min) continue;
            var n = len > 1e-3f ? d / len : new Vector2(1f, 0f);
            pos = pl.Pos + n * min;
            normal = n;
        }
        return pos;
    }

    public bool InsidePillar(Vector2 pos, float r) => Pillars.Any(pl => Vector2.Distance(pos, pl.Pos) < pl.Radius + r);

    void MoveEnemy(Enemy e, float dt)
    {
        var pos = Collide(e.Pos + e.Vel * dt, e.Radius, out var n);
        e.Pos = pos;
        if (n == Vector2.Zero) return;
        float into = -Vector2.Dot(e.Vel, n);
        if (into <= 0f) return;
        bool charging = e.State == EState.Act && e.Kind is EKind.Spike or EKind.Monolith && into > ChargeSlamSpeed;
        bool thrown = e.State == EState.Stun && into > WallSlamSpeed;
        e.Vel += n * into * 1.35f; // lose the speed into the wall and bounce off a little
        if (e.WallCd > 0f || !(charging || thrown)) return;
        e.WallCd = 20f;

        if (charging)
        {
            // a charge that ends in stone: long daze, even through the boss's guard
            Damage(e, e.Boss ? 20 : 8, n, 0f, 0f, 0f, byPlayer: false); // the enemy's own doing
            if (!e.Alive) return;
            if (e.Boss) e.Poise = MathF.Max(1f, e.Poise - 45f);
            e.State = EState.Stun; e.T = 0f; e.StunLen = e.Boss ? 70f : 80f;
            Hitstop = Math.Max(Hitstop, 5);
            Emit(Ev.WallHit, e.Pos - n * e.Radius, into, 1);
        }
        else
        {
            float left = e.State == EState.Stun ? e.StunLen - e.T : 0f;
            Damage(e, 6 + (int)(into / 90f), n, 0f, 15f, 40f);
            if (e.State == EState.Stun && left > e.StunLen) { e.T = 0f; e.StunLen = left; } // never cuts a longer daze short
            Hitstop = Math.Max(Hitstop, 3);
            Emit(Ev.WallHit, e.Pos - n * e.Radius, into, 0);
        }
    }

    void MovePlayer()
    {
        var p = Player;
        p.Pos = Collide(p.Pos + p.Vel * Tune.Dt, Tune.PlayerRadius, out var n);
        float into = -Vector2.Dot(p.Vel, n);
        if (n != Vector2.Zero && into > 0f) p.Vel += n * into; // slide along
    }
}
