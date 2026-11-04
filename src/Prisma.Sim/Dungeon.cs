using System.Numerics;

namespace Prisma.Sim;

public enum RoomKind { Start, Fight, Elite, Treasure, Boss }

public enum Side { N, E, S, W }

/// <summary>One room of a floor's map. A fight room holds waves and then its guardians, who keep the gates shut.</summary>
public sealed class RoomNode
{
    public int X, Y, Depth, Layout, Seed;
    public RoomKind Kind;
    public EKind[][] Waves = [];
    public EKind[] Guardians = [];
    public bool Cleared, Seen;
    /// <summary>Offers raised here and not yet taken: they wait on their altars if you leave and come back.</summary>
    public Offer[]? Pending;
}

public static class Sides
{
    public static readonly Side[] All = [Side.N, Side.E, Side.S, Side.W];
    public static (int dx, int dy) Step(Side s) => s switch { Side.N => (0, -1), Side.E => (1, 0), Side.S => (0, 1), _ => (-1, 0) };
    public static Side Opposite(Side s) => (Side)(((int)s + 2) % 4);
    /// <summary>The middle of the doorway in that wall.</summary>
    public static Vector2 Door(Side s) => s switch
    {
        Side.N => new(0f, -Tune.HalfH), Side.E => new(Tune.HalfW, 0f), Side.S => new(0f, Tune.HalfH), _ => new(-Tune.HalfW, 0f),
    };
    public static Vector2 Inward(Side s) => -Tune.Norm(Door(s));
}

/// <summary>
/// A floor: rooms on a grid grown from the start room as branches (a tree, no loops), generated from the seed.
/// The deepest dead end is the boss room; other dead ends hold treasure; one deep room is an elite room.
/// </summary>
public sealed class Dungeon(int floor)
{
    public const int Floors = 3;
    public readonly int Floor = floor;
    public readonly Dictionary<(int, int), RoomNode> Rooms = [];
    public RoomNode Start = null!, Boss = null!;

    public RoomNode? Neighbor(RoomNode r, Side s)
    {
        var (dx, dy) = Sides.Step(s);
        return Rooms.GetValueOrDefault((r.X + dx, r.Y + dy));
    }

    public IEnumerable<Side> Doors(RoomNode r) => Sides.All.Where(s => Neighbor(r, s) != null);

    /// <summary>Which enemies may appear from which tier (floor x 3 + depth), and their cost in a wave's budget.</summary>
    static readonly (EKind Kind, int From, float Cost)[] Pool =
    [
        (EKind.Spike, 0, 1f), (EKind.Imp, 0, 0.5f), (EKind.Orb, 1, 1f), (EKind.Bomber, 2, 1.5f),
        (EKind.Bastion, 3, 2.5f), (EKind.Shield, 4, 2f), (EKind.Ogre, 7, 3.5f), (EKind.Brood, 8, 3f),
    ];

    /// <summary>The kinds that can stand as a room's guardian.</summary>
    static readonly EKind[] Leaders = [EKind.Spike, EKind.Orb, EKind.Bomber, EKind.Bastion, EKind.Shield, EKind.Ogre, EKind.Brood];

    /// <summary>Pillar layouts a room can have. Kept clear of the doorways and of the altar spots.</summary>
    public static readonly Pillar[][] Layouts =
    [
        World.PillarLayout,
        [new(new Vector2(-170f, 0f), 40f), new(new Vector2(170f, 0f), 40f)],
        [],
        [.. Enumerable.Range(0, 6).Select(i => new Pillar(Tune.Dir(i * MathF.Tau / 6f) * new Vector2(210f, 200f), 26f))],
        [new(new Vector2(-330f, -120f), 30f), new(new Vector2(0f, 40f), 44f), new(new Vector2(330f, 120f), 30f)],
    ];

    public static int RoomCount(int floor) => 7 + floor;

