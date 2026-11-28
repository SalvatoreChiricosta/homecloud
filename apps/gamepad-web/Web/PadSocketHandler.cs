using System.Net.WebSockets;
using System.Text.Json;
using GamepadWeb.Input;
using GamepadWeb.Session;

namespace GamepadWeb.Web;

/// <summary>
/// Endpoint WebSocket della pagina gamepad.
///
/// Protocollo (messaggi JSON di testo):
///   server → telefono: {"t":"welcome","player":2,"color":"#1e88e5","token":"…","resumed":false}
///                      {"t":"full"} · {"t":"badcode"}
///   telefono → server: {"t":"s","b":5,"lx":128,"ly":200,"rx":128,"ry":128,"l":0,"r":255}
///     b = bitmask dei pulsanti (stessi bit di GamepadButtons), levette e grilletti 0..255.
/// Il telefono reinvia lo stato almeno ogni 250 ms: dopo 1 s di silenzio il controller torna a riposo.
/// </summary>
public sealed class PadSocketHandler(GameSession session, ILogger<PadSocketHandler> logger)
{
    /// <summary>Colori dei giocatori, come le luci dei controller: rosso, blu, verde, giallo.</summary>
    public static readonly string[] PlayerColors = ["#e53935", "#1e88e5", "#43a047", "#fdd835"];

    // Codici di chiusura applicativi (intervallo 4000-4999 riservato alle applicazioni)
    private const int CloseBadCode = 4001;
    private const int CloseFull = 4003;

    private const uint AllButtons = (1u << 17) - 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record PadMessage(string? T, uint B, int Lx = 128, int Ly = 128, int Rx = 128, int Ry = 128, int L = 0, int R = 0);

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var ct = context.RequestAborted;
        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        if (!session.IsValidCode(context.Request.Query["c"]))
        {
            await SendAsync(socket, new { t = "badcode" }, ct);
            await CloseAsync(socket, CloseBadCode, "Codice partita non valido", ct);
            return;
        }

        var requestedToken = context.Request.Query["t"].ToString();
        var join = session.Join(string.IsNullOrWhiteSpace(requestedToken) ? null : requestedToken);
        if (join is null)
        {
            await SendAsync(socket, new { t = "full" }, ct);
            await CloseAsync(socket, CloseFull, "Tutte le porte sono occupate", ct);
            return;
        }

        await SendAsync(socket, new
        {
            t = "welcome",
            player = join.Slot + 1,
            color = PlayerColors[join.Slot],
            token = join.Token,
            resumed = join.Resumed,
        }, ct);

        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) break;

                // Messaggi binari o più grandi del buffer non fanno parte del protocollo: si ignorano
                if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage) continue;

                if (TryParseState(buffer.AsSpan(0, result.Count), out var state))
                {
                    session.Input(join.Token, state);
                }
            }

            if (socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            logger.LogDebug(ex, "Connessione del giocatore {Player} interrotta", join.Slot + 1);
        }
        finally
        {
            session.Disconnect(join.Token);
        }
    }

    private static bool TryParseState(ReadOnlySpan<byte> json, out ControllerState state)
    {
        state = default;
        PadMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<PadMessage>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (msg is null || msg.T != "s") return false;

        state = ControllerState.Neutral with
        {
            Buttons = (GamepadButtons)(msg.B & AllButtons),
            LeftX = ToByte(msg.Lx),
            LeftY = ToByte(msg.Ly),
            RightX = ToByte(msg.Rx),
            RightY = ToByte(msg.Ry),
            L2Analog = ToByte(msg.L),
            R2Analog = ToByte(msg.R),
        };
        return true;
    }

    private static byte ToByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private static Task SendAsync(WebSocket socket, object message, CancellationToken ct) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, endOfMessage: true, ct);

    private static Task CloseAsync(WebSocket socket, int code, string reason, CancellationToken ct) =>
        socket.CloseAsync((WebSocketCloseStatus)code, reason, ct);
}
