#!/bin/bash
# Configura (uma vez só) o backup fora da VM no Oracle Object Storage. Rodar com:
#   sudo ~/scripts/configurar-backup-nuvem.sh
# Pergunta o namespace e a chave SECRETA (não aparece na tela), cria a senha da criptografia,
# instala o rclone e o agendamento diário (04:30) e faz o primeiro envio.
set -euo pipefail
[ "$(id -u)" = 0 ] || { echo "Rode com sudo."; exit 1; }

ACCESS_KEY="${ACCESS_KEY:?informe a chave de ACESSO da Oracle: sudo ACCESS_KEY=... ~/scripts/configurar-backup-nuvem.sh}"   # (o ID da chave fica fora do Git)
REGIAO="sa-saopaulo-1"
BUCKET="backup-oracle-a1"
CONF=/root/.config/rclone/rclone.conf

echo "▶ 1/5  Instalando o rclone…"
apt-get install -y -qq rclone >/dev/null
rclone version | head -1 | sed 's/^/   /'

echo
read -rp "Namespace do Object Storage (aparece na página do bucket, campo 'Namespace'): " NAMESPACE
read -rsp "Chave SECRETA da Oracle (cole e Enter — não aparece na tela): " SECRETA; echo
[ -n "$NAMESPACE" ] && [ -n "$SECRETA" ] || { echo "Faltou o namespace ou a chave."; exit 1; }

echo "▶ 2/5  Criando a senha da criptografia…"
SENHA_CRIPTO=$(openssl rand -base64 30 | tr -d '/+=' | cut -c1-32)
SAL=$(openssl rand -base64 30 | tr -d '/+=' | cut -c1-32)

mkdir -p "$(dirname "$CONF")"
umask 077
cat > "$CONF" <<EOF
[oracle]
type = s3
provider = Other
access_key_id = $ACCESS_KEY
secret_access_key = $SECRETA
endpoint = https://$NAMESPACE.compat.objectstorage.$REGIAO.oraclecloud.com
region = $REGIAO
force_path_style = true
no_check_bucket = true

[nuvem]
type = crypt
remote = oracle:$BUCKET
password = $(rclone obscure "$SENHA_CRIPTO")
password2 = $(rclone obscure "$SAL")
EOF
chmod 600 "$CONF"

echo "▶ 3/5  Testando o acesso ao bucket…"
if ! rclone lsd "oracle:$BUCKET" >/dev/null 2>/tmp/rclone-erro.txt; then
  echo "❌ Não consegui acessar o bucket:"; tail -3 /tmp/rclone-erro.txt; rm -f /tmp/rclone-erro.txt
  echo "   Confira o namespace e a chave secreta e rode de novo."; exit 1
fi
echo "   ✅ Bucket acessível"

echo "▶ 4/5  Agendamento diário (04:30)…"
cat > /etc/systemd/system/backup-nuvem.service <<'EOF'
[Unit]
Description=Envia os backups (n8n e Marketplace) para o Oracle Object Storage, criptografados
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=/home/ubuntu/scripts/backup-nuvem.sh
EOF
cat > /etc/systemd/system/backup-nuvem.timer <<'EOF'
[Unit]
Description=Roda o backup na nuvem todo dia às 04:30

[Timer]
OnCalendar=*-*-* 04:30:00
Persistent=true

[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now backup-nuvem.timer >/dev/null

echo "▶ 5/5  Primeiro envio (pode levar 1 minuto)…"
systemctl start backup-nuvem.service && sed 's/^/   /' /home/ubuntu/backup/nuvem-ultimo.txt

cat <<EOF

════════════════════════════════════════════════════════════════
  🔐 SENHA DA CRIPTOGRAFIA — GUARDE NO SEU GERENCIADOR DE SENHAS

     Senha:  $SENHA_CRIPTO
     Sal:    $SAL

  Sem ela, os backups da nuvem NÃO abrem numa VM nova.
  (Não mande no chat. Depois de guardar, pode limpar a tela: clear)
════════════════════════════════════════════════════════════════
EOF
