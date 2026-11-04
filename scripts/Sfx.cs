using Godot;

namespace Prisma;

/// <summary>
/// Sound effects from a pool of players, with a little random pitch so repeats don't sound mechanical, plus one
/// looping music track. Files live in res://assets/audio (CC0, see docs/ASSET_SOURCES.md); a missing file is silent.
/// </summary>
public partial class Sfx : Node
{
    readonly Dictionary<string, AudioStream[]> lib = [];
    readonly AudioStreamPlayer[] pool = new AudioStreamPlayer[20];
    AudioStreamPlayer music = null!;
    string current = "";
    int next;

    public int Loaded { get; private set; }
    public int Played { get; private set; }
    public string CurrentMusic => current;
    public bool MusicPlaying => music.Playing;

    static readonly (string name, string[] files)[] Sounds =
    [
        ("swing", ["swing1", "swing2", "swing3", "swing4"]),
        ("clash", ["clash"]),
        ("deflect", ["deflect"]),
        ("chop", ["chop"]),
        ("stone", ["stone1", "stone2"]),
        ("guard_break", ["guard_break"]),
        ("wall", ["wall"]),
        ("heavy_swing", ["heavy_swing"]),
        ("charge", ["charge"]),
        ("hit", ["hit1", "hit2", "hit3"]),
        ("heavy_hit", ["heavy_hit"]),
        ("dash", ["dash"]),
        ("perfect", ["perfect"]),
        ("shatter", ["shatter"]),
        ("enemy_shoot", ["enemy_shoot"]),
        ("player_hurt", ["player_hurt"]),
        ("enemy_die", ["enemy_die"]),
        ("wave_start", ["wave_start"]),
        ("victory", ["victory"]),
        ("defeat", ["defeat"]),
    ];

    public override void _Ready()
    {
        Settings.Load(); // makes the Music and SFX buses
        foreach (var (name, files) in Sounds)
        {
            var streams = files.Select(f => Find("res://assets/audio/sfx/" + f)).OfType<AudioStream>().ToArray();
            Loaded += streams.Length;
            lib[name] = streams;
        }
        for (int i = 0; i < pool.Length; i++) AddChild(pool[i] = new AudioStreamPlayer { Bus = Settings.SfxBus });
        AddChild(music = new AudioStreamPlayer { VolumeDb = -9f, Bus = Settings.MusicBus });
        music.Finished += () => music.Play(); // loop whatever the format, wav included
    }

    static AudioStream? Find(string basePath)
    {
        foreach (var ext in new[] { ".ogg", ".wav", ".mp3" })
            if (ResourceLoader.Exists(basePath + ext)) return GD.Load<AudioStream>(basePath + ext);
        return null;
    }

    public void Play(string name, float volumeDb = 0f, float pitch = 1f, float vary = 0.07f)
    {
        if (!lib.TryGetValue(name, out var streams) || streams.Length == 0) return;
        var p = pool[next];
        next = (next + 1) % pool.Length;
        p.Stream = streams[Random.Shared.Next(streams.Length)];
        p.VolumeDb = volumeDb;
        p.PitchScale = Mathf.Max(0.1f, pitch * (1f + (float)(Random.Shared.NextDouble() * 2 - 1) * vary));
        p.Play();
        Played++;
    }

    public void Music(string name)
    {
        if (name == current) return;
        current = name;
        var stream = Find("res://assets/audio/music/" + name);
        if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
        if (stream is AudioStreamMP3 mp3) mp3.Loop = true;
        music.Stream = stream;
        if (stream != null) music.Play(); else music.Stop();
    }
}
