#!/usr/bin/env bash
# Decifra i segreti di uno stack in /srv/docker/<stack>/secrets/ (un file per segreto)
set -euo pipefail
umask 077

STACK="${1:?Uso: $0 <nome-stack>}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$REPO/stacks/$STACK/secrets.enc.yaml"
DEST="/srv/docker/$STACK/secrets"

[[ -f "$SRC" ]] || { echo "File non trovato: $SRC" >&2; exit 1; }

mkdir -p "$DEST"
chmod 700 "$DEST"

for key in $(sops --decrypt --output-type json "$SRC" | jq -r 'keys[]'); do
  sops --decrypt --extract "[\"$key\"]" "$SRC" > "$DEST/$key"
  # 644: leggibile dall'utente interno al container; la cartella 700 blocca gli altri utenti dell'host
  chmod 644 "$DEST/$key"
done

echo "Segreti di '$STACK' scritti in $DEST:"
ls -l "$DEST"
