# ADR 0007: Monstros grandes no mesmo traço: Ogro e Aranha-mãe

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O usuário pediu mais inimigos e monstros maiores. Monstro grande só é justo se o golpe dele for legível.

## Decisão
- **Ogro**: preparação de 56 quadros. O círculo do golpe trava a mira aos 22 quadros, então dá para esquivar depois de ver. Também tem uma investida de 600 por 48 quadros.
- **Aranha-mãe**: põe ovos que chocam em aranhinhas (no máximo 5 vivas, 2 por ninhada) e morde de perto.
- Monstros grandes emitem `Ev.Stomp` a cada passada: tremor pela distância e pegada no chão.
- Entram no pool só a partir do tier 7/8, para não aparecer na primeira fase.

## Consequências
- Cada ataque grande tem telegrafia desenhada (círculo, linha, bolsa de ovos inchando).
- Os números moram em `Enemies.cs` e são testados em `BigEnemyTests.cs`.
