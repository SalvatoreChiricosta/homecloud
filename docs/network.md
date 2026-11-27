# Rete

## Interfacce

| Interfaccia | Tipo | MAC | IP (prenotazione DHCP) | Ruolo |
|---|---|---|---|---|
| `enp7s0` | Ethernet 1 Gbit/s | `<MAC_ETH>` | IP riservato principale (`<IP_SERVER>`) | Principale |
| `wlp0s20f3` | Wi-Fi | `<MAC_WIFI>` | IP riservato di riserva (`<IP_SERVER_WIFI>`) | Riserva |

Indirizzi assegnati dal router tramite prenotazione DHCP legata al MAC di ciascuna interfaccia. I valori reali (MAC, IP, modello del router) sono nell'inventario privato, non in questo repository.

## Configurazione netplan

| File | Contenuto |
|---|---|
| `00-installer-config.yaml` | Generato dall'installer: identifica `enp7s0` dal MAC (senza indirizzamento) |
| `40-ethernet.yaml` | `dhcp4: true` su `enp7s0` (unito da netplan al file precedente) |
| `60-wifi.yaml` | Wi-Fi con DHCP e `optional: true`; contiene la password: non versionato |

Permessi dei file: `600`. Entrambe le interfacce usano `dhcp-identifier: mac` (per impostazione predefinita systemd-networkd usa il DUID, che molti router non associano alle prenotazioni).

## Routing

Due default route verso il gateway del router: `enp7s0` con metric 100 (preferita) e `wlp0s20f3` con metric 600 (failover automatico se il cavo è scollegato). Il traffico in ingresso usa l'interfaccia a cui appartiene l'IP contattato: i client puntano all'IP dell'interfaccia cablata.

## Modifiche sicure

- Validare con `netplan generate`.
- Usare `netplan try` solo se la sessione SSH non passa dall'interfaccia che viene modificata; altrimenti applicare con un riavvio.
- Accesso di emergenza: SSH verso l'IP riservato del Wi-Fi.
- Dopo una modifica alle prenotazioni il router la applica alla scadenza del lease DHCP in corso (alcune ore): `networkctl reconfigure` e i riavvii ricevono ancora il lease precedente.

## Misure (ping da PC Windows, 100 pacchetti)

| Collegamento | Persi | Min | Medio | Max |
|---|---|---|---|---|
| Wi-Fi a riposo | 0% | 3 ms | 11 ms | 95 ms |
| Wi-Fi sotto carico (download, streaming) | 20-90% | 3 ms | > 100 ms | 1806 ms |
| Ethernet | 0% | 1 ms | 6 ms | 124 ms |
