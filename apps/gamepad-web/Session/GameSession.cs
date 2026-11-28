using System.Security.Cryptography;
using GamepadWeb.Input;
using Microsoft.Extensions.Options;

namespace GamepadWeb.Session;

public sealed class GameSessionOptions
{
    /// <summary>Codice di partita fisso (comodo in sviluppo). Se vuoto viene generato a caso all'avvio.</summary>
    public string? JoinCode { get; set; }

    /// <summary>Secondi per cui la porta resta riservata a un telefono disconnesso, in attesa che rientri.</summary>
    public int ReconnectGraceSeconds { get; set; } = 30;

    /// <summary>Millisecondi senza input oltre i quali il controller viene riportato a riposo (evita tasti "incastrati").</summary>
    public int InputTimeoutMs { get; set; } = 1000;
}

/// <summary>Esito dell'ingresso di un telefono in partita.</summary>
public sealed record JoinResult(int Slot, string Token, bool Resumed);

/// <summary>
/// Partita in corso: codice d'ingresso, assegnazione delle porte ai telefoni e riconnessione tramite token.
/// Thread-safe: viene usata da tutte le connessioni WebSocket e dal servizio di pulizia.
/// </summary>
public sealed class GameSession
{
    // Alfabeto senza caratteri ambigui (0/O, 1/I) per codici facili da leggere sulla TV
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly ControllerSlots _slots;
    private readonly TimeProvider _time;
    private readonly ILogger<GameSession> _logger;
    private readonly TimeSpan _grace;
    private readonly TimeSpan _inputTimeout;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Reservation> _reservations = new();

    private sealed class Reservation(int slot)
    {
        public int Slot { get; } = slot;
        public int Connections { get; set; }
        public DateTimeOffset? DisconnectedAt { get; set; }
        public DateTimeOffset LastInputAt { get; set; }
        public bool AtRest { get; set; } = true;
    }

    public GameSession(ControllerSlots slots, IOptions<GameSessionOptions> options, TimeProvider time, ILogger<GameSession> logger)
    {
        _slots = slots;
        _time = time;
        _logger = logger;
        _grace = TimeSpan.FromSeconds(Math.Max(0, options.Value.ReconnectGraceSeconds));
        _inputTimeout = TimeSpan.FromMilliseconds(Math.Max(100, options.Value.InputTimeoutMs));
        JoinCode = string.IsNullOrWhiteSpace(options.Value.JoinCode)
            ? NewCode(4)
            : options.Value.JoinCode.Trim().ToUpperInvariant();
    }

    public string JoinCode { get; }

    public bool IsValidCode(string? code) =>
        string.Equals(code?.Trim(), JoinCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fa entrare un telefono. Con un token valido riprende la stessa porta; altrimenti occupa la prima libera.
    /// Restituisce null se le 4 porte sono occupate.
    /// </summary>
    public JoinResult? Join(string? token)
    {
        lock (_lock)
        {
            if (token is not null && _reservations.TryGetValue(token, out var existing))
            {
                existing.Connections++;
                existing.DisconnectedAt = null;
                _logger.LogInformation("Giocatore {Player} rientrato", existing.Slot + 1);
                return new JoinResult(existing.Slot, token, Resumed: true);
            }

            var slot = _slots.ConnectFirstFree();
            if (slot is null) return null;

            var newToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _reservations[newToken] = new Reservation(slot.Value)
            {
                Connections = 1,
                LastInputAt = _time.GetUtcNow(),
            };
            _logger.LogInformation("Nuovo giocatore sulla porta {Player}", slot.Value + 1);
            return new JoinResult(slot.Value, newToken, Resumed: false);
        }
    }

    /// <summary>Aggiorna lo stato del controller associato al token.</summary>
    public void Input(string token, ControllerState state)
    {
        lock (_lock)
        {
            if (!_reservations.TryGetValue(token, out var r)) return;
            r.LastInputAt = _time.GetUtcNow();
            r.AtRest = state == ControllerState.Neutral;
            _slots.Update(r.Slot, state);
        }
    }

    /// <summary>Il telefono si è scollegato: il controller torna a riposo e la porta resta riservata per il tempo di grazia.</summary>
    public void Disconnect(string token)
    {
        lock (_lock)
        {
            if (!_reservations.TryGetValue(token, out var r)) return;
            r.Connections = Math.Max(0, r.Connections - 1);
            if (r.Connections > 0) return;

            r.DisconnectedAt = _time.GetUtcNow();
            r.AtRest = true;
            _slots.Update(r.Slot, ControllerState.Neutral);
            _logger.LogInformation("Giocatore {Player} disconnesso, porta riservata per {Seconds} s", r.Slot + 1, _grace.TotalSeconds);
        }
    }

    /// <summary>
    /// Manutenzione periodica: rimette a riposo i controller senza input recente
    /// e libera le porte dei telefoni che non sono rientrati entro il tempo di grazia.
    /// </summary>
    public void Maintain()
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            foreach (var (token, r) in _reservations.ToArray())
            {
                if (r.DisconnectedAt is { } at)
                {
                    if (now - at < _grace) continue;
                    _reservations.Remove(token);
                    _slots.Disconnect(r.Slot);
                    _logger.LogInformation("Porta {Player} liberata", r.Slot + 1);
                }
                else if (!r.AtRest && now - r.LastInputAt > _inputTimeout)
                {
                    r.AtRest = true;
                    _slots.Update(r.Slot, ControllerState.Neutral);
                    _logger.LogWarning("Nessun input dal giocatore {Player}: controller a riposo", r.Slot + 1);
                }
            }
        }
    }

    private static string NewCode(int length) =>
        string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
}