    public static Dungeon Make(uint seed, int floor)
    {
        var rng = new Rng(seed * 2246822519u + (uint)floor * 3266489917u + 1u);
        var d = new Dungeon(floor);
        var order = new List<RoomNode> { d.Add(0, 0) };
        d.Start = order[0];
        d.Start.Kind = RoomKind.Start;
        int target = RoomCount(floor);
        for (int tries = 0; d.Rooms.Count < target && tries < 5000; tries++)
        {
            var from = order[(int)(rng.Next() % (uint)order.Count)];
            var (dx, dy) = Sides.Step((Side)(rng.Next() % 4));
            int x = from.X + dx, y = from.Y + dy;
            if (d.Rooms.ContainsKey((x, y))) continue;
            int touching = Sides.All.Count(s => { var (a, b) = Sides.Step(s); return d.Rooms.ContainsKey((x + a, y + b)); });
            if (touching > 1) continue; // only its parent: branches and corridors, never a blob
            order.Add(d.Add(x, y));
        }

        // depth by walking out from the start
        var queue = new Queue<RoomNode>([d.Start]);
        var seen = new HashSet<RoomNode> { d.Start };
        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            foreach (var s in d.Doors(r))
            {
                var n = d.Neighbor(r, s)!;
                if (!seen.Add(n)) continue;
                n.Depth = r.Depth + 1;
                queue.Enqueue(n);
            }
        }

        var rooms = order.Skip(1).ToList();
        var leaves = rooms.Where(r => d.Doors(r).Count() == 1).OrderByDescending(r => r.Depth).ThenBy(r => order.IndexOf(r)).ToList();
        d.Boss = leaves.Count > 0 ? leaves[0] : rooms.OrderByDescending(r => r.Depth).First();
        d.Boss.Kind = RoomKind.Boss;
        int treasures = floor == 0 ? 1 : 2;
        foreach (var r in leaves.Skip(1).Take(treasures)) r.Kind = RoomKind.Treasure;
        if (!rooms.Any(r => r.Kind == RoomKind.Treasure))
        {
            var t = rooms.Where(r => r.Kind == RoomKind.Fight && r.Depth >= 1).OrderBy(r => r.Depth).FirstOrDefault();
            if (t != null) t.Kind = RoomKind.Treasure;
        }
        var elite = rooms.Where(r => r.Kind == RoomKind.Fight && r.Depth >= 2).OrderByDescending(r => r.Depth).FirstOrDefault();
        if (elite != null) elite.Kind = RoomKind.Elite;

        foreach (var r in order)
        {
            r.Seed = (int)(rng.Next() & 0x7FFFFFFF);
            r.Layout = r.Kind is RoomKind.Start or RoomKind.Boss ? 0 : (int)(rng.Next() % (uint)Layouts.Length);
            int tier = floor * 3 + r.Depth;
            switch (r.Kind)
            {
                case RoomKind.Fight:
                case RoomKind.Elite:
                {
                    int waves = 2 + (tier >= 5 ? 1 : 0);
                    r.Waves = [.. Enumerable.Range(0, waves).Select(i => Wave(rng, tier, 2.5f + tier * 0.7f + i * 0.5f))];
                    var open = Leaders.Where(k => Pool.Any(p => p.Kind == k && p.From <= tier)).ToArray();
                    int n = r.Kind == RoomKind.Elite ? 2 : 1;
                    r.Guardians = [.. Enumerable.Range(0, n).Select(_ => open[(int)(rng.Next() % (uint)open.Length)])];
                    break;
                }
                case RoomKind.Boss:
                    r.Waves = [Wave(rng, tier, 3f + tier * 0.6f)];
                    r.Guardians = [EKind.Monolith];
                    break;
            }
        }
        return d;
    }

    static EKind[] Wave(Rng rng, int tier, float budget)
    {
        var open = Pool.Where(x => x.From <= tier).ToArray();
        var list = new List<EKind>();
        while (list.Count < 10)
        {
            var fit = open.Where(x => x.Cost <= budget + 0.01f).ToArray();
            if (fit.Length == 0) break;
            var pick = fit[(int)(rng.Next() % (uint)fit.Length)];
            list.Add(pick.Kind);
            budget -= pick.Cost;
        }
        return [.. list];
    }

    RoomNode Add(int x, int y)
    {
        var r = new RoomNode { X = x, Y = y, Kind = RoomKind.Fight };
        Rooms[(x, y)] = r;
        return r;
    }
}

/// <summary>
/// The room flow. Entering an uncleared fight room shuts the gates; its waves come one after another, then its
/// guardians (elites, or the Monolith in the boss room). Killing the last guardian opens the gates and raises
/// the altars. In the boss room it opens the portal to the next floor instead; on the last floor it wins the run.
/// </summary>
public sealed partial class World
{
    public Phase Phase;
    /// <summary>The wave of the current room (-1 before the first; Room.Waves.Length = the guardians).</summary>
    public int Wave = -1, PhaseT;
    public readonly bool AutoWaves;

