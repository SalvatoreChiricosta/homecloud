# Rete

## Interfacce

| Interfaccia | Tipo | MAC | IP (prenotazione DHCP) | Ruolo |
|---|---|---|---|---|
| `enp7s0` | Ethernet 1 Gbit/s | 98:fa:9b:31:32:07 | 192.168.1.35 | Principale |
| `wlp0s20f3` | Wi-Fi | dc:fb:48:a7:7f:79 | dinamico (DHCP) | Riserva |

Router: TIM HUB+ (ZTE H388X), prenotazioni in Rete locale → LAN → IP Statico.

## Configurazione netplan

| File | Contenuto |
|---|---|
| `00-installer-config.yaml` | Generato dall'installer: identifica `enp7s0` dal MAC (senza indirizzamento) |
| `40-ethernet.yaml` | `dhcp4: true` su `enp7s0` (unito da netplan al file precedente) |
| `60-wifi.yaml` | Wi-Fi con DHCP e `optional: true`; contiene la password: non versionato |

Permessi dei file: `600`. Entrambe le interfacce usano `dhcp-identifier: mac` (per impostazione predefinita systemd-networkd usa il DUID, che molti router non associano alle prenotazioni).

## Routing

Due default route verso `192.168.1.1`: `enp7s0` con metric 100 (preferita) e `wlp0s20f3` con metric 600 (failover automatico se il cavo è scollegato). Il traffico in ingresso usa l'interfaccia a cui appartiene l'IP contattato: i client puntano a `.35` (cavo).

## Modifiche sicure

- Validare con `netplan generate`.
- Usare `netplan try` solo se la sessione SSH non passa dall'interfaccia che viene modificata; altrimenti applicare con un riavvio.
- Accesso di emergenza via Wi-Fi: IP dinamico, visibile nella lista dispositivi del router (il router non applica la prenotazione alla seconda interfaccia con lo stesso hostname).

## Misure (ping da PC Windows, 100 pacchetti)

| Collegamento | Persi | Min | Medio | Max |
|---|---|---|---|---|
| Wi-Fi a riposo | 0% | 3 ms | 11 ms | 95 ms |
| Wi-Fi sotto carico (download, streaming) | 20-90% | 3 ms | > 100 ms | 1806 ms |
| Ethernet | 0% | 1 ms | 6 ms | 124 ms |
