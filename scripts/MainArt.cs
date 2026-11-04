using Godot;
using Prisma.Sim;

namespace Prisma;

/// <summary>
/// Everything the scene draws, in a sketchbook style made in code (see Ink): a hooded wanderer with a red cape,
/// each sword with its own silhouette, and creatures instead of shapes. Seen from above, lit from the top left.
///   Spike = Chifrudo (horned boar)       Orb = Olho Errante (floating eye)   Bastion = Golem de Pedra
///   Monolith = Ídolo Monólito (boss)     Imp = Diabrete                      Bomber = Bombardeiro (goblin)
///   Shield = Escudeiro (knight with a kite shield)
/// </summary>
public partial class Main
{
    static readonly Color HeavyCol = new(0.95f, 0.55f, 0.15f);
    static readonly Color DangerCol = new(0.82f, 0.16f, 0.12f);
    static readonly Color MagicCol = new(1.3f, 1.15f, 1.8f);
    static readonly Color CapeCol = new(0.72f, 0.16f, 0.14f);
    static readonly Color TunicCol = new(0.34f, 0.42f, 0.5f);
    static readonly Color SkinCol = new(0.93f, 0.84f, 0.68f);
    static readonly Color Shadow = new(0.1f, 0.07f, 0.05f, 0.22f);

    public static Color KindColor(EKind k) => k switch
    {
        EKind.Spike => new Color(0.8f, 0.47f, 0.22f),
        EKind.Orb => new Color(0.58f, 0.46f, 0.78f),
        EKind.Bastion => new Color(0.52f, 0.6f, 0.4f),
        EKind.Imp => new Color(0.84f, 0.26f, 0.2f),
        EKind.Bomber => new Color(0.5f, 0.62f, 0.36f),
        EKind.Shield => new Color(0.62f, 0.64f, 0.68f),
        _ => new Color(0.6f, 0.52f, 0.5f),
    };

    public static string KindName(EKind k) => k switch
    {
        EKind.Spike => "Chifrudo",
        EKind.Orb => "Olho Errante",
        EKind.Bastion => "Golem",
        EKind.Imp => "Diabrete",
        EKind.Bomber => "Bombardeiro",
        EKind.Shield => "Escudeiro",
        _ => "Monólito",
    };

    static Color SteelOf(WKind k) => k switch
    {
        WKind.Montante => new Color(0.74f, 0.73f, 0.7f),
        WKind.Adagas => new Color(0.86f, 0.87f, 0.9f),
        WKind.Foice => new Color(0.62f, 0.66f, 0.66f),
        WKind.Katana => new Color(0.9f, 0.92f, 0.96f),
        WKind.Runica => new Color(0.38f, 0.38f, 0.48f),
        _ => new Color(0.82f, 0.84f, 0.86f),
    };

    static Color TrailOf(WKind k, bool heavy) => heavy ? new Color(1f, 0.7f, 0.35f) : k switch
    {
        WKind.Runica => new Color(0.75f, 0.6f, 1f),
        WKind.Katana => new Color(0.7f, 0.85f, 1f),
        WKind.Foice => new Color(0.7f, 0.78f, 0.6f),
        WKind.Montante => new Color(1f, 0.8f, 0.55f),
        _ => new Color(1f, 0.97f, 0.9f),
    };

    float stride;
    readonly List<(float angle, float reach, int age, bool heavy)> trail = [];
    readonly List<(Vector2 pos, float facing, float life)> ghosts = [];

