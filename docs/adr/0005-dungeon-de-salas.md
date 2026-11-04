# ADR 0005: Run como dungeon de salas com portões e guardião

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O usuário pediu um dungeon crawler: salas com ondas, um guardião que abre os portões, mapa para escolher o caminho.

## Decisão
- `Dungeon.Make` gera uma grade de salas por fase, a partir da semente: Início, Luta, Elite, Tesouro e Chefe.
- Ao entrar numa sala de luta, os portões fecham. As ondas vêm, e o líder (guardião) abre os portões ao morrer. Andar até um portão aberto troca de sala.
- O chefe abre o portal para a próxima fase. O minimapa marca 鬼 (chefe), 宝 (tesouro) e ★ (elite).
- Altares de espada e relíquia ficam pendentes (`RoomNode.Pending`) até o jogador escolher, mesmo saindo da sala.

## Consequências
- Cada run tem caminho próprio; o jogador escolhe risco contra recompensa.
- A cena de testes precisa trocar de sala pelo evento `RoomEnter` (ou redesenhar à mão, como faz `BiomeShots`).
