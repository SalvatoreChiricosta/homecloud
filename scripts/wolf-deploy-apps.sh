#!/usr/bin/env bash
# Applica le personalizzazioni delle app di Wolf versionate in stacks/wolf/apps.
# Da eseguire con le app di Wolf chiuse (rileggono e riscrivono la configurazione).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$REPO/stacks/wolf/apps"
PROFILE=/etc/wolf/profile-data/user
TOML=/etc/wolf/cfg/config.toml

if docker ps --format '{{.Names}}' | grep -qE '^Wolf(ES-DE|Retroarch)_'; then
  echo "Errore: chiudi prima le app di Wolf (ES-DE / RetroArch)." >&2
  exit 1
fi

# ES-DE: script Dolphin, regole di ricerca, mappatura controller
sudo mkdir -p "$PROFILE/WolfES-DE"
sudo cp -a "$SRC/es-de/." "$PROFILE/WolfES-DE/"
sudo chown -R 1000:1000 \
  "$PROFILE/WolfES-DE/bin" \
  "$PROFILE/WolfES-DE/ES-DE/custom_systems" \
  "$PROFILE/WolfES-DE/.config/dolphin-emu"
sudo chmod 755 "$PROFILE/WolfES-DE/bin/dolphin-esde.sh"
echo "ES-DE: ok"

# RetroArch: imposta (o aggiunge) ogni chiave di retroarch-overrides.cfg
RA="$PROFILE/WolfRetroarch/.config/retroarch/retroarch.cfg"
if [ -f "$RA" ]; then
  grep -E '^[a-z_]+ = ' "$SRC/retroarch/retroarch-overrides.cfg" | while IFS= read -r line; do
    key="${line%% = *}"
    if sudo grep -q "^$key = " "$RA"; then
      sudo sed -i "s#^$key = .*#$line#" "$RA"
    else
      echo "$line" | sudo tee -a "$RA" > /dev/null
    fi
  done
  echo "RetroArch: ok"
else
  echo "RetroArch: retroarch.cfg non ancora creato (avviare l'app una volta), saltato"
fi

# config.toml: immagini :edge al posto delle :fedora difettose
sudo sed -i \
  -e 's#games-on-whales/retroarch:fedora#games-on-whales/retroarch:edge#' \
  -e 's#games-on-whales/es-de:fedora#games-on-whales/es-de:edge#' \
  "$TOML"
echo "config.toml: ok (se e' cambiato, riavviare Wolf: docker compose restart in stacks/wolf)"
