#!/usr/bin/env bash
# Publica o Marketplace: compila o Angular + a API e atualiza o serviço "marketplace".
# Uso:  ./publicar.sh
#
# Resultado: tudo em ~/servicos/marketplace/app  →  http://oracle-a1:5100
set -euo pipefail

RAIZ="$(cd "$(dirname "$0")" && pwd)"
DESTINO="$HOME/servicos/marketplace/app"
TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NG_CLI_ANALYTICS=false
export NVM_DIR="$HOME/.config/nvm"
# shellcheck disable=SC1091
. "$NVM_DIR/nvm.sh" >/dev/null

echo "▶ 1/4  Compilando o Angular (versão de produção)…"
cd "$RAIZ/marketplace-web"
npx ng build --configuration production >"$TEMP/ng.log" 2>&1 || { cat "$TEMP/ng.log"; exit 1; }
grep -E "Initial total|bundle generation complete" "$TEMP/ng.log" | sed 's/^/   /'

echo "▶ 2/4  Compilando a API (Release)…"
cd "$RAIZ/Marketplace.Api"
dotnet publish -c Release -o "$TEMP/app" -v q --nologo >"$TEMP/dotnet.log" 2>&1 || { cat "$TEMP/dotnet.log"; exit 1; }

echo "▶ 3/4  Montando a pasta do serviço…"
mkdir -p "$TEMP/app/wwwroot"
cp -r "$RAIZ/marketplace-web/dist/marketplace-web/browser/." "$TEMP/app/wwwroot/"
# Mantém a versão anterior como cópia de segurança (para voltar atrás, se precisar).
mkdir -p "$(dirname "$DESTINO")"
rm -rf "$DESTINO.anterior"
[ -d "$DESTINO" ] && mv "$DESTINO" "$DESTINO.anterior"
mv "$TEMP/app" "$DESTINO"

echo "▶ 4/4  Reiniciando o serviço…"
sudo systemctl restart marketplace
for _ in $(seq 1 30); do
  if curl -fs -o /dev/null http://127.0.0.1:5100/api/saude; then
    echo "✅ Publicado! $(curl -s http://127.0.0.1:5100/api/saude)"
    echo "   Abra: http://oracle-a1:5100"
    exit 0
  fi
  sleep 1
done
echo "❌ O serviço não respondeu. Veja o log:  journalctl -u marketplace -n 50"
exit 1
