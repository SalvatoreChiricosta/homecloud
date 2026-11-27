using System.Diagnostics;

namespace GamepadWeb.Input;

/// <summary>
/// Controller finto sulla porta 1, per verificare il server DSU senza telefono:
/// il tasto A si alterna ogni secondo e la levetta sinistra disegna un cerchio.
/// Attivo solo con Dsu:Demo=true.
/// </summary>
public sealed class DemoInputService(ControllerSlots slots, ILogger<DemoInputService> logger) : BackgroundService
{
    private const int Slot = 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        slots.Connect(Slot);
        logger.LogInformation("Modalità demo: controller finto sulla porta {Port}", Slot + 1);

        var clock = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var t = clock.Elapsed.TotalSeconds;
                var pressA = (int)t % 2 == 0;
                var angle = t * Math.PI; // mezzo giro al secondo

                var state = ControllerState.Neutral with
                {
                    Buttons = pressA ? GamepadButtons.A : GamepadButtons.None,
                    LeftX = (byte)(128 + 100 * Math.Cos(angle)),
                    LeftY = (byte)(128 + 100 * Math.Sin(angle)),
                };
                slots.Update(Slot, state);
            }
        }
        catch (OperationCanceledException)
        {
            // arresto normale
        }
        finally
        {
            slots.Disconnect(Slot);
        }
    }
}
