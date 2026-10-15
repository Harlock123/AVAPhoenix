using Avalonia.Input;

namespace AVAPhoenix.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right, the fire button and the shield.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    public Controls Controls
    {
        get
        {
            int dx = (Any(Key.Right, Key.D, Key.NumPad6, Key.NumPad9, Key.NumPad3) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4, Key.NumPad7, Key.NumPad1) ? 1 : 0);
            if (dx == 0) dx = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            bool fire = Any(Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl) || _pad.ButtonA || _pad.ButtonX;
            bool shield = Any(Key.X, Key.LeftShift, Key.RightShift, Key.Down, Key.S, Key.NumPad2) || _pad.ButtonB || _pad.ButtonY || _pad.LeftStickY > 0.6f || _pad.DPadDown;
            return new Controls(dx, 0, fire, shield);
        }
    }

    public bool Start => Any(Key.Enter) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