    public Dungeon Map { get; private set; } = null!;
    public RoomNode Room { get; private set; } = null!;
    public bool GuardianUp, PortalOpen;
    /// <summary>The pedestals standing in a cleared room. Cut one to take its offer.</summary>
    public readonly List<Altar> Altars = [];
    readonly Rng runRng;
    readonly uint runSeed;

    public const float DoorHalf = 56f, PortalRadius = 42f;
    public static readonly Vector2 PortalPos = new(0f, 40f);
    public static readonly Vector2[] AltarSpots = [new(-180f, -190f), new(0f, -215f), new(180f, -190f)];
    public const int RoomClearHeal = 20;

    public bool GatesOpen => Phase == Phase.Explore;
    public bool BossRoom => Room.Kind == RoomKind.Boss;

    public Enemy Spawn(EKind kind, Vector2 pos)
    {
        var e = Enemy.Make(kind, Tune.Clamp(pos, 40f), nextId++);
        e.Pos = Collide(e.Pos, e.Radius, out _);
        e.StrafeSign = Rng.F() < 0.5f ? -1f : 1f;
        Enemies.Add(e);
        return e;
    }

    /// <summary>Starts floor n of the run in its start room.</summary>
    public void GoToFloor(int floor)
    {
        Map = Dungeon.Make(runSeed, floor);
        EnterRoom(Map.Start, null);
    }

    /// <summary>Walks into a room, through the door on side <paramref name="via"/> of the room left (null: placed).</summary>
    public void EnterRoom(RoomNode node, Side? via)
    {
        Room = node;
        node.Seen = true;
        Pillars.Clear();
        Pillars.AddRange(Dungeon.Layouts[node.Layout]);
        Enemies.Clear(); Projs.Clear(); Bombs.Clear(); Altars.Clear(); pending.Clear();
        Wave = -1; GuardianUp = false; PortalOpen = false;
        var p = Player;
        if (via is { } s)
        {
            var at = Sides.Opposite(s);
            p.Pos = Sides.Door(at) + Sides.Inward(at) * 70f;
            p.Vel = Sides.Inward(at) * 150f;
        }
        else { p.Pos = new Vector2(0f, 150f); p.Vel = Vector2.Zero; }

        if (node.Cleared)
        {
            Phase = Phase.Explore;
            PortalOpen = node.Kind == RoomKind.Boss && Map.Floor < Dungeon.Floors - 1;
            if (node.Pending != null) PlaceAltars(node.Pending);
        }
        else if (node.Kind == RoomKind.Start) { node.Cleared = true; Phase = Phase.Explore; }
        else if (node.Kind == RoomKind.Treasure) { node.Cleared = true; Phase = Phase.Explore; RaiseAltars(); }
        else { Phase = Phase.Intermission; PhaseT = 50; }
        Emit(Ev.RoomEnter, p.Pos, (int)node.Kind, Map.Floor);
    }

    void StepWaves()
    {
        if (!AutoWaves || Phase is Phase.Won or Phase.Lost) return;
        switch (Phase)
        {
            case Phase.Intermission:
                if (--PhaseT > 0) return;
                Wave++;
                Phase = Phase.Fighting;
                if (Wave < Room.Waves.Length)
                {
                    foreach (var kind in Room.Waves[Wave]) Spawn(kind, SpawnPoint());
                    Emit(Ev.WaveStart, Vector2.Zero, (int)Room.Kind, Wave);
                }
                else
                {
                    GuardianUp = true;
                    foreach (var kind in Room.Guardians)
                    {
                        var e = Spawn(kind, kind == EKind.Monolith ? new Vector2(0f, -200f) : SpawnPoint());
                        if (kind == EKind.Monolith) e.Hp = e.MaxHp = (int)(e.MaxHp * (1f + 0.35f * Map.Floor));
                        else e.MakeElite();
                        e.Guardian = true;
                    }
                    Emit(Ev.Guardian, Vector2.Zero, Room.Guardians.Length, (int)Room.Guardians[0]);
                }
                return;

            case Phase.Fighting:
                if (Enemies.Count > 0 || ChainRunning) return;
                Projs.RemoveAll(pr => !pr.Friendly); // stale shots must not hit during the pause or after victory
                Bombs.RemoveAll(b => !b.Friendly);
                if (!GuardianUp)
                {
                    Phase = Phase.Intermission;
                    PhaseT = 70;
                    Emit(Ev.WaveClear, Player.Pos, 0f, Wave);
                    return;
                }
                Room.Cleared = true;
                GuardianUp = false;
                Player.Hp = Math.Min(Player.MaxHp, Player.Hp + RoomClearHeal);
                Player.Recover = Math.Min(Player.Recover, Player.MaxHp - Player.Hp);
                if (BossRoom && Map.Floor >= Dungeon.Floors - 1)
                {
                    Phase = Phase.Won;
                    Emit(Ev.Victory, Player.Pos);
                    return;
                }
                Phase = Phase.Explore;
                Emit(Ev.GatesOpen, Player.Pos, RoomClearHeal, (int)Room.Kind);
                if (BossRoom) { PortalOpen = true; Emit(Ev.Portal, PortalPos); }
                else RaiseAltars();
                return;

            case Phase.Explore:
                if (PortalOpen && Vector2.Distance(Player.Pos, PortalPos) < PortalRadius)
                {
                    int next = Map.Floor + 1;
                    GoToFloor(next);
                    Emit(Ev.NextFloor, Player.Pos, 0f, next);
                    return;
                }
                foreach (var s in Map.Doors(Room))
                    if (AtDoor(s)) { EnterRoom(Map.Neighbor(Room, s)!, s); return; }
                return;
        }
    }

