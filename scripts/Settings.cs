using Godot;

namespace Prisma;

/// <summary>
/// Player settings, kept in user://settings.cfg: three volumes (master, music, effects) on their own audio buses,
/// how hard the screen shakes, and fullscreen. <see cref="Apply"/> pushes them to the engine.
/// </summary>
public static class Settings
{
    public const string MusicBus = "Music", SfxBus = "SFX";
    const string Path = "user://settings.cfg";

    public static float Master = 0.8f, Music = 0.7f, Effects = 0.9f, Shake = 1f;
    public static bool Fullscreen;
    /// <summary>The one-frame ink negative on the biggest blows (some players find flashes uncomfortable).</summary>
    public static bool ImpactFrames = true;
    /// <summary>Off in the automated checks, so a test never overwrites the player's file.</summary>
    public static bool Persist = true;

    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        EnsureBuses();
        var cfg = new ConfigFile();
        if (Persist && cfg.Load(Path) == Error.Ok)
        {
            Master = Unit(cfg, "audio", "master", Master);
            Music = Unit(cfg, "audio", "music", Music);
            Effects = Unit(cfg, "audio", "effects", Effects);
            Shake = Unit(cfg, "video", "shake", Shake);
            var fs = cfg.GetValue("video", "fullscreen", Fullscreen);
            Fullscreen = fs.VariantType == Variant.Type.Bool ? fs.AsBool() : Fullscreen;
            var im = cfg.GetValue("video", "impact_frames", ImpactFrames);
            ImpactFrames = im.VariantType == Variant.Type.Bool ? im.AsBool() : ImpactFrames;
        }
        Apply();
    }

    public static void Save()
    {
        if (!Persist) return;
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", Master);
        cfg.SetValue("audio", "music", Music);
        cfg.SetValue("audio", "effects", Effects);
        cfg.SetValue("video", "shake", Shake);
        cfg.SetValue("video", "fullscreen", Fullscreen);
        cfg.SetValue("video", "impact_frames", ImpactFrames);
        cfg.Save(Path);
    }

    static void EnsureBuses()
    {
        foreach (var name in new[] { MusicBus, SfxBus })
            if (AudioServer.GetBusIndex(name) < 0)
            {
                AudioServer.AddBus();
                int i = AudioServer.BusCount - 1;
                AudioServer.SetBusName(i, name);
                AudioServer.SetBusSend(i, "Master");
            }
    }

    /// <summary>A 0..1 value from the file; a hand-edited or broken entry falls back to the default.</summary>
    static float Unit(ConfigFile cfg, string section, string key, float fallback)
    {
        var v = cfg.GetValue(section, key, fallback);
        float f = v.VariantType switch { Variant.Type.Float => (float)v.AsDouble(), Variant.Type.Int => v.AsInt32(), _ => fallback };
        return float.IsFinite(f) ? Mathf.Clamp(f, 0f, 1f) : fallback;
    }

    static bool? appliedFullscreen;

    static float Db(float v) => v <= 0.001f ? -80f : Mathf.LinearToDb(v);

    public static void Apply()
    {
        EnsureBuses();
        AudioServer.SetBusVolumeDb(0, Db(Master));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(MusicBus), Db(Music));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(SfxBus), Db(Effects));
        if (DisplayServer.GetName() != "headless" && appliedFullscreen != Fullscreen) // leave a maximized window alone
        {
            appliedFullscreen = Fullscreen;
            DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        }
    }
}
