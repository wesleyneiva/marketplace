#!/usr/bin/env bash
# Cria a ÁREA DO DONO da plataforma e o login do dono (senha provisória mostrada só aqui no terminal).
# Uso:
#   ./criar-dono.sh --email voce@exemplo.com --nome "Seu Nome"
# Usa a mesma configuração do serviço (/etc/marketplace/marketplace.env, que só o root lê).
set -euo pipefail
APP="$HOME/servicos/marketplace/app"
# Lê o arquivo linha a linha, como o systemd faz (com "source" do bash, os ";" da conexão do banco
# cortariam a linha e o programa tentaria entrar no banco como "root").
sudo --preserve-env=HOME bash -c '
  while IFS= read -r linha; do
    [[ $linha =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] && export "$linha"
  done < /etc/marketplace/marketplace.env
  cd "$0" && exec dotnet Marketplace.Api.dll criar-dono "$@"' "$APP" "$@"
# O serviço lê ao ligar qual é a empresa da plataforma: reinicia para a área do dono valer já.
sudo systemctl restart marketplace && echo "   Serviço reiniciado: a área do dono já vale."
