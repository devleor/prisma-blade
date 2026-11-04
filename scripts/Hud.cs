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
        DrawRect(new Rect2(hpRect.Position, new Vector2(hpRect.Size.X * shownHp / p.MaxHp, hpRect.Size.Y)), new Color(1f, 0.85f, 0.7f));
        DrawRect(new Rect2(hpRect.Position, new Vector2(hpRect.Size.X * p.Hp / p.MaxHp, hpRect.Size.Y)), new Color(0.78f, 0.2f, 0.16f));
        if (p.Recover > 0) // health the spin spent: striped, won back by landing hits
        {
            float x0 = hpRect.Size.X * p.Hp / p.MaxHp, wRec = Mathf.Min(hpRect.Size.X - x0, hpRect.Size.X * p.Recover / p.MaxHp);
            DrawRect(new Rect2(hpRect.Position + new Vector2(x0, 0), new Vector2(wRec, hpRect.Size.Y)), new Color(0.55f, 0.75f, 0.4f, 0.55f));
            for (float x = 0; x < wRec; x += 6)
                DrawLine(hpRect.Position + new Vector2(x0 + x, hpRect.Size.Y), hpRect.Position + new Vector2(x0 + Mathf.Min(wRec, x + 4), 0), new Color(0.85f, 1f, 0.7f, 0.6f), 1.5f);
        }
        DrawString(font, new Vector2(28, 58), $"{p.Hp}/{p.MaxHp}", HorizontalAlignment.Left, -1, 15, new Color(1, 1, 1, 0.7f));

        // focus: two charges of Shatter
        var fRect = new Rect2(24, 66, 220, 10);
        DrawRect(fRect.Grow(3), new Color(0, 0, 0, 0.6f));
        bool ready = p.Focus >= Tune.ShatterCost;
        var fc = ready ? new Color(1.25f, 1.1f, 1.6f) : new Color(0.55f, 0.45f, 0.75f);
        DrawRect(new Rect2(fRect.Position, new Vector2(fRect.Size.X * p.Focus / Tune.FocusMax, fRect.Size.Y)), fc);
        DrawLine(fRect.Position + new Vector2(fRect.Size.X / 2, -3), fRect.Position + new Vector2(fRect.Size.X / 2, 13), Colors.Black, 2);
        DrawString(font, new Vector2(252, 77), ready ? "ESTILHAÇAR [Q]" : "foco", HorizontalAlignment.Left, -1, 14,
            ready ? new Color(1.2f, 1.1f, 1.5f) : new Color(1, 1, 1, 0.5f));

        // combo counter: grows a little with each hit, fades when the chain is about to drop
        if (w.Combo >= 2)
        {
            float fade = 1f - Mathf.Clamp((w.ComboT - Tune.ComboDrop * 0.6f) / (Tune.ComboDrop * 0.4f), 0, 1);
            int sizeC = 30 + Mathf.Min(w.Combo, 40) / 2;
            var cc = w.Combo >= 20 ? new Color(1f, 0.6f, 0.25f, fade) : new Color(1f, 0.92f, 0.75f, fade);
            DrawString(font, new Vector2(size.X - 230, 64), $"{w.Combo}", HorizontalAlignment.Right, 130, sizeC, cc);
            DrawString(font, new Vector2(size.X - 94, 64), "HITS", HorizontalAlignment.Left, -1, 18, new Color(1, 1, 1, 0.7f * fade));
        }

        // wave
        if (w.Wave >= 0)
        {
            string wave = w.BossRoom ? "CHEFE" : $"SALA {w.Wave + 1}/{w.Rooms.Count}";
            DrawString(font, new Vector2(0, 36), wave, HorizontalAlignment.Center, size.X, 20, new Color(1, 1, 1, 0.8f));
        }

        // boss bar
        var boss = w.Enemies.FirstOrDefault(e => e.Boss);
        if (boss != null)
        {
            var b = new Rect2(size.X / 2 - 320, size.Y - 54, 640, 14);
            DrawString(font, new Vector2(0, b.Position.Y - 10), "MONÓLITO", HorizontalAlignment.Center, size.X, 18, new Color(0.95f, 0.75f, 0.65f));
            DrawRect(b.Grow(3), new Color(0, 0, 0, 0.7f));
            DrawRect(new Rect2(b.Position, new Vector2(b.Size.X * boss.Hp / boss.MaxHp, b.Size.Y)), new Color(0.7f, 0.15f, 0.2f));
            DrawRect(new Rect2(b.Position + new Vector2(0, b.Size.Y + 3), new Vector2(b.Size.X * boss.Poise / boss.MaxPoise, 3)), new Color(0.95f, 0.85f, 0.4f));
        }

        if (perfectT > 0)
        {
            var c = new Color(1.3f, 1.2f, 1.7f, Mathf.Clamp(perfectT * 2, 0, 1));
            DrawString(font, new Vector2(0, size.Y * 0.32f), "PERFEITO", HorizontalAlignment.Center, size.X, 34, c);
        }

        if (bannerT > 0 || w.Phase is Phase.Lost or Phase.Won)
        {
            float a = w.Phase is Phase.Lost or Phase.Won ? 1f : Mathf.Clamp(bannerT * 2, 0, 1);
            DrawRect(new Rect2(0, size.Y * 0.42f, size.X, 90), new Color(0, 0, 0, 0.45f * a));
            DrawString(font, new Vector2(0, size.Y * 0.42f + 52), banner, HorizontalAlignment.Center, size.X, 44, new Color(1f, 0.93f, 0.8f, a));
            if (sub != "") DrawString(font, new Vector2(0, size.Y * 0.42f + 80), sub, HorizontalAlignment.Center, size.X, 18, new Color(1, 1, 1, 0.8f * a));
        }

        string hint = "WASD mover · mouse mirar · LMB combo · RMB segurar = pesado · Espaço dash (no tempo = PERFEITO) · F giro (custa vida, volta acertando) · Q estilhaçar · R reiniciar";
        DrawString(font, new Vector2(0, size.Y - 12), hint, HorizontalAlignment.Center, size.X, 13, new Color(1, 1, 1, 0.35f));
    }
}