    /// <summary>The player is pressed into the open doorway on that side.</summary>
    public bool AtDoor(Side s)
    {
        var p = Player.Pos;
        float edge = Tune.PlayerRadius + 3f;
        return s switch
        {
            Side.N => MathF.Abs(p.X) < DoorHalf && p.Y < -Tune.HalfH + edge,
            Side.S => MathF.Abs(p.X) < DoorHalf && p.Y > Tune.HalfH - edge,
            Side.E => MathF.Abs(p.Y) < DoorHalf && p.X > Tune.HalfW - edge,
            _ => MathF.Abs(p.Y) < DoorHalf && p.X < -Tune.HalfW + edge,
        };
    }

    /// <summary>Three pedestals: a sword you are not holding and two relics (a potion instead of one when hurt).</summary>
    void RaiseAltars()
    {
        Altars.Clear();
        var offers = new List<Offer>();
        var swords = Weapon.All.Where(w => w.Kind != Player.Weapon.Kind).ToArray();
        offers.Add(new Offer(OfferKind.Weapon, swords[(int)(runRng.Next() % (uint)swords.Length)].Kind));
        var relics = Enum.GetValues<RKind>().ToList();
        while (offers.Count < 3)
        {
            var r = relics[(int)(runRng.Next() % (uint)relics.Count)];
            relics.Remove(r);
            offers.Add(new Offer(OfferKind.Relic, Relic: r));
        }
        if (Player.Hp < Player.MaxHp * 0.6f) offers[2] = new Offer(OfferKind.Heal);
        Room.Pending = offers.ToArray();
        PlaceAltars(Room.Pending);
    }

    void PlaceAltars(Offer[] offers)
    {
        Altars.Clear();
        for (int i = 0; i < offers.Length; i++) Altars.Add(new Altar { Pos = AltarSpots[i], Offer = offers[i] });
        Emit(Ev.Altars, Vector2.Zero, 0f, Altars.Count);
    }

    /// <summary>The sword cut altar i: take its offer; the other two crumble.</summary>
    public void TakeAltar(int i)
    {
        if (i < 0 || i >= Altars.Count) return;
        var p = Player;
        var o = Altars[i].Offer;
        var pos = Altars[i].Pos;
        switch (o.Kind)
        {
            case OfferKind.Weapon:
                p.Weapon = Weapon.Of(o.Weapon);
                p.NextCombo = 0; p.ComboTimer = 0; p.EchoReady = false;
                if (!p.HeavySwing) p.Combo = -1; // the swing in progress ends into the new sword's first cut
                break;
            case OfferKind.Relic:
                p.Relics[(int)o.Relic]++;
                if (o.Relic == RKind.Coracao) { p.MaxHp += Relics.CoracaoHp; HealPlayer(Relics.CoracaoHp); }
                break;
            case OfferKind.Heal:
                HealPlayer(Offer.HealAmount);
                break;
        }
        Altars.Clear();
        if (Room != null) Room.Pending = null;
        Emit(Ev.Reward, pos, (int)o.Kind, i);
    }
}
