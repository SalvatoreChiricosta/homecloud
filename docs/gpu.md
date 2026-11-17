# GPU NVIDIA

## Hardware

| Componente | Dettaglio |
|---|---|
| GPU dedicata | NVIDIA GeForce GTX 1650 4 GB (Turing, TU117) |
| GPU integrata | Intel UHD 630 (Quick Sync) |
| Encoder video | NVENC (H.264 / HEVC) |
| Secure Boot | Disattivato |

## Driver

- Driver installato: **`nvidia-driver-615-open`** (variante completa, moduli kernel open, pacchetto firmato dell'archivio Ubuntu).
- Il ramo 595 consigliato da `ubuntu-drivers` espone l'API NVENC 13.0; le immagini ffmpeg recenti richiedono l'API 13.1 (driver ≥ 610). Il ramo 615 garantisce compatibilità con le immagini aggiornate.
- Il driver completo è necessario per grafica (OpenGL/Vulkan) e NVENC, richiesti dal game streaming e dall'emulazione. Il ramo `--gpgpu` (solo calcolo) non è adatto.
- Secure Boot è disattivato nel firmware: i moduli del kernel vengono caricati senza registrazione della chiave MOK. Se in futuro Secure Boot viene attivato, al primo riavvio dopo un'installazione del driver va completata la registrazione MOK dalla tastiera del server.

Verifica:

```bash
mokutil --sb-state
nvidia-smi
lsmod | grep nvidia
```

## NVIDIA Container Toolkit

Il driver resta sull'host; il toolkit monta nei container i dispositivi `/dev/nvidia*` e le librerie del driver.

```bash
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker
docker info | grep -i runtimes
```

`/etc/docker/daemon.json` mantiene la rotazione dei log (`log-driver: local`) e aggiunge il runtime `nvidia`.

## Uso nei servizi Compose

```yaml
services:
  esempio:
    image: ...
    environment:
      NVIDIA_DRIVER_CAPABILITIES: all   # oppure: compute,utility,video,graphics
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]
```

| Capability | Serve per |
|---|---|
| `utility` | `nvidia-smi` |
| `compute` | CUDA (es. riconoscimento volti di Immich) |
| `video` | NVENC / NVDEC (streaming, transcodifica Jellyfin) |
| `graphics` | OpenGL / Vulkan (giochi, emulatori) |

## Test

```bash
# GPU visibile nel container
docker run --rm --gpus all ubuntu nvidia-smi

# Encoder hardware: 10 s di video 1080p60 codificati con NVENC
docker run --rm --gpus all -e NVIDIA_DRIVER_CAPABILITIES=all \
  linuxserver/ffmpeg -f lavfi -i testsrc=size=1920x1080:rate=60 \
  -t 10 -c:v h264_nvenc -f null -
```

Durante il test, `nvidia-smi dmon` mostra l'utilizzo dell'encoder nella colonna `enc`.

## Troubleshooting

| Sintomo | Causa probabile |
|---|---|
| `nvidia-smi: command not found` | Driver non installato |
| `NVIDIA-SMI has failed... couldn't communicate with the driver` | Modulo non caricato (MOK non registrata con Secure Boot, oppure manca il riavvio) |
| `could not select device driver "" with capabilities: [[gpu]]` | Container Toolkit non configurato o Docker non riavviato |
| `h264_nvenc` non disponibile nel container | Manca la capability `video` |
| `Driver does not support the required nvenc API version` | Immagine compilata per un'API NVENC più recente del driver dell'host: aggiornare il driver o fissare un'immagine compatibile |

## Risultati del test (driver 615.71.09)

| Metrica | Valore |
|---|---|
| Codifica H.264 1080p60, 600 frame | 131 fps (2,17× tempo reale) |
| Utilizzo encoder (`enc`) | ~73% |
| Consumo GPU | ~15 W |

La sorgente di test è RGB (H.264 4:4:4), più onerosa del 4:2:0 usato nello streaming: il margine per 1080p60 è ampio.