    void StepArt()
    {
        var p = World.Player;
        if (World.Hitstop == 0)
        {
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                var t = trail[i];
                if (++t.age > 7) trail.RemoveAt(i); else trail[i] = t;
            }
            if (p.State == PState.Dash && World.Frame % 2 == 0) ghosts.Add((G(p.Pos), Mathf.Atan2(p.Vel.Y, p.Vel.X), 0.25f));
        }
        if (p.BladeActive && (trail.Count == 0 || trail[^1].age > 0 || trail[^1].angle != p.BladeAngle))
            trail.Add((p.BladeAngle, p.BladeReach, 0, p.HeavySwing));
    }

    void AgeGhosts(float dt)
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            var g = ghosts[i];
            g.life -= dt;
            if (g.life <= 0) ghosts.RemoveAt(i); else ghosts[i] = g;
        }
    }

    // ---------------------------------------------------------------- frame

    public override void _Draw()
    {
        float hw = Tune.HalfW, hh = Tune.HalfH;
        DrawGates();
        if (World.PortalOpen) DrawPortal();
        foreach (var e in World.Enemies) DrawShadow(G(e.Pos), e.Radius);
        if (World.Player.Alive) DrawShadow(G(World.Player.Pos), 16);
        foreach (var pl in World.Pillars) DrawShadow(G(pl.Pos) + new Vector2(6, 6), pl.Radius + 4);

        foreach (var a in World.Altars) DrawAltar(a);
        foreach (var b in World.Bombs) DrawBombTarget(b);
        foreach (var e in World.Enemies) DrawTelegraph(e);
        foreach (var e in World.Enemies) DrawEnemy(e);
        foreach (var pl in World.Pillars) DrawPillar(pl);
        foreach (var pr in World.Projs) DrawShot(pr);
        DrawTrail();
        DrawPlayer();
        foreach (var b in World.Bombs) DrawBomb(b);
        if (World.SlowMo > 0) DrawRect(new Rect2(-hw, -hh, hw * 2, hh * 2), new Color(0.3f, 0.35f, 0.6f, 0.1f));
    }

    /// <summary>
    /// Doorways in the walls toward the neighbouring rooms: a dark passage when the gates are open (with an arrow
    /// and the kind of room behind, if seen), an iron portcullis while the room still has a guardian alive.
    /// </summary>
    void DrawGates()
    {
        if (World.Map == null) return;
        const float wall = 44f, half = World.DoorHalf;
        foreach (var s in World.Map.Doors(World.Room))
        {
            var d = G(Sides.Door(s));
            var inward = G(Sides.Inward(s));
            var along = inward.Orthogonal();
            var outer = d - inward * wall;
            Vector2[] gap = [d + along * half, outer + along * half, outer - along * half, d - along * half];
            int seed = 900 + (int)s;
            DrawColoredPolygon(gap, new Color(0.12f, 0.1f, 0.09f));
            Ink.Stroke(this, [d + along * (half + 4), outer + along * (half + 4)], false, 3.5f, seed);
            Ink.Stroke(this, [d - along * (half + 4), outer - along * (half + 4)], false, 3.5f, seed + 1);
            if (World.GatesOpen)
            {
                float bob = Mathf.Sin(World.Frame * 0.12f) * 4f;
                var tip = d + inward * (30 + bob);
                Ink.Stroke(this, [tip + inward * 22 + along * 12, tip, tip + inward * 22 - along * 12], false, 3f, seed + 2, new Color(0.95f, 0.85f, 0.6f, 0.9f));
                var next = World.Map.Neighbor(World.Room, s)!;
                string mark = !next.Seen ? "?" : next.Kind switch { RoomKind.Boss => "鬼", RoomKind.Treasure => "宝", RoomKind.Elite => "★", _ => next.Cleared ? "" : "!" };
                if (mark != "") DrawString(Hud.Kanji, d + inward * 70 - new Vector2(20, -8), mark, HorizontalAlignment.Center, 40, 24, new Color(Ink.Line, 0.8f));
            }
            else
            {
                // portcullis: iron bars across the doorway
                for (int i = -3; i <= 3; i++)
                {
                    var a0 = d + along * (i * half / 3.4f);
                    Ink.Line2(this, a0 - inward * 2, a0 - inward * (wall - 4), 4f, seed + 10 + i, new Color(0.25f, 0.24f, 0.26f));
                }
                Ink.Line2(this, d - inward * 12 + along * half, d - inward * 12 - along * half, 4f, seed + 30, new Color(0.25f, 0.24f, 0.26f));
                Ink.Line2(this, d - inward * 30 + along * half, d - inward * 30 - along * half, 4f, seed + 31, new Color(0.25f, 0.24f, 0.26f));
            }
        }
    }

    void DrawPortal()
    {
        var c = G(World.PortalPos);
        float t = World.Frame * 0.05f;
        DrawColoredPolygon(Ink.Blob(c, World.PortalRadius * 1.2f, World.PortalRadius * 0.9f, 0f, 950, 0.1f, 20), new Color(0.12f, 0.08f, 0.18f));
        for (int i = 0; i < 4; i++)
        {
            float r = World.PortalRadius * (0.35f + 0.22f * i) + Mathf.Sin(t * 2 + i) * 3f;
            DrawArc(c, r, t * (i % 2 == 0 ? 1 : -1.3f) + i, t * (i % 2 == 0 ? 1 : -1.3f) + i + Mathf.Tau * 0.7f, 28, new Color(MagicCol, 0.8f - i * 0.15f), 3f);
        }
        Ink.Stroke(this, Ink.Blob(c, World.PortalRadius * 1.2f, World.PortalRadius * 0.9f, 0f, 951, 0.1f, 20), true, 3f, 951);
        Ink.Write(this, c + new Vector2(0, World.PortalRadius + 30), "desça", 18, Ink.Line, 120);
    }

    void DrawShadow(Vector2 pos, float r)
    {
        var pts = Ink.Blob(pos + new Vector2(5, r * 0.7f), r * 1.05f, r * 0.55f, 0f, 77, 0.05f, 14);
        DrawColoredPolygon(pts, Shadow);
    }

    // ---------------------------------------------------------------- stone

    void DrawPillar(Pillar pl)
    {
        var c = G(pl.Pos);
        int seed = (int)(pl.Pos.X * 3 + pl.Pos.Y);
        var outer = Ink.Circle(c, pl.Radius, seed, 0.06f);
        Ink.Shape(this, outer, new Color(0.66f, 0.6f, 0.52f), seed, 4f, pl.Radius * 0.45f);
        var top = Ink.Circle(c + new Vector2(-2, -3), pl.Radius * 0.66f, seed + 1, 0.05f); // the column's cut top
        Ink.Shape(this, top, new Color(0.76f, 0.71f, 0.62f), seed + 1, 2f);
        Ink.Stroke(this, [c + new Vector2(-pl.Radius * 0.4f, -4), c + new Vector2(-6, 2), c + new Vector2(2, -2), c + new Vector2(10, 8)], false, 1.3f, seed + 2, new Color(Ink.Line, 0.6f));
    }

    void DrawShot(Proj pr)
    {
        var c = G(pr.Pos);
        var col = pr.Friendly ? new Color(1f, 0.92f, 0.7f) : DangerCol;
        Ink.Shape(this, Ink.Circle(c, pr.Radius + 1, 300 + (int)(pr.Life * 10) % 3), col, 300, 2f);
        DrawCircle(c + new Vector2(-1.5f, -1.5f), pr.Radius * 0.35f, new Color(1, 1, 0.95f));
    }

    // ---------------------------------------------------------------- bombs

    void DrawBombTarget(Bomb b)
    {
        var to = G(b.To);
        float k = Mathf.Clamp(b.T / b.Flight, 0, 1);
        var col = b.Friendly ? HeavyCol : DangerCol;
        var ring = Ink.Circle(to, b.Radius, 600, 0.03f);
        Ink.Stroke(this, ring, true, 2f, 600, new Color(col, 0.5f + 0.4f * k), 0.6f);
        var inner = Ink.Circle(to, b.Radius * k, 601, 0.03f);
        if (k > 0.1f) Ink.Hatch(this, inner, 7f, 0.8f, new Color(col, 0.35f), 1.5f, 602);
    }

    void DrawBomb(Bomb b)
    {
        var g = G(b.Ground);
        var at = g - new Vector2(0, b.Height * 120f);
        DrawColoredPolygon(Ink.Blob(g + new Vector2(3, 4), 9, 5, 0, 610, 0.05f, 10), Shadow);
        Ink.Shape(this, Ink.Circle(at, 9, 611), b.Friendly ? new Color(0.35f, 0.25f, 0.15f) : new Color(0.16f, 0.14f, 0.14f), 611, 2f);
        DrawCircle(at + new Vector2(-3, -3), 2.5f, new Color(1, 1, 1, 0.5f));
        var fuse = at + new Vector2(5, -8);
        Ink.Line2(this, at + new Vector2(3, -6), fuse, 1.6f, 612);
        DrawCircle(fuse + Ink.J(613, 0, 1.5f), 3f, new Color(1.6f, 1.1f, 0.4f)); // the spark glows
    }

    // ---------------------------------------------------------------- altars

    void DrawAltar(Altar a)
    {
        var c = G(a.Pos);
        int seed = 700 + (int)a.Pos.X;
        float bob = Mathf.Sin(World.Frame * 0.06f + a.Pos.X) * 3f;
        var stone = Ink.Blob(c + new Vector2(0, 8), Altar.Radius * 1.15f, Altar.Radius * 0.75f, 0, seed, 0.08f, 16);
        Ink.Shape(this, stone, new Color(0.7f, 0.65f, 0.56f), seed, 3f, 6f);
        var top = Ink.Blob(c, Altar.Radius * 0.9f, Altar.Radius * 0.55f, 0, seed + 1, 0.06f, 14);
        Ink.Shape(this, top, new Color(0.8f, 0.76f, 0.67f), seed + 1, 2f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(World.Frame * 0.1f);
        DrawArc(c + new Vector2(0, -18 + bob), 30, 0, Mathf.Tau, 32, new Color(MagicCol, 0.15f + 0.2f * pulse), 4f);
        var icon = c + new Vector2(0, -20 + bob);
        switch (a.Offer.Kind)
        {
            case OfferKind.Weapon:
                var w = Weapon.Of(a.Offer.Weapon);
                var d = Vector2.FromAngle(-Mathf.Pi * 0.75f);
                DrawWeapon(w.Kind, icon - d * 22, d, 50, SteelOf(w.Kind), 1f, seed + 2, 0.6f);
                break;
            case OfferKind.Relic: DrawRelic(a.Offer.Relic, icon, seed + 3); break;
            default: DrawPotion(icon, seed + 4); break;
        }
        Ink.Write(this, c + new Vector2(0, 46), a.Offer.Title, 17, Ink.Line, 220);
        Ink.Write(this, c + new Vector2(0, 64), a.Offer.Blurb, 12, new Color(Ink.Line, 0.8f), 230);
    }

    void DrawPotion(Vector2 c, int seed)
    {
        var flask = Ink.Blob(c + new Vector2(0, 4), 11, 12, 0, seed, 0.04f, 16);
        Ink.Shape(this, flask, new Color(0.8f, 0.15f, 0.2f), seed, 2f, 4f);
        Ink.Shape(this, [c + new Vector2(-4, -14), c + new Vector2(4, -14), c + new Vector2(4, -6), c + new Vector2(-4, -6)], new Color(0.8f, 0.76f, 0.7f), seed + 1, 1.6f);
        DrawCircle(c + new Vector2(-4, 0), 2.5f, new Color(1, 1, 1, 0.7f));
    }

    void DrawRelic(RKind k, Vector2 c, int seed)
    {
        switch (k)
        {
            case RKind.Coracao:
            {
                var pts = new Vector2[20];
                for (int i = 0; i < 20; i++)
                {
                    float t = i * Mathf.Tau / 20;
                    pts[i] = c + new Vector2(16 * Mathf.Pow(Mathf.Sin(t), 3), -(13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t))) * 0.8f;
                }
                Ink.Shape(this, pts, new Color(0.62f, 0.56f, 0.5f), seed, 2f, 4f);
                Ink.Stroke(this, [c + new Vector2(-4, -4), c + new Vector2(0, 2), c + new Vector2(3, -2)], false, 1.2f, seed + 1);
                break;
            }
            case RKind.Afiacao:
                Ink.Shape(this, Ink.Blob(c, 16, 7, -0.4f, seed, 0.05f, 12), new Color(0.55f, 0.6f, 0.65f), seed, 2f, 3f);
                Ink.Line2(this, c + new Vector2(-10, 2), c + new Vector2(10, -6), 1.2f, seed + 1, new Color(1, 1, 1, 0.8f));
                break;
            case RKind.PeLeve:
                for (int i = 0; i < 3; i++)
                {
                    var f = Ink.Blob(c + new Vector2(-4 + i * 5, -6 + i * 5), 14 - i * 2, 4, -0.5f, seed + i, 0.05f, 10);
                    Ink.Shape(this, f, new Color(0.92f, 0.94f, 0.96f), seed + i, 1.6f);
                }
                break;
            case RKind.Brasa:
                Flame(c, 14, seed);
                break;
            case RKind.Sede:
                Ink.Shape(this, [c + new Vector2(-12, -10), c + new Vector2(12, -10), c + new Vector2(6, 2), c + new Vector2(-6, 2)], new Color(0.85f, 0.7f, 0.3f), seed, 2f, 3f);
                Ink.Line2(this, c + new Vector2(0, 2), c + new Vector2(0, 12), 3f, seed + 1);
                Ink.Line2(this, c + new Vector2(-7, 13), c + new Vector2(7, 13), 3f, seed + 2);
                DrawColoredPolygon([c + new Vector2(-9, -8), c + new Vector2(9, -8), c + new Vector2(6, -4), c + new Vector2(-6, -4)], new Color(0.7f, 0.1f, 0.15f));
                break;
            case RKind.Foco:
            {
                var tri = new[] { c + new Vector2(0, -16), c + new Vector2(14, 10), c + new Vector2(-14, 10) };
                Ink.Shape(this, tri, new Color(0.85f, 0.82f, 0.95f), seed, 2f, 3f);
                DrawCircle(c + new Vector2(0, 2), 4, new Color(0.9f, 0.7f, 1.6f));
                break;
            }
            default:
                for (int i = 0; i < 4; i++)
                {
                    float an = -Mathf.Pi / 2 + (i - 1.5f) * 0.5f;
                    var tip = c + Vector2.FromAngle(an) * (16 + (i % 2) * 5);
                    var sd = Vector2.FromAngle(an).Orthogonal() * 4;
                    Ink.Shape(this, [c + new Vector2(0, 8) + sd, tip, c + new Vector2(0, 8) - sd], new Color(0.75f, 0.85f, 1f), seed + i, 1.5f);
                }
                break;
        }
    }

    void Flame(Vector2 c, float s, int seed)
    {
        float lick = Mathf.Sin(World.Frame * 0.3f + seed) * 0.2f;
        Vector2[] outer = [c + new Vector2(0, -s * (1.2f + lick)), c + new Vector2(s * 0.6f, -s * 0.2f), c + new Vector2(s * 0.5f, s * 0.6f), c + new Vector2(-s * 0.5f, s * 0.6f), c + new Vector2(-s * 0.6f, -s * 0.1f)];
        Ink.Shape(this, outer, new Color(0.95f, 0.45f, 0.12f), seed, 1.8f);
        Vector2[] inner = [c + new Vector2(0, -s * 0.5f), c + new Vector2(s * 0.3f, s * 0.2f), c + new Vector2(0, s * 0.5f), c + new Vector2(-s * 0.3f, s * 0.2f)];
        DrawColoredPolygon(inner, new Color(1.5f, 1.2f, 0.5f));
    }

    // ---------------------------------------------------------------- swords

    /// <summary>One sword, from the hilt along dir. scale shrinks the guard and width for icons.</summary>
    void DrawWeapon(WKind kind, Vector2 hilt, Vector2 dir, float len, Color steel, float alpha, int seed, float scale = 1f)
    {
        var side = dir.Orthogonal();
        var ink = new Color(Ink.Line, alpha);
        steel.A *= alpha;
        var leather = new Color(0.45f, 0.3f, 0.18f, alpha);
        switch (kind)
        {
            case WKind.Foice:
            {
                var shaftEnd = hilt + dir * len;
                Ink.Stroke(this, [hilt - dir * 18 * scale, shaftEnd], false, 5f * scale, seed, new Color(0.42f, 0.28f, 0.16f, alpha));
                Ink.Stroke(this, [hilt - dir * 18 * scale, shaftEnd], false, 1.5f * scale, seed + 1, ink);
                var t = shaftEnd; float s = scale;
                Vector2[] blade =
                [
                    t + side * 3 * s, t - side * 14 * s + dir * 5 * s, t - side * 30 * s - dir * 3 * s, t - side * 42 * s - dir * 22 * s,
                    t - side * 26 * s - dir * 11 * s, t - side * 11 * s - dir * 5 * s, t - dir * 6 * s,
                ];
                Ink.Shape(this, blade, steel, seed + 2, 2f * scale, 0f, ink);
                break;
            }
            case WKind.Katana:
            {
                const int n = 7;
                var l = new Vector2[n]; var r = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)(n - 1);
                    var mid = hilt + dir * len * t - side * 7f * t * t * scale; // a gentle curve back
                    float w = Mathf.Lerp(3.6f, 0.6f, t * t) * scale;
                    l[i] = mid + side * w; r[i] = mid - side * w;
                }
                var blade = l.Concat(r.Reverse()).ToArray();
                Ink.Shape(this, blade, steel, seed, 1.8f * scale, 0f, ink);
                Ink.Shape(this, Ink.Circle(hilt, 6.5f * scale, seed + 1), new Color(0.3f, 0.26f, 0.22f, alpha), seed + 1, 1.6f * scale, 0f, ink);
                Ink.Stroke(this, [hilt - dir * 3 * scale, hilt - dir * 20 * scale], false, 5f * scale, seed + 2, new Color(0.2f, 0.18f, 0.22f, alpha));
                for (int i = 0; i < 3; i++) // the wrapped grip
                    DrawLine(hilt - dir * (6 + i * 5) * scale + side * 2.5f * scale, hilt - dir * (8 + i * 5) * scale - side * 2.5f * scale, new Color(0.85f, 0.8f, 0.7f, alpha), 1.2f);
                break;
            }
            default:
            {
                (float w, float guard, float grip) = kind switch
                {
                    WKind.Montante => (9f, 20f, 18f),
                    WKind.Adagas => (4f, 8f, 8f),
                    WKind.Runica => (6f, 13f, 11f),
                    _ => (5f, 13f, 11f),
                };
                w *= scale; guard *= scale; grip *= scale;
                Vector2[] blade =
                [
                    hilt + side * w, hilt + dir * len * 0.84f + side * w * 0.85f, hilt + dir * len,
                    hilt + dir * len * 0.84f - side * w * 0.85f, hilt - side * w,
                ];
                Ink.Shape(this, blade, steel, seed, 2f * scale, 0f, ink);
                if (kind == WKind.Montante)
                    Ink.Stroke(this, [hilt + dir * 10 * scale, hilt + dir * len * 0.72f], false, 1.3f * scale, seed + 3, new Color(Ink.Line, 0.5f * alpha));
                if (kind == WKind.Runica)
                    for (int i = 0; i < 4; i++)
                    {
                        var at = hilt + dir * len * (0.25f + i * 0.15f);
                        DrawLine(at - side * w * 0.5f + dir * 2, at + side * w * 0.5f - dir * 2, new Color(MagicCol, alpha), 1.8f * scale);
                    }
                Ink.Stroke(this, [hilt - side * guard, hilt + side * guard], false, 5f * scale, seed + 1, ink);
                Ink.Stroke(this, [hilt - side * (guard - 1), hilt + side * (guard - 1)], false, 2.5f * scale, seed + 2, new Color(0.7f, 0.55f, 0.3f, alpha));
                if (kind == WKind.Montante)
                {
                    DrawCircle(hilt + side * guard, 3.5f * scale, ink);
                    DrawCircle(hilt - side * guard, 3.5f * scale, ink);
                }
                Ink.Stroke(this, [hilt, hilt - dir * grip], false, 4.5f * scale, seed + 4, leather);
                DrawCircle(hilt - dir * (grip + 2 * scale), 3.2f * scale, ink);
                break;
            }
        }
    }

    void DrawTrail()
    {
        if (trail.Count < 2) return;
        var c0 = G(World.Player.Pos);
        var kind = World.Player.Weapon.Kind;
        var edge = new List<Vector2>();
        for (int i = 0; i < trail.Count - 1; i++)
        {
            var (a0, r0, age0, heavy) = trail[i];
            var (a1, r1, age1, _) = trail[i + 1];
            if (Mathf.Abs(Mathf.AngleDifference(a0, a1)) > 1.6f) continue;
            var col = TrailOf(kind, heavy);
            const int sub = 4;
            for (int s = 0; s < sub; s++)
            {
                float t0 = s / (float)sub, t1 = (s + 1) / (float)sub;
                float aa = Mathf.LerpAngle(a0, a1, t0), ab = Mathf.LerpAngle(a0, a1, t1);
                float ra = Mathf.Lerp(r0, r1, t0), rb = Mathf.Lerp(r0, r1, t1);
                float age = Mathf.Lerp(age0, age1, t0);
                var c = col; c.A = Mathf.Clamp(1f - age / 7f, 0, 1) * 0.55f;
                const float inner = 26f;
                DrawColoredPolygon([c0 + Vector2.FromAngle(aa) * inner, c0 + Vector2.FromAngle(aa) * ra, c0 + Vector2.FromAngle(ab) * rb, c0 + Vector2.FromAngle(ab) * inner], c);
                if (age < 4) edge.Add(c0 + Vector2.FromAngle(aa) * ra);
            }
        }
        if (edge.Count >= 2) Ink.Stroke(this, [.. edge], false, 2f, 400, new Color(Ink.Line, 0.55f), 0.6f); // the brush edge of the swing
    }

    // ---------------------------------------------------------------- the wanderer

    void DrawPlayer()
    {
        var p = World.Player;
        var pos = G(p.Pos);
        foreach (var (gp, gf, life) in ghosts)
        {
            var ghost = Ink.Blob(gp, 14, 11, gf, 50, 0.05f, 12);
            Ink.Stroke(this, ghost, true, 2f, 51, new Color(Ink.Line, life * 2f));
            Ink.Fill(this, ghost, new Color(CapeCol, life * 1.2f), 52);
        }

        bool blink = p.Invuln > 0 && p.State != PState.Dash && World.Frame % 6 < 3;
        float alpha = blink ? 0.45f : 1f;
        bool dead = !p.Alive;
        float facing = p.State == PState.Dash ? Mathf.Atan2(p.Vel.Y, p.Vel.X) : p.Facing;
        var f = Vector2.FromAngle(facing);
        var s = f.Orthogonal();
        var vel = G(p.Vel);

        if (p.State == PState.Charge)
        {
            float k = p.Charge / (float)p.Weapon.ChargeFrames;
            bool full = p.Weapon.FullCharge(p.Charge);
            var c = full ? new Color(1.4f, 1.05f, 0.5f) : HeavyCol;
            c.A = full ? 0.6f + 0.4f * Mathf.Sin(World.Frame * 0.6f) : 0.35f + 0.4f * k;
            Ink.Stroke(this, Ink.Circle(pos, Mathf.Lerp(70, 26, k), 60), true, full ? 4 : 2.5f, 60, c);
        }

        // feet, stepping with the distance walked
        float step = Mathf.Sin(stride * 0.09f) * Mathf.Clamp(vel.Length() / 200f, 0f, 1f) * 7f;
        foreach (int side in new[] { -1, 1 })
        {
            var foot = pos + s * side * 7 + f * (side * step);
            Ink.Shape(this, Ink.Blob(foot, 5, 3.5f, facing, 70 + side, 0.05f, 8), new Color(0.3f, 0.22f, 0.16f, alpha), 70 + side, 1.5f);
        }

        // the cape trails behind and lags when running
        var back = -f;
        var lag = -vel * 0.035f;
        float wave = Mathf.Sin(World.Frame * 0.18f) * 3f;
        Vector2[] cape =
        [
            pos + s * 12 + f * 2, pos + s * 15 + back * 10 + lag * 0.5f, pos + s * 8 + back * 22 + lag + s * wave,
            pos + back * 26 + lag * 1.2f, pos - s * 8 + back * 22 + lag - s * wave, pos - s * 15 + back * 10 + lag * 0.5f, pos - s * 12 + f * 2,
        ];
        var capeCol = dead ? new Color(0.4f, 0.32f, 0.3f) : p.State == PState.Hurt ? new Color(1f, 0.45f, 0.4f) : CapeCol;
        Ink.Shape(this, cape, capeCol, 80, 2.6f, 5f, null, alpha);

        // shoulders and tunic
        var body = Ink.Blob(pos, 9.5f, 13.5f, facing, 81, 0.05f, 14);
        Ink.Shape(this, body, dead ? new Color(0.45f, 0.45f, 0.45f) : TunicCol, 81, 2.4f, 4f, null, alpha);

        // the sword, held in front (the daggers: one in each hand)
        if (p.Alive)
        {
            var dir = Vector2.FromAngle(p.BladeAngle);
            float idle = p.Weapon.Light[0].Reach - 16f;
            float len = (p.State == PState.Attack ? p.BladeReach : idle) - 10f;
            var hilt = pos + dir * 15;
            var steel = p.State == PState.Charge && p.Weapon.FullCharge(p.Charge) ? new Color(1.3f, 1.0f, 0.55f)
                : p.BladeActive && p.HeavySwing ? new Color(1f, 0.82f, 0.5f) : SteelOf(p.Weapon.Kind);
            if (p.EchoReady) steel = new Color(0.9f, 1.1f, 1.6f);
            DrawWeapon(p.Weapon.Kind, hilt, dir, len - 15f, steel, alpha, 90);
            Ink.Shape(this, Ink.Circle(hilt, 4f, 91), SkinCol, 91, 1.5f, 0f, null, alpha); // the hand
            if (p.Weapon.Kind == WKind.Adagas)
            {
                var off = Vector2.FromAngle(facing - 1.1f + Mathf.Sin(World.Frame * 0.2f) * 0.1f);
                var h2 = pos + off * 14 - s * 4;
                DrawWeapon(WKind.Adagas, h2, Vector2.FromAngle(facing - 0.5f), 34, SteelOf(WKind.Adagas), alpha, 92);
                Ink.Shape(this, Ink.Circle(h2, 4f, 93), SkinCol, 93, 1.5f, 0f, null, alpha);
            }
        }

        // the hood: a rounded cowl with its point toward the back and a dark opening toward the face
        var head = pos + f * 2;
        Vector2[] hood =
        [
            head + f * 9, head + f * 6 + s * 7, head + s * 9 - f * 2, head + back * 10 + s * 4, head + back * 15,
            head + back * 10 - s * 4, head - s * 9 - f * 2, head + f * 6 - s * 7,
        ];
        Ink.Shape(this, hood, dead ? new Color(0.55f, 0.52f, 0.5f) : new Color(0.86f, 0.8f, 0.66f), 82, 2.4f, 3.5f, null, alpha);
        var face = Ink.Blob(head + f * 5.5f, 3.2f, 5f, facing, 83, 0.04f, 10);
        Ink.Fill(this, face, new Color(0.2f, 0.15f, 0.12f, alpha), 83);
        if (p.State != PState.Dash && p.Alive)
            DrawCircle(head + f * 6.5f + s * 1.8f, 1.1f, new Color(1f, 0.9f, 0.6f, alpha)); // a glint of eye in the shade

        if (p.Recover > 0 && World.Frame % 20 < 10)
            Ink.Stroke(this, Ink.Circle(pos, 27, 84), true, 1.6f, 84, new Color(0.35f, 0.6f, 0.3f, 0.6f));
    }

    // ---------------------------------------------------------------- creatures

    void DrawTelegraph(Enemy e)
    {
        if (e.State != EState.Telegraph) return;
        var pos = G(e.Pos);
        float pulse = 0.5f + 0.5f * Mathf.Sin(e.T * 0.6f);
        int seed = e.Id * 131 + 5;
        switch (e.Kind)
        {
            case EKind.Spike:
            case EKind.Imp:
            {
                float wind = e.Kind == EKind.Spike ? 32f : 14f, reach = e.Kind == EKind.Spike ? 380f : 140f;
                float k = Mathf.Clamp(e.T / wind, 0, 1);
                var aim = G(e.Aim);
                var end = pos + aim * reach;
                DrawLine(pos, end, new Color(DangerCol, 0.08f + 0.2f * k), 4 + 18 * k * e.Radius / 16f);
                for (float d = 30; d < reach; d += 28) // a dashed ink arrow
                    DrawLine(pos + aim * d, pos + aim * Mathf.Min(reach, d + 14), new Color(DangerCol, 0.5f + 0.4f * k), 2.5f);
                Ink.Stroke(this, [end - aim * 16 + aim.Orthogonal() * 10, end, end - aim * 16 - aim.Orthogonal() * 10], false, 2.5f, seed, new Color(DangerCol, 0.8f));
                break;
            }
            case EKind.Orb:
                Ink.Stroke(this, Ink.Circle(pos, e.Radius + 14 * (e.T / 30f), seed), true, 2f, seed, new Color(KindColor(EKind.Orb), 0.7f));
                break;
            case EKind.Bomber:
                Ink.Stroke(this, Ink.Circle(pos, e.Radius + 10, seed), true, 2f, seed, new Color(DangerCol, 0.4f + 0.4f * pulse));
                break;
            case EKind.Shield:
            {
                var aim = G(e.Aim);
                DrawLine(pos, pos + aim * 120, new Color(DangerCol, 0.25f + 0.3f * pulse), e.Radius * 1.6f);
                break;
            }
            case EKind.Bastion:
            {
                float k = Mathf.Clamp(e.T / 46f, 0, 1);
                var ring = Ink.Circle(pos, World.SlamRadius, seed, 0.03f);
                Ink.Hatch(this, Ink.Circle(pos, World.SlamRadius * k, seed + 1, 0.03f), 9f, 0.8f, new Color(DangerCol, 0.3f), 1.5f, seed + 2);
                Ink.Stroke(this, ring, true, 2.5f, seed, new Color(DangerCol, 0.45f + 0.4f * pulse));
                break;
            }
            case EKind.Monolith:
                if (e.Mode == 0) DrawLine(pos, pos + G(e.Aim) * 900, new Color(DangerCol, 0.2f + 0.25f * pulse), e.Radius * 1.4f);
                else if (e.Mode == 1) Ink.Stroke(this, Ink.Circle(pos, e.Radius + 10 + e.T * 2.5f, seed), true, 3f, seed, new Color(DangerCol, 0.6f));
                else DrawArc(pos, e.Radius + 30, e.T * 0.1f, e.T * 0.1f + Mathf.Tau * 0.75f, 32, KindColor(EKind.Spike), 4);
                break;
        }
        // the "!" every attack shows above the head
        Ink.Write(this, pos + new Vector2(0, -e.Radius - 14), "!", e.Boss ? 34 : 24, DangerCol, 40, false);
    }

    float FacingOf(Enemy e, Vector2 pos)
    {
        if (e.Kind == EKind.Shield && e.Aim != System.Numerics.Vector2.Zero) return G(e.Aim).Angle();
        if (e.State is EState.Act or EState.Telegraph && e.Aim != System.Numerics.Vector2.Zero) return G(e.Aim).Angle();
        if (e.Kind is EKind.Spike or EKind.Imp && e.Vel.LengthSquared() > 400) return G(e.Vel).Angle();
        return (G(World.Player.Pos) - pos).Angle();
    }

    void DrawEnemy(Enemy e)
    {
        var col = KindColor(e.Kind);
        var pos = G(e.Pos);
        float scale = 1f, alpha = 1f;
        int seed = e.Id * 131;
        if (e.State == EState.Spawn)
        {
            float k = Mathf.Clamp(e.T / 40f, 0, 1);
            scale = 0.4f + 0.6f * k; alpha = 0.25f + 0.75f * k;
            Ink.Stroke(this, Ink.Circle(pos, e.Radius * (2.5f - 1.5f * k), seed + 99), true, 1.5f, seed + 99, new Color(Ink.Line, 0.5f * (1 - k)));
        }
        if (e.State == EState.Stun) pos += new Vector2(Mathf.Sin(e.T * 1.7f), Mathf.Cos(e.T * 2.3f)) * 2.5f;
        var fill = e.Flash > 0 ? new Color(1, 1, 1) : col;
        if (e.State == EState.Telegraph) fill = fill.Lerp(DangerCol, 0.15f + 0.2f * Mathf.Sin(e.T * 0.8f));
        if (e.Flash > 0) scale *= 1f + 0.14f * Mathf.Clamp(e.Flash / 6f, 0f, 1f); // a hit pops the body
        float r = e.Radius * scale;
        float a = FacingOf(e, pos);
        var f = Vector2.FromAngle(a);
        var s = f.Orthogonal();
        float w = e.Boss ? 4.5f : e.Elite ? 3.6f : 2.6f;

        if (e.Elite) // a scribbled aura behind the elite
            Ink.Stroke(this, Ink.Circle(pos, r * 1.45f, seed + 98, 0.15f), true, 1.6f, seed + 98, new Color(0.85f, 0.65f, 0.15f, 0.6f * alpha), 2f);

        switch (e.Kind)
        {
            case EKind.Spike: DrawHorned(pos, r, f, s, fill, alpha, w, seed, e); break;
            case EKind.Orb: DrawEye(pos, r, fill, alpha, w, seed, e); break;
            case EKind.Bastion: DrawGolem(pos, r, f, s, fill, alpha, w, seed); break;
            case EKind.Imp: DrawImp(pos, r, f, s, fill, alpha, w, seed, e); break;
            case EKind.Bomber: DrawGoblin(pos, r, f, s, fill, alpha, w, seed, e); break;
            case EKind.Shield: DrawKnight(pos, r, f, s, fill, alpha, w, seed, e); break;
            default: DrawIdol(pos, r, fill, alpha, w, seed, e); break;
        }

        if (e.Elite) // a crown
        {
            var c = pos + new Vector2(0, -r - 10);
            Vector2[] crown = [c + new Vector2(-10, 4), c + new Vector2(-10, -4), c + new Vector2(-5, 0), c + new Vector2(0, -7), c + new Vector2(5, 0), c + new Vector2(10, -4), c + new Vector2(10, 4)];
            Ink.Shape(this, crown, new Color(0.95f, 0.75f, 0.2f), seed + 97, 1.6f, 0f, null, alpha);
        }
        if (e.MaxPoise > 0 && e.Poise < e.MaxPoise)
            DrawArc(pos, r + 8, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * e.Poise / e.MaxPoise, 24, new Color(0.9f, 0.72f, 0.2f, 0.9f), 3);
        if (e.Boss && e.Phase2) Ink.Stroke(this, Ink.Circle(pos, r * 1.6f, seed + 96, 0.1f), true, 2f, seed + 96, new Color(DangerCol, 0.45f));
        if (e.State == EState.Stun) // dazed: doodle stars circling
            for (int i = 0; i < 3; i++)
                Ink.Star(this, pos + new Vector2(0, -r - 10) + Vector2.FromAngle(e.T * 0.15f + i * 2.1f) * new Vector2(15, 5), 5f, new Color(1f, 0.85f, 0.3f), seed + 90 + i);
        if (e.Burn > 0) Flame(pos + new Vector2(r * 0.4f, -r * 0.6f), 7f, seed + 80);

        // fracture: glowing cracks and orbiting shards, one per stack (magic, so HDR)
        for (int i = 0; i < e.Fracture; i++)
        {
            float an = World.Frame * 0.08f + i * Mathf.Tau / e.Fracture;
            var c = pos + Vector2.FromAngle(an) * (r + 14);
            var fs = Vector2.FromAngle(an * 2) * 6;
            DrawColoredPolygon([c + fs, c + fs.Orthogonal() * 0.6f - fs * 0.4f, c - fs.Orthogonal() * 0.6f - fs * 0.4f], MagicCol);
            var crack = Vector2.FromAngle(i * 2.2f + e.Id) * r * 0.9f;
            DrawPolyline([pos - crack, pos - crack * 0.2f + crack.Orthogonal() * 0.2f, pos + crack * 0.6f], MagicCol, 2);
        }

        if (!e.Boss && e.Hp < e.MaxHp && e.State != EState.Spawn)
        {
            var bar = new Rect2(pos + new Vector2(-e.Radius, -e.Radius - 18), new Vector2(e.Radius * 2, 4));
            DrawRect(bar, new Color(Ink.Paper, 0.8f));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * e.Hp / e.MaxHp, 4)), DangerCol);
            DrawRect(bar, Ink.Line, false, 1.2f);
        }
    }

    void DrawHorned(Vector2 pos, float r, Vector2 f, Vector2 s, Color fill, float alpha, float w, int seed, Enemy e)
    {
        float gait = Mathf.Sin(e.T * 0.5f) * Mathf.Clamp(e.Vel.Length() / 100f, 0, 1) * 4f;
        for (int i = 0; i < 4; i++) // stubby legs
        {
            float fs = i < 2 ? 0.5f : -0.5f, ss = i % 2 == 0 ? 1 : -1;
            var hip = pos + f * r * fs + s * ss * r * 0.7f;
            Ink.Line2(this, hip, hip + s * ss * r * 0.35f + f * (gait * (i % 3 == 0 ? 1 : -1)), 3f, seed + 20 + i, new Color(Ink.Line, alpha));
        }
        var body = Ink.Blob(pos, r * 1.15f, r * 0.85f, f.Angle(), seed, 0.1f, 16);
        Ink.Shape(this, body, fill, seed, w, r * 0.35f, null, alpha);
        for (int i = 0; i < 4; i++) // bristles down the back
        {
            var at = pos - f * r * (0.5f - i * 0.25f);
            Ink.Line2(this, at - s * 3, at + s * 3 - f * 4, 1.4f, seed + 30 + i, new Color(Ink.Line, 0.7f * alpha));
        }
        var head = pos + f * r * 0.85f;
        Ink.Shape(this, Ink.Circle(head, r * 0.5f, seed + 1), fill.Darkened(0.12f), seed + 1, w * 0.85f, 0f, null, alpha);
        foreach (int side in new[] { -1, 1 }) // horns sweeping forward
        {
            var root = head + s * side * r * 0.35f;
            Vector2[] horn = [root + f * 3, root + s * side * r * 0.4f + f * r * 0.35f, root + s * side * r * 0.1f + f * r * 0.95f, root - f * 3];
            Ink.Shape(this, horn, new Color(0.95f, 0.9f, 0.78f), seed + 2 + side, 1.8f, 0f, null, alpha);
            DrawCircle(head + f * r * 0.25f + s * side * r * 0.2f, 1.6f, new Color(0.1f, 0.05f, 0.03f, alpha));
        }
    }

    void DrawEye(Vector2 pos, float r, Color fill, float alpha, float w, int seed, Enemy e)
    {
        var look = (G(World.Player.Pos) - pos).Normalized();
        for (int i = 0; i < 4; i++) // wispy tentacles drifting away from the player
        {
            float an = look.Angle() + Mathf.Pi + (i - 1.5f) * 0.45f;
            var pts = new Vector2[5];
            for (int k = 0; k < 5; k++)
            {
                float t = k / 4f;
                pts[k] = pos + Vector2.FromAngle(an) * (r * 0.7f + t * r * 1.3f) + Vector2.FromAngle(an).Orthogonal() * Mathf.Sin(e.T * 0.12f + i + t * 4) * 5 * t;
            }
            Ink.Stroke(this, pts, false, 2.4f * (1.2f - 0.3f * i % 2), seed + 40 + i, new Color(fill.Darkened(0.3f), alpha));
        }
        var ball = Ink.Circle(pos, r, seed, 0.06f);
        Ink.Shape(this, ball, fill, seed, w, r * 0.3f, null, alpha);
        var white = Ink.Circle(pos + look * r * 0.15f, r * 0.62f, seed + 1, 0.04f);
        Ink.Shape(this, white, new Color(0.97f, 0.95f, 0.88f), seed + 1, 1.6f, 0f, null, alpha);
        var iris = pos + look * r * 0.38f;
        Ink.Shape(this, Ink.Circle(iris, r * 0.3f, seed + 2), new Color(0.75f, 0.2f, 0.15f), seed + 2, 1.4f, 0f, null, alpha);
        DrawCircle(iris, r * 0.13f, new Color(0.05f, 0.03f, 0.03f, alpha));
        DrawCircle(iris + new Vector2(-2, -2), 1.5f, new Color(1, 1, 1, alpha));
    }

    void DrawGolem(Vector2 pos, float r, Vector2 f, Vector2 s, Color fill, float alpha, float w, int seed)
    {
        foreach (int side in new[] { -1, 1 }) // big stone fists
        {
            var fist = pos + s * side * r * 1.05f + f * r * 0.35f;
            Ink.Shape(this, Ink.Blob(fist, r * 0.42f, r * 0.36f, f.Angle(), seed + 5 + side, 0.2f, 9), fill.Darkened(0.15f), seed + 5 + side, w * 0.8f, r * 0.12f, null, alpha);
        }
        var body = Ink.Blob(pos, r, r * 0.92f, f.Angle(), seed, 0.22f, 9);
        Ink.Shape(this, body, fill, seed, w, r * 0.35f, null, alpha);
        Ink.Fill(this, Ink.Blob(pos - f * r * 0.2f + s * r * 0.2f, r * 0.45f, r * 0.3f, 0.5f, seed + 3, 0.3f, 10), new Color(0.35f, 0.5f, 0.22f, 0.7f * alpha), seed + 3); // moss
        Ink.Stroke(this, [pos - s * r * 0.5f - f * r * 0.1f, pos - s * r * 0.1f + f * r * 0.1f, pos + s * r * 0.15f - f * r * 0.25f], false, 1.4f, seed + 4, new Color(Ink.Line, 0.7f * alpha));
        foreach (int side in new[] { -1, 1 })
            DrawCircle(pos + f * r * 0.55f + s * side * r * 0.22f, 2.6f, new Color(1.4f, 0.9f, 0.3f, alpha)); // ember eyes
    }

    void DrawImp(Vector2 pos, float r, Vector2 f, Vector2 s, Color fill, float alpha, float w, int seed, Enemy e)
    {
        float flap = Mathf.Sin(e.T * 0.7f) * 0.5f + 0.5f;
        foreach (int side in new[] { -1, 1 }) // bat wings
        {
            var root = pos + s * side * r * 0.5f;
            var tip = root + s * side * r * (1.1f + flap * 0.8f) - f * r * 0.4f;
            Vector2[] wing = [root + f * r * 0.3f, tip, root - f * r * 0.2f + s * side * r * 0.5f, root - f * r * 0.5f];
            Ink.Shape(this, wing, new Color(0.45f, 0.15f, 0.15f), seed + 10 + side, 1.6f, 0f, null, alpha);
        }
        var tail = new Vector2[5];
        for (int k = 0; k < 5; k++) tail[k] = pos - f * (r + k * 4f) + s * Mathf.Sin(e.T * 0.3f + k) * 3f;
        Ink.Stroke(this, tail, false, 1.6f, seed + 12, new Color(Ink.Line, alpha));
        Ink.Shape(this, Ink.Circle(pos, r, seed, 0.06f), fill, seed, w * 0.8f, r * 0.3f, null, alpha);
        foreach (int side in new[] { -1, 1 })
        {
            var root = pos + f * r * 0.4f + s * side * r * 0.45f;
            Ink.Shape(this, [root - s * side * 2, root + f * r * 0.7f + s * side * r * 0.2f, root + s * side * 2.5f], new Color(0.25f, 0.18f, 0.15f), seed + 14 + side, 1.2f, 0f, null, alpha);
            DrawCircle(pos + f * r * 0.45f + s * side * r * 0.25f, 1.6f, new Color(1.4f, 1.2f, 0.4f, alpha));
        }
    }

    void DrawGoblin(Vector2 pos, float r, Vector2 f, Vector2 s, Color fill, float alpha, float w, int seed, Enemy e)
    {
        var bag = pos - f * r * 0.65f;
        Ink.Shape(this, Ink.Blob(bag, r * 0.7f, r * 0.55f, f.Angle(), seed + 6, 0.1f, 10), new Color(0.55f, 0.4f, 0.25f), seed + 6, w * 0.8f, r * 0.2f, null, alpha);
        for (int i = 0; i < 2; i++) DrawCircle(bag + s * (i * 6 - 3) - f * 2, 3.2f, new Color(0.15f, 0.13f, 0.13f, alpha)); // bombs in the satchel
        Ink.Shape(this, Ink.Circle(pos, r, seed, 0.07f), fill, seed, w, r * 0.3f, null, alpha);
        foreach (int side in new[] { -1, 1 }) // long pointed ears
        {
            var root = pos + s * side * r * 0.75f + f * r * 0.1f;
            Ink.Shape(this, [root + f * 4, root + s * side * r * 0.75f - f * r * 0.15f, root - f * 4], fill.Darkened(0.1f), seed + 8 + side, 1.6f, 0f, null, alpha);
            DrawCircle(pos + f * r * 0.55f + s * side * r * 0.25f, 1.8f, new Color(0.1f, 0.05f, 0.03f, alpha));
        }
        if (e.State == EState.Telegraph) // a lit bomb raised to throw
        {
            float k = Mathf.Clamp(e.T / World.BomberWindup, 0, 1);
            var hand = pos + f * r * 0.9f + s * r * 0.6f - f * k * 6;
            Ink.Shape(this, Ink.Circle(hand, 7, seed + 9), new Color(0.15f, 0.13f, 0.13f), seed + 9, 1.6f, 0f, null, alpha);
            DrawCircle(hand + new Vector2(4, -6) + Ink.J(seed, 1, 1.5f), 2.5f, new Color(1.6f, 1.1f, 0.4f));
        }
    }

    void DrawKnight(Vector2 pos, float r, Vector2 f, Vector2 s, Color fill, float alpha, float w, int seed, Enemy e)
    {
        bool down = e.State is EState.Recover or EState.Stun or EState.Spawn;
        Ink.Shape(this, Ink.Blob(pos - f * 3, r * 0.85f, r, f.Angle(), seed + 1, 0.05f, 14), new Color(0.32f, 0.3f, 0.36f), seed + 1, w * 0.8f, r * 0.3f, null, alpha); // tabard
        Ink.Shape(this, Ink.Circle(pos, r * 0.62f, seed, 0.04f), fill, seed, w, r * 0.22f, null, alpha); // helmet
        Ink.Line2(this, pos + f * r * 0.45f - s * r * 0.3f, pos + f * r * 0.45f + s * r * 0.3f, 2f, seed + 2, new Color(Ink.Line, alpha)); // visor slit
        Ink.Line2(this, pos - f * r * 0.5f, pos + f * r * 0.3f, 1.3f, seed + 3, new Color(Ink.Line, 0.6f * alpha)); // the helmet's ridge

        // the kite shield: a thick curved slab across the front, lowered to the side when open
        float centre = down ? f.Angle() + 1.4f : f.Angle();
        float half = down ? 0.6f : World.ShieldCover * 0.8f;
        float rad = r + (down ? 4f : 9f);
        const int n = 9;
        var shield = new Vector2[n * 2];
        for (int i = 0; i < n; i++)
        {
            float an = centre - half + 2 * half * i / (n - 1);
            float bulge = 1f - MathF.Abs(i - (n - 1) / 2f) / ((n - 1) / 2f) * 0.25f;
            shield[i] = pos + Vector2.FromAngle(an) * (rad + 7f * bulge);
            shield[n * 2 - 1 - i] = pos + Vector2.FromAngle(an) * (rad - 2f);
        }
        Ink.Shape(this, shield, new Color(0.55f, 0.3f, 0.2f), seed + 4, w, 3f, null, alpha);
        var boss = pos + Vector2.FromAngle(centre) * (rad + 3);
        Ink.Shape(this, Ink.Circle(boss, 4f, seed + 5), new Color(0.85f, 0.75f, 0.4f), seed + 5, 1.4f, 0f, null, alpha);
    }

    void DrawIdol(Vector2 pos, float r, Color fill, float alpha, float w, int seed, Enemy e)
    {
        float a = World.Frame * 0.01f;
        var ax = Vector2.FromAngle(a); var ay = ax.Orthogonal();
        Vector2[] diamond = [pos + ax * r * 1.3f, pos + ay * r * 0.85f, pos - ax * r * 1.3f, pos - ay * r * 0.85f];
        Ink.Shape(this, diamond, fill, seed, w, r * 0.4f, null, alpha);
        Vector2[] inner = [pos + ax * r * 0.8f, pos + ay * r * 0.5f, pos - ax * r * 0.8f, pos - ay * r * 0.5f];
        Ink.Stroke(this, inner, true, 1.6f, seed + 1, new Color(Ink.Line, 0.6f * alpha));
        var crackCol = e.Phase2 ? new Color(1.5f, 0.4f, 0.3f) : new Color(0.55f, 0.15f, 0.15f);
        Ink.Stroke(this, [pos + ax * r * 0.9f, pos + ax * r * 0.5f + ay * 6, pos + ax * r * 0.3f - ay * 4, pos + ay * 3], false, 2f, seed + 2, crackCol);
        Ink.Stroke(this, [pos - ax * r * 1f, pos - ax * r * 0.6f - ay * 7, pos - ax * r * 0.35f + ay * 2], false, 2f, seed + 3, crackCol);
        // the carved eye opens wide and burns red while it winds up an attack
        bool angry = e.State == EState.Telegraph || e.Phase2;
        var lid = Ink.Blob(pos, r * 0.42f, r * (angry ? 0.26f : 0.14f), a, seed + 4, 0.03f, 14);
        Ink.Shape(this, lid, new Color(0.95f, 0.9f, 0.8f), seed + 4, 2f, 0f, null, alpha);
        var look = (G(World.Player.Pos) - pos).Normalized() * r * 0.15f;
        DrawCircle(pos + look, r * 0.13f, angry ? new Color(1.6f, 0.3f, 0.2f, alpha) : new Color(0.2f, 0.1f, 0.1f, alpha));
    }
}
