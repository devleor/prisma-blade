using Godot;

namespace Prisma;

/// <summary>Input actions, registered in code so keyboard+mouse and gamepad both work without editor setup.</summary>
public static class Controls
{
    public const string Left = "move_left", Right = "move_right", Up = "move_up", Down = "move_down";
    public const string AimLeft = "aim_left", AimRight = "aim_right", AimUp = "aim_up", AimDown = "aim_down";
    public const string Light = "attack_light", Heavy = "attack_heavy", Dash = "dash", Shatter = "shatter", Restart = "restart";

    public static readonly string[] All = [Left, Right, Up, Down, AimLeft, AimRight, AimUp, AimDown, Light, Heavy, Dash, Shatter, Restart];

    static bool registered;

    public static void Register()
    {
        if (registered) return; // Main._Ready runs again on every restart
        registered = true;
        Add(Left, Key.A, Key.Left); Axis(Left, JoyAxis.LeftX, -1);
        Add(Right, Key.D, Key.Right); Axis(Right, JoyAxis.LeftX, 1);
        Add(Up, Key.W, Key.Up); Axis(Up, JoyAxis.LeftY, -1);
        Add(Down, Key.S, Key.Down); Axis(Down, JoyAxis.LeftY, 1);
        Add(AimLeft); Axis(AimLeft, JoyAxis.RightX, -1);
        Add(AimRight); Axis(AimRight, JoyAxis.RightX, 1);
        Add(AimUp); Axis(AimUp, JoyAxis.RightY, -1);
        Add(AimDown); Axis(AimDown, JoyAxis.RightY, 1);
        Add(Light, Key.J); Mouse(Light, MouseButton.Left); Pad(Light, JoyButton.X);
        Add(Heavy, Key.K); Mouse(Heavy, MouseButton.Right); Pad(Heavy, JoyButton.Y);
        Add(Dash, Key.Space, Key.Shift); Pad(Dash, JoyButton.A); Pad(Dash, JoyButton.RightShoulder);
        Add(Shatter, Key.Q, Key.E, Key.L); Pad(Shatter, JoyButton.B); Axis(Shatter, JoyAxis.TriggerRight, 1);
        Add(Restart, Key.R, Key.Enter); Pad(Restart, JoyButton.Start);
    }

    static void Add(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.25f);
        foreach (var k in keys) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
    }

    static void Mouse(string action, MouseButton b) => InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = b });
    static void Pad(string action, JoyButton b) => InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = b, Device = -1 });

    static void Axis(string action, JoyAxis axis, float sign) =>
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = sign, Device = -1 });
}
