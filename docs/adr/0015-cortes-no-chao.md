# ADR 0015: Cortes da espada ficam no chão

- **Status:** Aceita
- **Data:** 2026-11-04

## Contexto
O usuário pediu que os golpes da espada fiquem marcados no chão por um tempo, onde acertaram.

## Decisão
- Cada `Ev.Hit` risca um corte no chão (`Stains.Cut`):
  - cruza o golpe, com ±0,3 rad de variação;
  - tem forma de lente curva, fina nas pontas;
  - leva um fio claro de papel na borda, como a beira do corte pegando luz.
- O tamanho depende do golpe: ~58 px no leve e ~92 px no pesado. No crítico o corte é vermelho.
- O corte se desenha em 0,07 s, fica 7 s e some nos últimos 2,5 s. Há no máximo 40 cortes, e eles não ficam na sala quando você sai: só as manchas secas ficam.
- Os cortes ficam numa camada própria, filha de `Stains`, desenhada em lote. Como desbotam a cada quadro, a camada redesenha todo quadro enquanto há cortes. As manchas secas só redesenham quando ganham uma nova.
- O recorte em orelhas do `InkBatch` passou a descartar pontos colineares. Antes, um polígono com um ponto no meio de uma aresta reta era rejeitado.

## Consequências
- Custo medido no teste de estresse: ~0,4 ms por quadro com 40 cortes.
- Conferido na `GameCheck` (o corte aparece e continua no chão depois) e nas capturas `ImpactShots`, tudo sem som.
