#!/bin/bash
# SEGUNDA NUVEM dos backups: Cloudflare R2 (20/10/2026). A VM e o bucket da Oracle estão na MESMA conta; se a
# Oracle suspender a conta, os dois somem juntos. Com isto, os mesmos arquivos (já criptografados) vão também
# para o R2, em outra empresa.
#
# Roda UMA vez, como root, no terminal (pede as chaves sem mostrar na tela):
#   sudo infra/configurar-r2.sh
#
# Cria no /root/.config/rclone/rclone.conf:
#   r2:     → a conta do R2 (chave de API S3 da Cloudflare)
#   nuvem2: → criptografia por cima de r2:<bucket>, com AS MESMAS 2 senhas do "nuvem:" (copiadas do rclone.conf,
#             nada a digitar). Por isso os arquivos são idênticos nas duas nuvens e as mesmas senhas abrem os dois.
# Depois copia para o R2 tudo o que já está na Oracle. Daí em diante o backup-nuvem.sh e o enviar-wal.sh mandam
# para as duas.
set -euo pipefail
[ "$(id -u)" = 0 ] || { echo "Rode com sudo."; exit 1; }
CONF=/root/.config/rclone/rclone.conf

read -rp  "Account ID (o código no endpoint padrão https://<ACCOUNT_ID>.r2.cloudflarestorage.com; NÃO é o ID da chave): " CONTA
read -rp  "Nome do bucket [marcus-backup]: " BUCKET; BUCKET=${BUCKET:-marcus-backup}
read -rp  "Access Key ID do token do R2: " CHAVE
read -rsp "Secret Access Key do token do R2 (não aparece): " SECRETA; echo

rclone config delete r2 2>/dev/null || true
# no_head: depois de cada envio o rclone 1.60 confere o arquivo com HEAD ?versionId=…, que o R2 não implementa (501
# NotImplemented na 1ª tentativa, "ok" só na 2ª). Sem essa conferência extra; a integridade fica pelo Content-MD5,
# que o R2 confere ao receber.
rclone config create r2 s3 provider Cloudflare access_key_id "$CHAVE" secret_access_key "$SECRETA" \
  endpoint "https://$CONTA.r2.cloudflarestorage.com" region auto no_check_bucket true no_head true >/dev/null

# As 2 senhas do "nuvem:" já estão embaralhadas (obscure) no arquivo: copia como estão.
senha() { awk -v k="$1" '/^\[/{s=($0=="[nuvem]")} s && $1==k {print $3}' "$CONF"; }
P1=$(senha password); P2=$(senha password2)
[ -n "$P1" ] && [ -n "$P2" ] || { echo "Não achei as senhas do [nuvem] em $CONF."; exit 1; }
rclone config delete nuvem2 2>/dev/null || true
rclone config create nuvem2 crypt remote "r2:$BUCKET" password "$P1" password2 "$P2" --no-obscure >/dev/null

echo "▶ Testando o acesso ao R2…"
rclone lsf "r2:$BUCKET" --max-depth 1 >/dev/null || { echo "Falhou: confira o Account ID, o bucket e as chaves."; exit 1; }

echo "▶ Copiando para o R2 o que já está na Oracle (arquivos já criptografados, cópia idêntica)…"
rclone copy oracle:backup-oracle-a1 "r2:$BUCKET" --disable Copy --transfers 8 --checkers 16 --stats-one-line --stats 15s

echo "▶ Conferindo com as senhas de sempre:"
rclone lsd nuvem2:
rclone size nuvem2:
echo "✅ R2 pronto. Os próximos envios vão para as duas nuvens."
