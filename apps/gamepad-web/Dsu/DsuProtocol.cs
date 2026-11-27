using System.Buffers.Binary;
using System.IO.Hashing;
using GamepadWeb.Input;

namespace GamepadWeb.Dsu;

/// <summary>
/// Codifica/decodifica dei pacchetti del protocollo DSU (cemuhook), versione 1001.
/// Riferimento: https://v1993.github.io/cemuhook-protocol/
/// Tutti gli interi sono little-endian.
///
/// Pacchetto = header (16 byte) + tipo di messaggio (4 byte) + payload.
///   0  "DSUS" (server) / "DSUC" (client)
///   4  uint16 versione (1001)
///   6  uint16 lunghezza del pacchetto esclusi i 16 byte di header
///   8  uint32 CRC32 dell'intero pacchetto calcolato con questo campo a zero
///   12 uint32 id del mittente
///   16 uint32 tipo di messaggio
///   20 payload
/// </summary>
public static class DsuProtocol
{
    public const ushort ProtocolVersion = 1001;

    public const uint MsgVersion = 0x100000;
    public const uint MsgInfo = 0x100001;
    public const uint MsgData = 0x100002;

    private const int HeaderSize = 16;
    private const int PayloadOffset = 20;

    /// <summary>Valida un pacchetto ricevuto dal client e ne estrae tipo e payload.</summary>
    public static bool TryParseRequest(ReadOnlySpan<byte> data, out uint messageType, out ReadOnlySpan<byte> payload)
    {
        messageType = 0;
        payload = default;

        if (data.Length < PayloadOffset || !data[..4].SequenceEqual("DSUC"u8))
            return false;

        int length = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        if (length < 4 || HeaderSize + length > data.Length)
            return false;

        messageType = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        payload = data.Slice(PayloadOffset, length - 4);
        return true;
    }

