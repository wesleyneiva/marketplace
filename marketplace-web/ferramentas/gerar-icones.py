#!/usr/bin/env python3
"""Gera src/app/shared/icones.ts com os desenhos dos ícones Lucide (https://lucide.dev, licença ISC).

Só entram no sistema os ícones listados em ICONES (o resto do pacote não vai para o navegador).
Para usar um ícone novo: acrescente o nome na lista (procure em https://lucide.dev/icons) e rode
    python3 ferramentas/gerar-icones.py
de dentro de marketplace-web. O script baixa o pacote lucide-static numa pasta temporária.
"""
import pathlib, re, subprocess, sys, tarfile, tempfile

VERSAO = "0.460.0"
ICONES = sorted(set("""
arrow-down-to-line arrow-left arrow-left-right arrow-up-from-line ban banknote bot building
building-2 calendar calendar-clock chart-column chart-line check chevron-down chevron-right chevron-up
circle circle-alert circle-check circle-dollar-sign circle-x clipboard-list clock cloud-rain
cloud-sun-rain copy credit-card download eye file-text flag flask-conical folder hand-coins history
import key-round landmark layout-dashboard lightbulb loader-circle lock lock-open log-out map-pin
menu message-circle minus notebook-pen octagon-alert package package-minus package-plus pause
pencil play plug plus printer qr-code receipt refresh-cw scale send settings shopping-bag
shopping-cart smartphone snowflake sparkles store sun tag trash-2 trending-up triangle-alert truck
undo-2 users x moon monitor panel-left-close panel-left-open receipt-text mail boxes search
layout-grid wrench ticket repeat
""".split()))

destino = pathlib.Path(__file__).resolve().parent.parent / "src/app/shared/icones.ts"
with tempfile.TemporaryDirectory() as tmp:
    subprocess.run(["npm", "pack", f"lucide-static@{VERSAO}", "--silent"], cwd=tmp, check=True, capture_output=True)
    with tarfile.open(next(pathlib.Path(tmp).glob("*.tgz"))) as t:
        t.extractall(tmp, filter="data")
    pasta = pathlib.Path(tmp) / "package/icons"
    linhas = []
    for nome in ICONES:
        arq = pasta / f"{nome}.svg"
        if not arq.exists():
            sys.exit(f"Ícone não existe no Lucide {VERSAO}: {nome}")
        corpo = re.search(r"<svg[^>]*>(.*)</svg>", arq.read_text(), re.S).group(1)
        corpo = re.sub(r"\s*\n\s*", "", corpo).replace(" />", "/>")
        linhas.append(f"  '{nome}': '{corpo}',")

destino.write_text(
    "// GERADO por ferramentas/gerar-icones.py — não edite à mão.\n"
    f"// Desenhos do Lucide {VERSAO} (https://lucide.dev, licença ISC): grade 24×24, só traço.\n"
    "export const ICONES: Record<string, string> = {\n" + "\n".join(linhas) + "\n};\n"
)
print(f"{len(ICONES)} ícones → {destino}")
