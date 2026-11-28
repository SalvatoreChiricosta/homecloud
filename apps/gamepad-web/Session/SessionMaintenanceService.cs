namespace GamepadWeb.Session;

/// <summary>Esegue periodicamente la manutenzione della partita (input scaduti, porte da liberare).</summary>
public sealed class SessionMaintenanceService(GameSession session) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                session.Maintain();
            }
        }
        catch (OperationCanceledException)
        {
            // arresto normale
        }
    }
}
