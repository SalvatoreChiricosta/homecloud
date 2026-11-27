namespace GamepadWeb.Input;

/// <summary>Pulsanti digitali di un controller (layout posizionale: A = in basso, B = a destra, X = a sinistra, Y = in alto).</summary>
[Flags]
public enum GamepadButtons : uint
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    L1 = 1 << 4,
    R1 = 1 << 5,
    L2 = 1 << 6,
    R2 = 1 << 7,
    Share = 1 << 8,
    Options = 1 << 9,
    L3 = 1 << 10,
    R3 = 1 << 11,
    Home = 1 << 12,
    DpadUp = 1 << 13,
    DpadDown = 1 << 14,
    DpadLeft = 1 << 15,
    DpadRight = 1 << 16,
}

/// <summary>
/// Stato istantaneo di un controller. Immutabile: ogni aggiornamento crea un nuovo valore (con "with").
/// Levette: 0..255 con 128 = centro; per gli assi Y il valore cresce verso l'alto.
/// </summary>
public readonly record struct ControllerState(
    GamepadButtons Buttons,
    byte LeftX,
    byte LeftY,
    byte RightX,
    byte RightY,
    byte L2Analog,
    byte R2Analog,
    float AccelX,
    float AccelY,
    float AccelZ,
    float GyroPitch,
    float GyroYaw,
    float GyroRoll)
{
    public static ControllerState Neutral { get; } =
        new(GamepadButtons.None, 128, 128, 128, 128, 0, 0, 0f, 0f, 0f, 0f, 0f, 0f);

    public bool Has(GamepadButtons button) => (Buttons & button) != 0;
}
