#!/usr/bin/env bash
# IMPLANTAÇÃO: importa a planilha de produtos (estoque inicial, fornecedores) direto na empresa de um cliente.
# Uso:
#   ./importar-produtos.sh --empresa 4 --arquivo ~/planilha.csv               (prévia, não grava)
#   ./importar-produtos.sh --empresa 4 --arquivo ~/planilha.csv --gravar true
# Usa a mesma configuração do serviço (/etc/marketplace/marketplace.env, que só o root lê).
set -euo pipefail
APP="$HOME/servicos/marketplace/app"
# Lê o arquivo linha a linha, como o systemd faz (com "source" do bash, os ";" da conexão do banco
# cortariam a linha e o programa tentaria entrar no banco como "root").
sudo --preserve-env=HOME bash -c '
  while IFS= read -r linha; do
    [[ $linha =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] && export "$linha"
  done < /etc/marketplace/marketplace.env
  cd "$0" && exec dotnet Marketplace.Api.dll importar-produtos "$@"' "$APP" "$@"
