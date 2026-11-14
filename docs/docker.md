# Convenzioni Docker

## Immagini
- Versioni sempre fissate (`postgres:17-alpine`), mai `latest` per i servizi.
- Aggiornamenti di major version dei database solo con migrazione pianificata.

## Porte
- Le porte pubblicate bypassano ufw: nessun servizio viene pubblicato su `0.0.0.0`.
- Pubblicazione su `127.0.0.1` ed esposizione tramite reverse proxy; accesso amministrativo tramite tunnel SSH.

## Dati
- Configurazioni e dati applicativi: bind mount in `/srv/docker/<servizio>/`.
- Media: `/srv/data/` (montato in sola lettura dove possibile).
- Database: named volume o bind mount dedicato, sempre incluso nei backup.
- Permessi: `PUID=1000` / `PGID=1000`.

## Reti
- Una rete dedicata per ogni stack; i container comunicano per nome tramite DNS interno.
- I database non pubblicano porte sull'host.

## Esecuzione
- Restart policy `unless-stopped`.
- Log driver `local` con rotazione (`/etc/docker/daemon.json`).
- Segreti in file `.env` esclusi dal repository.
