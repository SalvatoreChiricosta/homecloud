# gamepad-web

Trasforma gli smartphone in controller per gli emulatori, senza installare app.

```
📱 pagina web ──WebSocket──► gamepad-web ──DSU / UDP 26760──► emulatore (Dolphin, Cemu, …)
```

- **Pagina gamepad** (`/pad?c=<codice>`): levetta, A/B/X/Y, L/Z/R, Start; multi-touch, colore del giocatore.
- **Partita**: codice d'ingresso, fino a 4 giocatori, riconnessione automatica alla stessa porta (token salvato nel browser).
- **Server DSU** ([protocollo cemuhook](https://v1993.github.io/cemuhook-protocol/)): espone le 4 porte agli emulatori.

## Avvio in sviluppo

Richiede .NET SDK 10.

```bash
dotnet run --project apps/gamepad-web -- --Session:JoinCode=TEST
```

All'avvio il log mostra gli indirizzi da aprire sul telefono, ad esempio `http://<IP-del-PC>:8080/pad?c=TEST`.
Telefono e PC devono essere sulla stessa rete; il firewall deve consentire TCP 8080 in ingresso.

Controller simulato (senza telefono): `-- --Dsu:Demo=true`.

## Configurazione

| Chiave | Default | Descrizione |
|---|---|---|
| `Urls` | `http://0.0.0.0:8080` | Indirizzo del web server |
| `Dsu:Port` | `26760` | Porta UDP del server DSU |
| `Dsu:SendRateHz` | `125` | Pacchetti al secondo per porta verso gli emulatori |
| `Dsu:Demo` | `false` | Controller simulato sulla porta 1 |
| `Session:JoinCode` | casuale | Codice d'ingresso fisso |
| `Session:ReconnectGraceSeconds` | `30` | Tempo per cui una porta resta riservata a un telefono disconnesso |
| `Session:InputTimeoutMs` | `1000` | Dopo questo silenzio il controller torna a riposo |

Ogni chiave si può impostare anche come variabile d'ambiente (`Session__JoinCode=TEST`) o da riga di comando.

## Endpoint

| Percorso | Descrizione |
|---|---|
| `/pad?c=<codice>` | Pagina gamepad |
| `/ws` | WebSocket del gamepad |
| `/api/slots` | Stato delle 4 porte (debug) |
| `/health` | Controllo di salute |

## Configurare l'emulatore (Dolphin)

Impostazioni controller → Sorgenti di input alternative → DSU Client → server `<IP>:26760`.
Ogni porta GameCube usa il dispositivo `DSUClient/<n>/…` corrispondente.
I tasti frontali compaiono come Cross (A), Circle (B), Square (X), Triangle (Y).
