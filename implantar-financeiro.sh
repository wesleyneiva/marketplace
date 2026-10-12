#!/usr/bin/env bash
# IMPLANTAÇÃO do Financeiro de um cliente: saldos iniciais, contas fixas, saldo mínimo e alíquota do Simples (.json).
# Uso:
#   ./implantar-financeiro.sh --arquivo ~/cliente-financeiro.json               (prévia, não grava)
#   ./implantar-financeiro.sh --arquivo ~/cliente-financeiro.json --gravar true
# Usa a mesma configuração do serviço (/etc/marketplace/marketplace.env, que só o root lê).
set -euo pipefail
APP="$HOME/servicos/marketplace/app"
# Lê o arquivo linha a linha, como o systemd faz (com "source" do bash, os ";" da conexão do banco
# cortariam a linha e o programa tentaria entrar no banco como "root").
sudo --preserve-env=HOME bash -c '
  while IFS= read -r linha; do
    [[ $linha =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] && export "$linha"
  done < /etc/marketplace/marketplace.env
  cd "$0" && exec dotnet Marketplace.Api.dll implantar-financeiro "$@"' "$APP" "$@"
