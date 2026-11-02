using Godot;
using Prisma.Sim;

namespace Prisma;

/// <summary>Screen-space overlay: health, focus (two Shatter charges), wave, boss bar, banners and the controls hint.</summary>
public partial class Hud : Control
{
    readonly Main main;
    string banner = "", sub = "";
    float bannerT, hurtT, perfectT;
    float shownHp = 100f;

    public string Banner => banner;

    public Hud(Main main)
    {
        this.main = main;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(string text, string subtitle = "", float seconds = 2.2f)
    {
        banner = text; sub = subtitle; bannerT = seconds;
    }

    public void Hurt() => hurtT = 0.35f;
    public void Perfect() => perfectT = 0.8f;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bannerT = Mathf.Max(0, bannerT - dt);
        hurtT = Mathf.Max(0, hurtT - dt);
        perfectT = Mathf.Max(0, perfectT - dt);
        shownHp = Mathf.Lerp(shownHp, main.World.Player.Hp, 1 - Mathf.Exp(-6 * dt));
        QueueRedraw();
    }

    public override void _Draw()
    {
        var w = main.World;
        var p = w.Player;
        var size = Size;
        var font = ThemeDB.FallbackFont;

        if (w.SlowMo > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(0.2f, 0.5f, 1f, 0.08f));
        if (hurtT > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(1f, 0.1f, 0.2f, hurtT * 0.5f));
        if (p.Hp < 30 && p.Alive)
        {
            float pulse = 0.12f + 0.08f * Mathf.Sin(Time.GetTicksMsec() / 150f);
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0.8f, 0f, 0.1f, pulse), false, 40f);
        }

        // health
        var hpRect = new Rect2(24, 22, 300, 16);
        DrawRect(hpRect.Grow(3), new Color(0, 0, 0, 0.6f));
        DrawRect(new Rect2(hpRect.Position, new Vector2(hpRect.Size.X * shownHp / p.MaxHp, hpRect.Size.Y)), new Color(1f, 0.85f, 0.85f));
        DrawRect(new Rect2(hpRect.Position, new Vector2(hpRect.Size.X * p.Hp / p.MaxHp, hpRect.Size.Y)), new Color(0.2f, 1.4f, 1.2f));
        DrawString(font, new Vector2(28, 58), $"{p.Hp}/{p.MaxHp}", HorizontalAlignment.Left, -1, 15, new Color(1, 1, 1, 0.7f));

        // focus: two charges of Shatter
        var fRect = new Rect2(24, 66, 220, 10);
        DrawRect(fRect.Grow(3), new Color(0, 0, 0, 0.6f));
        bool ready = p.Focus >= Tune.ShatterCost;
        var fc = ready ? new Color(1.6f, 1.2f, 2.0f) : new Color(0.6f, 0.45f, 0.9f);
        DrawRect(new Rect2(fRect.Position, new Vector2(fRect.Size.X * p.Focus / Tune.FocusMax, fRect.Size.Y)), fc);
        DrawLine(fRect.Position + new Vector2(fRect.Size.X / 2, -3), fRect.Position + new Vector2(fRect.Size.X / 2, 13), Colors.Black, 2);
        DrawString(font, new Vector2(252, 77), ready ? "ESTILHAÇAR [Q]" : "foco", HorizontalAlignment.Left, -1, 14,
            ready ? new Color(1.4f, 1.2f, 1.8f) : new Color(1, 1, 1, 0.5f));

        // wave
        if (w.Wave >= 0)
        {
            string wave = w.Wave >= World.Waves.Length - 1 ? "CHEFE" : $"ONDA {w.Wave + 1}/{World.Waves.Length - 1}";
            DrawString(font, new Vector2(0, 36), wave, HorizontalAlignment.Center, size.X, 20, new Color(1, 1, 1, 0.8f));
        }

        // boss bar
        var boss = w.Enemies.FirstOrDefault(e => e.Boss);
        if (boss != null)
        {
            var b = new Rect2(size.X / 2 - 320, size.Y - 54, 640, 14);
            DrawString(font, new Vector2(0, b.Position.Y - 10), "MONÓLITO", HorizontalAlignment.Center, size.X, 18, new Color(1.5f, 0.5f, 0.7f));
            DrawRect(b.Grow(3), new Color(0, 0, 0, 0.7f));
            DrawRect(new Rect2(b.Position, new Vector2(b.Size.X * boss.Hp / boss.MaxHp, b.Size.Y)), new Color(1.6f, 0.25f, 0.5f));
            DrawRect(new Rect2(b.Position + new Vector2(0, b.Size.Y + 3), new Vector2(b.Size.X * boss.Poise / boss.MaxPoise, 3)), new Color(1.2f, 1.1f, 0.6f));
        }

        if (perfectT > 0)
        {
            var c = new Color(1.6f, 1.8f, 2f, Mathf.Clamp(perfectT * 2, 0, 1));
            DrawString(font, new Vector2(0, size.Y * 0.32f), "PERFEITO", HorizontalAlignment.Center, size.X, 34, c);
        }

        if (bannerT > 0 || w.Phase is Phase.Lost or Phase.Won)
        {
            float a = w.Phase is Phase.Lost or Phase.Won ? 1f : Mathf.Clamp(bannerT * 2, 0, 1);
            DrawRect(new Rect2(0, size.Y * 0.42f, size.X, 90), new Color(0, 0, 0, 0.45f * a));
            DrawString(font, new Vector2(0, size.Y * 0.42f + 52), banner, HorizontalAlignment.Center, size.X, 44, new Color(1.3f, 1.3f, 1.5f, a));
            if (sub != "") DrawString(font, new Vector2(0, size.Y * 0.42f + 80), sub, HorizontalAlignment.Center, size.X, 18, new Color(1, 1, 1, 0.8f * a));
        }

        string hint = "WASD mover · mouse mirar · LMB/J combo · RMB/K segurar = golpe pesado · Espaço dash (no tempo certo = PERFEITO) · Q estilhaçar · R reiniciar";
        DrawString(font, new Vector2(0, size.Y - 12), hint, HorizontalAlignment.Center, size.X, 13, new Color(1, 1, 1, 0.35f));
    }
}
