#!/bin/bash
# Backup diário do n8n (roda como root pelo backup-n8n.timer, às 03:30).
# Guarda em ~/backup/n8n-auto/AAAA-MM-DD: fluxos (1 arquivo por fluxo), credenciais (criptografadas),
# a chave "config" (sem ela as credenciais não abrem) e o banco completo compactado.
# Mantém os últimos 7 dias.
set -euo pipefail

BASE=/home/ubuntu/backup/n8n-auto
MANTER=7
VOLUME=/var/lib/docker/volumes/n8n_dados/_data
DESTINO="$BASE/$(date +%F)"
PARCIAL="$DESTINO.parcial"   # só vira a pasta do dia se tudo der certo

rm -rf "$PARCIAL"
mkdir -p "$PARCIAL"
chmod 700 "$BASE" "$PARCIAL"

# 1. Fluxos e credenciais, exportados pelo próprio n8n
docker exec n8n sh -c 'rm -rf /tmp/bkp && mkdir -p /tmp/bkp/fluxos \
  && n8n export:workflow --all --separate --output=/tmp/bkp/fluxos/ \
  && n8n export:credentials --all --output=/tmp/bkp/credenciais.json' >/dev/null
docker cp -q n8n:/tmp/bkp/. "$PARCIAL/"
docker exec n8n rm -rf /tmp/bkp

FLUXOS=$(ls "$PARCIAL/fluxos" | wc -l)
if [ "$FLUXOS" -eq 0 ]; then
    echo "Nenhum fluxo exportado: backup abortado" >&2
    exit 1
fi
# Índice "código → nome do fluxo", para achar o arquivo certo na hora de restaurar
python3 - "$PARCIAL/fluxos" > "$PARCIAL/indice-fluxos.txt" <<'EOF'
import json, sys
from pathlib import Path
for arq in sorted(Path(sys.argv[1]).glob("*.json"), key=lambda a: json.loads(a.read_text())["name"].lower()):
    fluxo = json.loads(arq.read_text())
    ativo = "publicado" if fluxo.get("active") else "parado"
    print(f"{arq.name:24} {ativo:10} {fluxo['name']}")
EOF

# 2. Chave de criptografia
cp "$VOLUME/config" "$PARCIAL/config"

# 3. Banco: cópia consistente com o n8n ligado (backup do próprio SQLite, não "cp")
python3 - "$VOLUME/database.sqlite" "$PARCIAL/database.sqlite" <<'EOF'
import sqlite3, sys
origem = sqlite3.connect(f"file:{sys.argv[1]}?mode=ro", uri=True)
copia = sqlite3.connect(sys.argv[2])
origem.backup(copia)
assert copia.execute("pragma integrity_check").fetchone()[0] == "ok"
copia.close()
origem.close()
EOF
gzip "$PARCIAL/database.sqlite"

chown -R ubuntu:ubuntu "$BASE"
rm -rf "$DESTINO"
mv "$PARCIAL" "$DESTINO"

# 4. Apaga os mais antigos, mantendo os últimos $MANTER dias
ls -1d "$BASE"/20??-??-?? | head -n -"$MANTER" | xargs -r rm -rf

echo "Backup ok: $FLUXOS fluxos, $(du -sh "$DESTINO" | cut -f1) em $DESTINO"