    /// <summary>Risposta a "versione del protocollo": 2 byte con la versione massima supportata.</summary>
    public static byte[] BuildVersionResponse(uint serverId)
    {
        var packet = new byte[PayloadOffset + 2];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(PayloadOffset), ProtocolVersion);
        Finish(packet, serverId, MsgVersion);
        return packet;
    }

    /// <summary>Risposta a "informazioni sui controller": 12 byte per una porta.</summary>
    public static byte[] BuildInfoResponse(uint serverId, byte slot, bool connected)
    {
        var packet = new byte[PayloadOffset + 12];
        WriteSlotHeader(packet.AsSpan(PayloadOffset), slot, connected);
        Finish(packet, serverId, MsgInfo);
        return packet;
    }

    /// <summary>Pacchetto dati (100 byte totali): stato completo del controller di una porta.</summary>
    public static byte[] BuildDataPacket(uint serverId, byte slot, in ControllerState s, uint packetNumber, ulong motionTimestampUs)
    {
        var packet = new byte[PayloadOffset + 80];
        var p = packet.AsSpan(PayloadOffset);

        WriteSlotHeader(p, slot, connected: true);
        p[11] = 1; // connesso
        BinaryPrimitives.WriteUInt32LittleEndian(p[12..], packetNumber);

        // Bitmask 1 (dal bit 7 al bit 0): D-Pad Left, Down, Right, Up, Options, R3, L3, Share
        p[16] = Bits(
            s.Has(GamepadButtons.DpadLeft), s.Has(GamepadButtons.DpadDown),
            s.Has(GamepadButtons.DpadRight), s.Has(GamepadButtons.DpadUp),
            s.Has(GamepadButtons.Options), s.Has(GamepadButtons.R3),
            s.Has(GamepadButtons.L3), s.Has(GamepadButtons.Share));

        // Bitmask 2 (dal bit 7 al bit 0): Y, B, A, X, R1, L1, R2, L2.
        // ATTENZIONE: il protocollo usa i nomi Nintendo (Y = sinistra, B = basso, A = destra, X = alto),
        // il nostro enum usa le posizioni Xbox (X = sinistra, A = basso, B = destra, Y = alto).
        // In Dolphin corrispondono a Square, Cross, Circle, Triangle.
        p[17] = Bits(
            s.Has(GamepadButtons.X), s.Has(GamepadButtons.A),
            s.Has(GamepadButtons.B), s.Has(GamepadButtons.Y),
            s.Has(GamepadButtons.R1), s.Has(GamepadButtons.L1),
            s.Has(GamepadButtons.R2) || s.R2Analog > 0, s.Has(GamepadButtons.L2) || s.L2Analog > 0);

        p[18] = (byte)(s.Has(GamepadButtons.Home) ? 1 : 0);
        p[19] = 0; // pulsante touchpad

        // Levette: 128 = centro; per X destra positiva, per Y alto positivo
        p[20] = s.LeftX;
        p[21] = s.LeftY;
        p[22] = s.RightX;
        p[23] = s.RightY;

        // Pulsanti "analogici" (0 o 255 per quelli digitali)
        p[24] = Analog(s.Has(GamepadButtons.DpadLeft));
        p[25] = Analog(s.Has(GamepadButtons.DpadDown));
        p[26] = Analog(s.Has(GamepadButtons.DpadRight));
        p[27] = Analog(s.Has(GamepadButtons.DpadUp));
        p[28] = Analog(s.Has(GamepadButtons.X)); // sinistra (Y Nintendo / Square)
        p[29] = Analog(s.Has(GamepadButtons.A)); // basso    (B Nintendo / Cross)
        p[30] = Analog(s.Has(GamepadButtons.B)); // destra   (A Nintendo / Circle)
        p[31] = Analog(s.Has(GamepadButtons.Y)); // alto     (X Nintendo / Triangle)
        p[32] = Analog(s.Has(GamepadButtons.R1));
        p[33] = Analog(s.Has(GamepadButtons.L1));
        p[34] = s.Has(GamepadButtons.R2) ? (byte)255 : s.R2Analog;
        p[35] = s.Has(GamepadButtons.L2) ? (byte)255 : s.L2Analog;

        // 36..47: due tocchi del touchpad, non usati (zeri)

        // Movimento: timestamp in microsecondi, accelerometro in g, giroscopio in gradi/s
        BinaryPrimitives.WriteUInt64LittleEndian(p[48..], motionTimestampUs);
        BinaryPrimitives.WriteSingleLittleEndian(p[56..], s.AccelX);
        BinaryPrimitives.WriteSingleLittleEndian(p[60..], s.AccelY);
        BinaryPrimitives.WriteSingleLittleEndian(p[64..], s.AccelZ);
        BinaryPrimitives.WriteSingleLittleEndian(p[68..], s.GyroPitch);
        BinaryPrimitives.WriteSingleLittleEndian(p[72..], s.GyroYaw);
        BinaryPrimitives.WriteSingleLittleEndian(p[76..], s.GyroRoll);

        Finish(packet, serverId, MsgData);
        return packet;
    }

    /// <summary>Intestazione comune di 11 byte (porta, stato, modello, connessione, MAC, batteria) + 1 byte.</summary>
    private static void WriteSlotHeader(Span<byte> p, byte slot, bool connected)
    {
        p[0] = slot;
        if (!connected) return; // porta vuota: tutti zeri

        p[1] = 2;    // stato: connesso
        p[2] = 2;    // modello: giroscopio completo
        p[3] = 1;    // connessione: USB
        // MAC fittizio "amministrato localmente", diverso per ogni porta: 02:00:00:00:00:0N
        p[4] = 0x02;
        p[9] = (byte)(slot + 1);
        p[10] = 0x05; // batteria: piena
    }

    /// <summary>Scrive l'header e infine il CRC32 calcolato sull'intero pacchetto.</summary>
    private static void Finish(Span<byte> packet, uint serverId, uint messageType)
    {
        "DSUS"u8.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet[4..], ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet[6..], (ushort)(packet.Length - HeaderSize));
        BinaryPrimitives.WriteUInt32LittleEndian(packet[8..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[12..], serverId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[16..], messageType);

        var crc = Crc32.HashToUInt32(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[8..], crc);
    }

    private static byte Bits(bool b7, bool b6, bool b5, bool b4, bool b3, bool b2, bool b1, bool b0) =>
        (byte)((b7 ? 0x80 : 0) | (b6 ? 0x40 : 0) | (b5 ? 0x20 : 0) | (b4 ? 0x10 : 0) |
               (b3 ? 0x08 : 0) | (b2 ? 0x04 : 0) | (b1 ? 0x02 : 0) | (b0 ? 0x01 : 0));

    private static byte Analog(bool pressed) => pressed ? (byte)255 : (byte)0;
}
