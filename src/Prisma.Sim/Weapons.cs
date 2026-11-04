namespace Prisma.Sim;

public enum WKind { Longa, Montante, Adagas, Foice, Katana, Runica, Lanca, Martelo }

/// <summary>
/// A sword: its light chain, how its charged heavy scales, and the one thing it does differently.
/// The Longa is the reference blade (Moves.Light / Moves.Heavy); every other one trades something for its perk.
/// </summary>
public sealed record Weapon(
    WKind Kind, string Name, string Perk, Swing[] Light,
    float HeavyDamage = 1f, float HeavyReach = 0f, int HeavyWindup = 0, float HeavyKnock = 1f, int ChargeFrames = Tune.ChargeMax,
    float FocusGain = 1f, int DashCooldown = Tune.DashCooldown, float WallDamage = 1f, int KillHeal = 0,
    int PerfectBonus = 0, bool Echo = false, bool FractureEveryHit = false)
{
    /// <summary>The heavy slash for this blade at a given charge.</summary>
    public Swing Heavy(int charge)
    {
        var b = Moves.Heavy(charge * Tune.ChargeMax / Math.Max(1, ChargeFrames));
        return b with
        {
            Windup = Math.Max(2, b.Windup + HeavyWindup),
            Reach = b.Reach + HeavyReach,
            Damage = (int)MathF.Round(b.Damage * HeavyDamage),
            Knock = b.Knock * HeavyKnock,
        };
    }

    public bool FullCharge(int charge) => charge >= ChargeFrames;

    public static readonly Weapon Longa = new(WKind.Longa, "Espada Longa", "equilibrada: 3 golpes, o último fratura", Moves.Light);

    public static readonly Weapon Montante = new(WKind.Montante, "Montante", "lenta e enorme; arremessa longe e a pedra dói em dobro",
    [
        new(9, 7, 18, -1.6f, 1.5f, 140f, 22, 330f, 380f, 6, 0, 26),
        new(11, 8, 24, 2.4f, -2.4f, 150f, 34, 460f, 560f, 8, 1, 40),
    ], HeavyDamage: 1.4f, HeavyReach: 30f, HeavyWindup: 4, HeavyKnock: 1.3f, ChargeFrames: 40, DashCooldown: 28, WallDamage: 2f);

    public static readonly Weapon Adagas = new(WKind.Adagas, "Adagas Gêmeas", "5 cortes rápidos, muito foco, dash curto",
    [
        new(3, 4, 8, -1.1f, 1.0f, 80f, 6, 200f, 120f, 2, 0, 6),
        new(3, 4, 8, 1.1f, -1.0f, 80f, 6, 200f, 120f, 2, 0, 6),
        new(3, 4, 8, -1.1f, 1.0f, 82f, 7, 210f, 130f, 2, 0, 7),
        new(3, 4, 8, 1.1f, -1.0f, 82f, 7, 210f, 130f, 2, 0, 7),
        new(5, 5, 16, -1.8f, 1.8f, 92f, 12, 300f, 320f, 4, 1, 14),
    ], HeavyDamage: 0.7f, HeavyReach: -25f, HeavyWindup: -1, HeavyKnock: 0.8f, ChargeFrames: 30, FocusGain: 1.6f, DashCooldown: 14);

    public static readonly Weapon Foice = new(WKind.Foice, "Foice", "arco largo e alcance longo; cada abate cura 3",
    [
        new(7, 6, 16, -1.9f, 1.8f, 140f, 10, 220f, 200f, 3, 0, 10),
        new(7, 6, 16, 1.9f, -1.8f, 140f, 11, 230f, 210f, 3, 0, 11),
        new(10, 8, 22, -1.9f, 1.9f, 150f, 17, 300f, 380f, 5, 1, 18), // was 330° and 165 px: with big waves it beat everything
    ], HeavyReach: 45f, HeavyWindup: 2, KillHeal: 3);

    public static readonly Weapon Katana = new(WKind.Katana, "Katana do Eco", "cortes precisos; dash perfeito mais fácil e o próximo golpe dá dano dobrado",
    [
        new(4, 4, 11, -0.9f, 0.8f, 120f, 15, 300f, 220f, 4, 0, 13),
        new(4, 4, 11, 0.9f, -0.8f, 120f, 16, 310f, 230f, 4, 0, 14),
        new(6, 5, 18, -1.3f, 1.3f, 132f, 24, 480f, 400f, 6, 1, 22),
    ], HeavyDamage: 1.15f, PerfectBonus: 5, Echo: true);

    public static readonly Weapon Runica = new(WKind.Runica, "Lâmina Rúnica", "todo golpe fratura; dano baixo, estilhaços em cadeia",
    [
        new(5, 5, 12, -1.4f, 1.3f, 112f, 9, 250f, 200f, 3, 1, 10),
        new(5, 5, 12, 1.4f, -1.3f, 112f, 9, 260f, 210f, 3, 1, 10),
        new(8, 7, 20, -2.5f, 2.5f, 124f, 14, 400f, 380f, 5, 1, 16),
    ], HeavyDamage: 0.8f, FractureEveryHit: true);

    /// <summary>Spear: narrow thrusts that reach far; the third one goes straight through and staggers.</summary>
    public static readonly Weapon Lanca = new(WKind.Lanca, "Lança", "estocadas longas e estreitas; mantém tudo à distância",
    [
        new(5, 4, 12, -0.3f, 0.25f, 178f, 10, 320f, 300f, 3, 0, 14),
        new(5, 4, 12, 0.3f, -0.25f, 178f, 10, 330f, 300f, 3, 0, 14),
        new(9, 6, 20, -0.4f, 0.4f, 200f, 18, 520f, 520f, 5, 1, 34),
    ], HeavyReach: 70f, HeavyWindup: 2, HeavyKnock: 1.2f, DashCooldown: 24);

    /// <summary>War hammer: two slow blows that crush guards and stun long; stone hurts more.</summary>
    public static readonly Weapon Martelo = new(WKind.Martelo, "Martelo de Guerra", "dois golpes lentos que quebram a guarda e atordoam",
    [
        new(10, 6, 20, -1.5f, 1.2f, 118f, 16, 260f, 420f, 7, 0, 55),
        new(13, 7, 26, 1.8f, -1.6f, 124f, 24, 340f, 600f, 9, 1, 80),
    ], HeavyDamage: 1.3f, HeavyWindup: 3, HeavyKnock: 1.4f, ChargeFrames: 50, DashCooldown: 26, WallDamage: 1.5f);

    public static readonly Weapon[] All = [Longa, Montante, Adagas, Foice, Katana, Runica, Lanca, Martelo];

    public static Weapon Of(WKind k) => All[(int)k];
}

