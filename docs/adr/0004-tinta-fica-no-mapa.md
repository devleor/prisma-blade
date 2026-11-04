# ADR 0004: A tinta fica no mapa, por sala

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O usuário pediu que a tinta ficasse no chão: a sala deve contar a luta que aconteceu nela.

## Decisão
- `scripts/Stains.cs` guarda manchas por sala (fase e posição): borrão na cor do inimigo morto com gotas na direção do golpe, respingos de acerto, fuligem de bomba, pegadas de monstro grande.
- No máximo 420 por sala; as mais velhas secam primeiro. A triangulação é validada uma vez ao criar a mancha.

## Consequências
- Voltar a uma sala mostra a luta antiga.
- O custo de desenho é limitado pelo teto.
