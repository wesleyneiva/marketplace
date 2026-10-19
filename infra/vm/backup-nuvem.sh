#!/bin/bash
# Backup FORA da VM (roda como root pelo backup-nuvem.timer, às 04:30, depois dos backups locais).
# Copia ~/backup/n8n-auto e ~/backup/marketplace-auto para o Oracle Object Storage (bucket backup-oracle-a1,
# São Paulo, Always Free 20 GB), CRIPTOGRAFADO pelo rclone ("crypt") antes de sair da VM.
# Na nuvem fica 30 dias (aqui na VM, só 7).
#
# Configuração: /root/.config/rclone/rclone.conf (feita pelo configurar-backup-nuvem.sh)
#   oracle: → o bucket (chave de acesso S3 da Oracle)
#   nuvem:  → "camada" de criptografia por cima do oracle:backup-oracle-a1
#
# Ver o que tem lá:      sudo rclone lsd nuvem:  /  sudo rclone ls nuvem:marketplace
# Restaurar (ex.):       sudo rclone copy nuvem:marketplace/2026-10-09 /tmp/restaurar
# Numa VM NOVA: instalar o rclone e recriar o rclone.conf com a chave da Oracle + a SENHA DA
# CRIPTOGRAFIA (guardada no seu gerenciador de senhas) — sem ela, os arquivos não abrem.
set -euo pipefail

MANTER_DIAS=30
LOG=/home/ubuntu/backup/nuvem-ultimo.txt

falhou=0
{
  echo "Backup na nuvem — $(date '+%F %T')"
  for pasta in n8n marketplace; do
    rclone copy "/home/ubuntu/backup/$pasta-auto" "nuvem:$pasta" --exclude '*.parcial/**' --transfers 4 || falhou=1
    rclone delete "nuvem:$pasta" --min-age "${MANTER_DIAS}d" --rmdirs || falhou=1
  done
  echo "Na nuvem agora:"
  rclone size nuvem: | sed 's/^/  /' || falhou=1
} > "$LOG" 2>&1 || falhou=1

if [ "$falhou" = 1 ]; then echo "FALHOU" >> "$LOG"; exit 1; fi
echo "OK" >> "$LOG"
