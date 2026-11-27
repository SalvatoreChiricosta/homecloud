# Storage

## Layout LVM
| LV | Dimensione | Filesystem | Mount | Uso |
|---|---|---|---|---|
| `ubuntu-vg/ubuntu-lv` | 98G | ext4 | `/` | Sistema operativo, immagini Docker |
| `ubuntu-vg/data` | 350G | ext4 | `/srv/data` | Media e dati applicativi |

~24G lasciati liberi nel VG per crescita e snapshot.

## Directory


Proprietario: utente amministratore (UID/GID 1000), usato come `PUID`/`PGID` nei container.

## fstab
Montaggio tramite UUID con opzioni `defaults,noatime`.
