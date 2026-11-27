# Game streaming

## Architettura

```
Moonlight (Fire TV Stick) ⇄ Wolf (container, network_mode: host) → container dell'app (ES-DE, RetroArch, …)
                                                                    avviato da Wolf tramite docker.sock
```

Wolf crea un display Wayland virtuale per ogni sessione e codifica con NVENC (H.264/HEVC) in zero-copy sulla GTX 1650 (`/dev/dri/renderD128`). Le app girano in container separati, creati e distrutti da Wolf.

## Componenti

| Elemento | Percorso / valore |
|---|---|
| Stack | `stacks/wolf/compose.yaml` (immagine fissata con digest) |
| Volume driver per le app | `nvidia-driver-vol`, costruito da `stacks/wolf/nvidia-driver/Dockerfile` |
| Configurazione Wolf | `/etc/wolf/cfg/config.toml` (app, immagini, mount dei giochi) — non versionato: contiene i client associati |
| Home delle app | `/etc/wolf/profile-data/user/<App>/` |
| Personalizzazioni delle app | `stacks/wolf/apps/`, applicate con `scripts/wolf-deploy-apps.sh` |
| Giochi | `/srv/data/giochi/<console>` → `/ROMs/<console>` nei container (sola lettura) |

### Modifiche a `config.toml`

- RetroArch ed ES-DE: immagini `:edge` invece di `:fedora` (vedi problemi noti).
- RetroArch ed ES-DE: `mounts = [ '/srv/data/giochi:/ROMs:ro' ]` (il suffisso `:ro`/`:rw` è obbligatorio).

## Prerequisiti host

- `nvidia-drm.modeset=Y`
- udev: `/etc/udev/rules.d/85-wolf.rules` (ufficiale) e `86-uhid-static.rules` (`static_node=uhid`); modulo `uhid` in `/etc/modules-load.d/uhid.conf`
- GPU sempre attiva: `/etc/modprobe.d/nvidia-power.conf` (`NVreg_DynamicPowerManagement=0x00`) e drop-in `/etc/systemd/system/nvidia-persistenced.service.d/persistence.conf` con `--persistence-mode`
- ufw, solo dalla subnet LAN: 47984, 47989, 48010/tcp; 47999, 48100, 48200/udp

## Emulazione GameCube/Wii

- Dolphin standalone dall'immagine ES-DE, avviato da `stacks/wolf/apps/es-de/bin/dolphin-esde.sh` (`--appimage-extract-and-run`, nessun FUSE nel container).
- Impostazioni passate con `-C`: schermo intero, Vulkan, risoluzione interna 2×, widescreen hack, 16:9, ubershader esclusivi, contatore FPS. Il sistema grafico si chiama `Graphics` (non `GFX`).
- ES-DE trova Dolphin tramite `es_find_rules.xml` (emulatore `DOLPHIN` → script di avvio).
- Controller: `GCPadNew.ini` con dispositivo `evdev/0/Wolf X-Box One (virtual) pad`.

## Procedure

- **Pairing:** avviare il pairing da Moonlight, aprire il link completo `http://<IP_SERVER>:47989/pin/#<codice>` dai log di Wolf e inserire il PIN.
- **Controller:** accenderlo prima di avviare la sessione.
- **Chiudere un'app:** uscire dal menu dell'app. "Chiudi sessione" in Moonlight lascia l'app attiva nella lobby. Non usare `docker stop` sui container delle app.
- **Configurazione delle app:** modificare solo con l'app chiusa (viene riscritta alla chiusura); applicare le modifiche con `scripts/wolf-deploy-apps.sh`.
- **Aggiornamento driver NVIDIA:** ricostruire `nvidia-driver-vol` con la nuova versione.
- **Aggiornamento di Wolf:** `docker pull` del tag, lettura del nuovo digest con `docker inspect`, aggiornamento del digest in `compose.yaml` con un commit.

## Problemi noti

| Sintomo | Causa | Soluzione |
|---|---|---|
| "key not found" sulla pagina PIN | Link senza codice `#…` | Usare il link completo dai log |
| Porte 47984/47989 con Recv-Q > 0, Moonlight non si connette | Wolf bloccato | `docker compose restart`; se non si ferma, riavvio del server |
| Errori `NVRM` (TLB, mmu_walk) e processi in stato D | Risparmio energetico dinamico della GPU | Prerequisito "GPU sempre attiva" |
| Wolf non riparte dopo il reboot | Arresto forzato fallito, Docker lo considera fermato dall'utente (`unless-stopped`) | `docker compose up -d` |
| RetroArch torna subito al menu | `retroarch:fedora` senza `7z` (lo script degli asset fallisce) | Immagine `retroarch:edge` |
| ES-DE schermo nero | `es-de:fedora` contiene l'AppImage ES-DE per ARM64 (`Exec format error`) | Immagine `es-de:edge` |
| RetroArch non mostra i giochi | Info dei core assenti | `libretro_info_path = /usr/share/libretro/info` |
| ES-DE: "couldn't find emulator DOLPHIN" | Dolphin non nei percorsi standard | `es_find_rules.xml` + `dolphin-esde.sh` |
| Impostazioni grafiche di Dolphin ignorate | `-C GFX.…` non riconosciuto | `-C Graphics.Settings.…` |

## Misure (server su Ethernet, Fire Stick su Wi-Fi)

| Metrica | Valore |
|---|---|
| Ping durante lo streaming | 0% persi, min 1 / medio 2 / max 35 ms |
| Input | Nessun ritardo percepito |
| Encoding | 1080p60 H.264, NVENC |
| Mario Kart Double Dash (Dolphin, 2×, 16:9) | 59 FPS stabili |
| Fluidità percepita | Leggero stutter residuo dello stream, da analizzare con le statistiche di Moonlight |
