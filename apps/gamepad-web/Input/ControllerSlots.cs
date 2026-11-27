namespace GamepadWeb.Input;

/// <summary>
/// Le 4 porte dei controller (come le 4 porte del GameCube). Una porta vuota vale null.
/// Accesso thread-safe: la scrivono i telefoni (WebSocket) e la legge il server DSU.
/// </summary>
public sealed class ControllerSlots
{
    public const int Count = 4;

    private readonly ControllerState?[] _slots = new ControllerState?[Count];
    private readonly Lock _lock = new();

    public bool IsConnected(int slot)
    {
        lock (_lock) return _slots[slot] is not null;
    }

    /// <summary>Occupa la prima porta libera e restituisce il suo numero (null se sono tutte occupate).</summary>
    public int? ConnectFirstFree()
    {
        lock (_lock)
        {
            for (var i = 0; i < Count; i++)
            {
                if (_slots[i] is null)
                {
                    _slots[i] = ControllerState.Neutral;
                    return i;
                }
            }
            return null;
        }
    }

    public void Connect(int slot)
    {
        lock (_lock) _slots[slot] ??= ControllerState.Neutral;
    }

    public void Disconnect(int slot)
    {
        lock (_lock) _slots[slot] = null;
    }

    /// <summary>Aggiorna lo stato solo se la porta è occupata (evita di "riaprire" una porta appena liberata).</summary>
    public void Update(int slot, ControllerState state)
    {
        lock (_lock)
        {
            if (_slots[slot] is not null) _slots[slot] = state;
        }
    }

    public ControllerState?[] Snapshot()
    {
        lock (_lock) return (ControllerState?[])_slots.Clone();
    }
}
