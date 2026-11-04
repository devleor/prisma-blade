# ADR 0010: Clima desenhado em código

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
A chuva do Ecos da Fenda é feita em código, sem arte de terceiros; o usuário pediu o mesmo aqui.

## Decisão
- `scripts/Weather.cs` (`Node2D`, ZIndex 50) desenha o clima do bioma:
  - Chuva: traços de caneta inclinados pelo vento, que viram um anelzinho com respingos no chão. Na floresta há relâmpago (flash e tremor): o primeiro após 6–12 s, depois a cada 8–16 s, com trovão sintetizado que dura ~1,5 s.
  - Garoa no pântano.
  - Cinza caindo e brasas HDR subindo nas cavernas.
  - Pontos de luz no cristal.
- Fica sob o HUD e congela na pausa.

## Consequências
- O clima é checado na `GameCheck` (gotas, respingos e som sintetizado).
