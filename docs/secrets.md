# Gestione dei segreti

## Strumenti
- **SOPS + age**: segreti cifrati nel repository (`stacks/<stack>/secrets.enc.yaml`). Sono cifrati solo i valori; i nomi restano leggibili.
- **Docker Compose secrets**: i segreti sono montati nei container come file in `/run/secrets/<nome>` e letti tramite variabili `*_FILE`.

## Chiavi
- Chiave privata age: `~/.config/sops/age/keys.txt` sul server (permessi 600), con copia di backup esterna. Mai nel repository.
- Chiave pubblica e regole di cifratura: `.sops.yaml` nella root del repository.

## Flusso
1. Creare o modificare i segreti: `sops stacks/<stack>/secrets.enc.yaml`
2. Decifrarli sul server: `./scripts/decrypt-secrets.sh <stack>` → `/srv/docker/<stack>/secrets/` (cartella 700)
3. Riferirli nel compose:
   ```yaml
   secrets:
     nome_segreto:
       file: /srv/docker/<stack>/secrets/nome_segreto
   ```
4. Riavviare lo stack: `docker compose up -d`

## Regole
- Nessun segreto in chiaro nel repository o nelle variabili d'ambiente.
- I file decifrati restano fuori dal repository.
- `docker compose config` stampa i valori interpolati: per la validazione usare `--quiet`.
- In caso di esposizione accidentale: il segreto va considerato compromesso e ruotato.
