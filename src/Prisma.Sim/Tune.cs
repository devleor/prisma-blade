using System.Numerics;

namespace Prisma.Sim;

/// <summary>All the numbers that decide how the game feels. Per-second units unless the name says frames (60 Hz).</summary>
public static class Tune
{
    public const float Dt = 1f / 60f;
    public const float HalfW = 760f, HalfH = 440f;

    // movement: velocity eases toward the target, so starts and stops have a little inertia
    public const float MoveSpeed = 330f, MoveSharp = 12f, AttackSteer = 0.12f, ChargeSpeedMul = 0.35f;
    public const float PlayerRadius = 17f;

    // dash
    public const int DashFrames = 10, DashTail = 5, DashIFrames = 12, PerfectFrames = 8, DashCooldown = 22;
    public const float DashSpeed = 1000f;

    // input buffer and combo timing
    public const int BufferFrames = 10, ComboWindow = 22, CancelDelay = 3;

    // heavy charge, focus and the Shatter finisher
    public const int ChargeMax = 45;
    public const float FocusMax = 100f, ShatterCost = 50f;
    public const int MaxFracture = 3;
    public const float ShatterChainRadius = 165f;
    public const int ShatterChainDelay = 6;

    // perfect dodge: dash through an attack in the first frames of the dash
    public const int PerfectSlowFrames = 45;
    public const float SlowTimeScale = 0.3f, PerfectMarkRadius = 260f;

    // player damage
    public const int HurtFrames = 14, HurtInvuln = 45;

    public static float Wrap(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    public static Vector2 Dir(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    public static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X);
    public static Vector2 Norm(Vector2 v) => v.LengthSquared() > 1e-6f ? Vector2.Normalize(v) : Vector2.Zero;

    public static Vector2 Clamp(Vector2 p, float r) =>
        new(Math.Clamp(p.X, -HalfW + r, HalfW - r), Math.Clamp(p.Y, -HalfH + r, HalfH - r));
}

/// <summary>One sword swing, in frames. Arc angles are relative to the facing at the start of the swing.</summary>
public readonly record struct Swing(
    int Windup, int Active, int Recovery,
    float ArcStart, float ArcEnd, float Reach,
    int Damage, float Lunge, float Knock, int Hitstop, int Fracture, int Poise)
{
    public int Total => Windup + Active + Recovery;
}

public static class Moves
{
    /// <summary>Light chain: right-to-left, left-to-right, then a wide finishing sweep that fractures.</summary>
    public static readonly Swing[] Light =
    [
        new(5, 5, 12, -1.4f, 1.3f, 108f, 12, 270f, 230f, 3, 0, 12),
        new(5, 5, 12, 1.4f, -1.3f, 108f, 13, 290f, 250f, 3, 0, 13),
        new(8, 7, 20, -2.5f, 2.5f, 128f, 20, 440f, 440f, 5, 1, 22),
    ];

    /// <summary>Heavy slash, scaled by how long it was charged. A full charge adds a second fracture stack.</summary>
    public static Swing Heavy(int charge)
    {
        float c = Math.Clamp(charge / (float)Tune.ChargeMax, 0f, 1f);
        bool full = charge >= Tune.ChargeMax;
        return new(4, 8, 24, -2.1f, 2.1f, 125f + 40f * c, (int)(24 + 38 * c), 380f + 340f * c,
            420f + 420f * c, full ? 9 : 6, full ? 2 : 1, (int)(35 + 55 * c));
    }
}

/// <summary>Small deterministic xorshift generator, so a run replays the same from the same seed.</summary>
public sealed class Rng(uint seed)
{
    uint s = seed == 0 ? 1u : seed;

    public uint Next()
    {
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return s;
    }

    public float F() => (Next() & 0xFFFFFF) / 16777216f;
    public float Range(float a, float b) => a + (b - a) * F();
}
