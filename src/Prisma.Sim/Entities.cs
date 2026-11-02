using System.Numerics;

namespace Prisma.Sim;

/// <summary>What the player pressed this frame. Light, Dash and Shatter are presses; HeavyHeld is the button state.</summary>
public struct Inputs
{
    public Vector2 Move, Aim;
    public bool Light, HeavyHeld, Dash, Shatter;
}

public enum PState { Free, Attack, Charge, Dash, Hurt, Dead }

public enum Buffered { None, Light, Heavy, Dash, Shatter }

public sealed class Player
{
    public Vector2 Pos, Vel;
    public float Facing;
    public PState State;
    public int T;
    public int Hp = 100, MaxHp = 100;
    public int Invuln, DashCd, ComboTimer, NextCombo, Charge;
    public float Focus;

    public Swing Swing;
    public bool HeavySwing;
    public int Combo, SwingId;
    public Vector2 DashDir;
    public bool PerfectUsed, PrevHeavy;

    public Buffered Buffer;
    public int BufferT;

    /// <summary>Where the sword points (world angle) and whether its edge can cut this frame. For drawing and hits.</summary>
    public float BladeAngle, BladeReach = 100f;
    public bool BladeActive;

    public bool Alive => State != PState.Dead;
    public bool Invulnerable => Invuln > 0 || (State == PState.Dash && T <= Tune.DashIFrames);
}

public enum EKind { Spike, Orb, Bastion, Monolith }

public enum EState { Spawn, Idle, Telegraph, Act, Recover, Stun, Dead }

public sealed class Enemy
{
    public int Id;
    public EKind Kind;
    public Vector2 Pos, Vel, Aim;
    public float Radius, Speed;
    public int Hp, MaxHp;
    public float Poise, MaxPoise, PoiseIdle;
    public EState State = EState.Spawn;
    public float T, AttackCd, StunLen, Flash;
    public int Fracture, LastSwing = -1, BurstCast = -1;
    public bool HitPlayerThisAct;
    public int Mode, Sub, Pattern;
    public float StrafeSign = 1f;

    public bool Alive => State != EState.Dead && Hp > 0;
    public bool Hittable => Alive && State != EState.Spawn;
    public bool Boss => Kind == EKind.Monolith;
    public bool Phase2 => Boss && Hp * 2 < MaxHp;

    public static Enemy Make(EKind kind, Vector2 pos, int id)
    {
        var e = new Enemy { Id = id, Kind = kind, Pos = pos };
        (e.Hp, e.Radius, e.Speed, e.MaxPoise) = kind switch
        {
            EKind.Spike => (40, 16f, 150f, 0f),
            EKind.Orb => (30, 15f, 120f, 0f),
            EKind.Bastion => (130, 28f, 85f, 40f),
            _ => (700, 46f, 70f, 90f),
        };
        e.MaxHp = e.Hp;
        e.Poise = e.MaxPoise;
        e.AttackCd = 30f;
        return e;
    }
}

public sealed class Proj
{
    public Vector2 Pos, Vel;
    public float Life, Radius = 7f;
    public int Damage;
    public bool Friendly; // deflected by the sword: now hurts enemies
}

public enum Ev
{
    Swing, Hit, Kill, Dash, Perfect, PlayerHurt, Shoot, Deflect, ShatterCast, Shatter,
    Telegraph, Slam, ChargeFull, WaveStart, WaveClear, Victory, Defeat, Charge,
}

/// <summary>Something the view should show or play this frame. A is a magnitude, I a kind/index.</summary>
public readonly record struct GameEvent(Ev Kind, Vector2 Pos, float A = 0f, int I = 0);

public enum Phase { Intermission, Fighting, Won, Lost }