public enum RKind { Coracao, Afiacao, PeLeve, Brasa, Sede, Foco, Espinhos }

/// <summary>Relics stack: every copy adds its effect again.</summary>
public static class Relics
{
    public static string Name(RKind k) => k switch
    {
        RKind.Coracao => "Coração de Pedra",
        RKind.Afiacao => "Pedra de Afiar",
        RKind.PeLeve => "Botas de Vento",
        RKind.Brasa => "Brasa Viva",
        RKind.Sede => "Cálice da Sede",
        RKind.Foco => "Olho de Prisma",
        _ => "Espinhos de Cristal",
    };

    public static string Blurb(RKind k) => k switch
    {
        RKind.Coracao => "+20 de vida máxima",
        RKind.Afiacao => "+10% de dano",
        RKind.PeLeve => "dash recarrega 35% mais rápido",
        RKind.Brasa => "golpes queimam (4 de dano por meio segundo)",
        RKind.Sede => "cada abate cura 4",
        RKind.Foco => "+50% de foco ganho",
        _ => "dash perfeito explode em cristais (25 de dano)",
    };

    public const int CoracaoHp = 20, BurnFrames = 120, BurnTick = 30, BurnDamage = 4, SedeHeal = 4, EspinhosDamage = 25;
    public const float AfiacaoMul = 0.10f, PeLeveMul = 0.65f, PeLeveFloor = 0.4f, FocoMul = 0.5f;
}

public enum OfferKind { Weapon, Relic, Heal }

/// <summary>What an altar holds between rooms.</summary>
public readonly record struct Offer(OfferKind Kind, WKind Weapon = WKind.Longa, RKind Relic = RKind.Coracao)
{
    public const int HealAmount = 40;

    public string Title => Kind switch
    {
        OfferKind.Weapon => Prisma.Sim.Weapon.Of(Weapon).Name,
        OfferKind.Relic => Relics.Name(Relic),
        _ => "Poção Rubra",
    };

    public string Blurb => Kind switch
    {
        OfferKind.Weapon => Prisma.Sim.Weapon.Of(Weapon).Perk,
        OfferKind.Relic => Relics.Blurb(Relic),
        _ => $"+{HealAmount} de vida",
    };
}

/// <summary>A pedestal in the arena holding one offer. Cut it with the sword to take what it holds.</summary>
public sealed class Altar
{
    public System.Numerics.Vector2 Pos;
    public Offer Offer;
    public const float Radius = 26f;
}
