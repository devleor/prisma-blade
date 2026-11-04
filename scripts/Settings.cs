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
            Master = (float)cfg.GetValue("audio", "master", Master);
            Music = (float)cfg.GetValue("audio", "music", Music);
            Effects = (float)cfg.GetValue("audio", "effects", Effects);
            Shake = (float)cfg.GetValue("video", "shake", Shake);
            Fullscreen = (bool)cfg.GetValue("video", "fullscreen", Fullscreen);
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

    static float Db(float v) => v <= 0.001f ? -80f : Mathf.LinearToDb(v);

    public static void Apply()
    {
        EnsureBuses();
        AudioServer.SetBusVolumeDb(0, Db(Master));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(MusicBus), Db(Music));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(SfxBus), Db(Effects));
        if (DisplayServer.GetName() != "headless")
            DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }
}
