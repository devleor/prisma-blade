using System.Numerics;

namespace Prisma.Sim;

/// <summary>What the player pressed this frame. Light, Dash and Shatter are presses; HeavyHeld is the button state.</summary>
public struct Inputs
{
    public Vector2 Move, Aim;
    public bool Light, HeavyHeld, Dash, Shatter, Special;
}

public enum PState { Free, Attack, Charge, Dash, Hurt, Dead }

public enum Buffered { None, Light, Heavy, Dash, Shatter, Special }

public sealed class Player
{
    public Vector2 Pos, Vel;
    public float Facing;
    public PState State;
    public int T;
    public int Hp = 100, MaxHp = 100;
    public int Invuln, DashCd, ComboTimer, NextCombo, Charge;
    /// <summary>Health spent on the spin special; landing hits wins it back, getting hit loses it.</summary>
    public int Recover, SpinCd;
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

    /// <summary>The sword in hand and the relics picked up this run (a count per kind: they stack).</summary>
    public Weapon Weapon = Weapon.Longa;
    public readonly int[] Relics = new int[Enum.GetValues<RKind>().Length];
    /// <summary>Katana: a perfect dodge charges the next landed hit to deal double.</summary>
    public bool EchoReady;

    public int Has(RKind k) => Relics[(int)k];
    public int PerfectWindow => Tune.PerfectFrames + Weapon.PerfectBonus;
    public float DamageMul => 1f + Prisma.Sim.Relics.AfiacaoMul * Has(RKind.Afiacao);
    public float FocusMul => Weapon.FocusGain * (1f + Prisma.Sim.Relics.FocoMul * Has(RKind.Foco));
    public int DashCooldown => Math.Max(6, (int)MathF.Round(Weapon.DashCooldown * MathF.Pow(Prisma.Sim.Relics.PeLeveMul, Has(RKind.PeLeve))));

    public bool Alive => State != PState.Dead;
    public bool Invulnerable => Invuln > 0 || (State == PState.Dash && T <= Tune.DashIFrames);
}

public enum EKind { Spike, Orb, Bastion, Monolith, Imp, Bomber, Shield }

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
    public float T, AttackCd, StunLen, Flash, WallCd, BowlCd;
    public int Fracture, LastSwing = -1, BurstCast = -1;
    public bool HitPlayerThisAct;
    public int Mode, Sub, Pattern;
    public float StrafeSign = 1f;
    /// <summary>Frames of burning left (Brasa relic) and the frame counter toward the next tick.</summary>
    public int Burn, BurnT;
    /// <summary>Elite: a bigger, tougher version that leads a room.</summary>
    public bool Elite;
    /// <summary>Guards the room: the gates open when the last guardian falls.</summary>
    public bool Guardian;

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
            EKind.Imp => (16, 11f, 210f, 0f),
            EKind.Bomber => (45, 18f, 95f, 0f),
            EKind.Shield => (70, 20f, 105f, 25f),
            _ => (700, 46f, 70f, 90f),
        };
        e.MaxHp = e.Hp;
        e.Poise = e.MaxPoise;
        e.AttackCd = 30f;
        return e;
    }

    /// <summary>Elite: 2.2x health, a third bigger, a little faster, and a guard even if the base kind has none.</summary>
    public void MakeElite()
    {
        if (Elite || Boss) return;
        Elite = true;
        Hp = MaxHp = (int)(MaxHp * 2.2f);
        Radius *= 1.3f;
        Speed *= 1.1f;
        MaxPoise = Poise = MathF.Max(MaxPoise * 1.5f, 40f);
    }
}



/// <summary>
/// A bomb lobbed in an arc from From to To over Flight frames; it bursts on landing and hurts everyone in Radius.
/// The sword can bat one back mid-air: then it flies at the nearest enemy and only hurts enemies.
/// </summary>
public sealed class Bomb
{
    public Vector2 From, To;
    public float T, Flight = 46f, Radius = 75f;
    public int Damage = 14;
    public bool Friendly;
    public Vector2 Ground => Vector2.Lerp(From, To, Math.Clamp(T / Flight, 0f, 1f));
    /// <summary>Height above the ground, 0..1, for drawing the arc.</summary>
    public float Height => 4f * (T / Flight) * (1f - T / Flight);
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
    Telegraph, Slam, ChargeFull, WaveStart, WaveClear, Victory, Defeat, Charge, WallHit, Bowl,
    /// <summary>Altars rose after a room (I = how many). Then Reward: one was taken (I = its index), Burn: a burn tick.</summary>
    Altars, Reward, Burn, Heal,
    /// <summary>Block: a shield turned a cut aside. Lob: a bomb was thrown. Explode: one burst (A = radius, I = 1 if batted back).</summary>
    Block, Lob, Explode,
    /// <summary>RoomEnter (A = RoomKind, I = floor), Guardian rose (A = how many, I = kind), GatesOpen, Portal, NextFloor (I = floor).</summary>
    RoomEnter, Guardian, GatesOpen, Portal, NextFloor,
}

/// <summary>Something the view should show or play this frame. A is a magnitude, I a kind/index.</summary>
public readonly record struct GameEvent(Ev Kind, Vector2 Pos, float A = 0f, int I = 0);

/// <summary>Intermission: the pause before a wave. Explore: the room is clear, its gates open (and its altars up).</summary>
public enum Phase { Intermission, Fighting, Explore, Won, Lost }
