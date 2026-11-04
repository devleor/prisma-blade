# ADR 0002: Simulação determinística separada do motor

- **Status:** Aceita
- **Data:** 2026-11-01

## Contexto
Combate de ação precisa de números ajustáveis e testáveis sem abrir o Godot, e de um jeito de a tela reagir ao que acontece sem que a tela mande no jogo.

## Decisão
- `src/Prisma.Sim` não conhece o Godot. `World.Step(Inputs)` avança um quadro de 60 Hz (`Tune.Dt`); todo número de sensação fica em `Tune.cs`.
- A Sim conta o que aconteceu como uma lista de `GameEvent(Ev, Pos, A, I)` por quadro (Swing, Hit, Kill, Stomp, GuardBreak, NextFloor...). A tela (`scripts/Main.cs`, `React`) transforma cada evento em som, tremor, partículas e manchas.
- Sorteios usam a semente da run, então a mesma semente gera a mesma dungeon.

## Consequências
- 104 testes rodam em ~0,2 s sem motor.
- Toda novidade de jogo nasce na Sim com teste, e a tela só reage. Efeito que precisa de informação nova pede um evento novo (ex.: `Ev.GuardBreak` para o som de guarda quebrada).
