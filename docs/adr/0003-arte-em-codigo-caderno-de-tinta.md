# ADR 0003: Arte inteira desenhada em código, no estilo caderno de tinta

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O usuário pediu estilo desenhado à mão, espadas e inimigos diferentes. Gerar sprites (PixelLab ou outro gerador) traria custo por imagem, licença a conferir e animação limitada. Os personagens são vistos de cima e simples o bastante para serem montados com formas.

## Decisão
- Nenhum sprite. Tudo é desenhado em modo imediato (`_Draw`) com o pincel de `scripts/Ink.cs`: traço duplo, tinta que ferve 10x por segundo, aquarela fora do registro, hachura na sombra.
- Só a magia usa cor HDR (acima de 1) e brilha; o resto é tinta fosca.
- As regras e o passo a passo de cada desenho ficam em `docs/ART_STYLE.md`, mantido em dia a cada coisa nova.
- Nenhuma chave de serviço de arte entra no repositório.

## Consequências
- Coisa nova (inimigo, arma, bioma) custa só código, e herda o estilo de graça.
- Cada forma precisa de semente própria para o tremor não piscar.
- Conferência visual é por captura (`Snapshot.tscn`, `BiomeShots.tscn`).
