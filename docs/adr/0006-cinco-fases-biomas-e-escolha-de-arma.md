# ADR 0006: Cinco fases, um bioma por fase e três espadas ao descer

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O usuário pediu mais fases, biomas e armas diferentes para pegar entre as fases.

## Decisão
- `Dungeon.Floors = 5`. `Dungeon.BiomeOf(fase)`: 牢 Masmorra, 森 Floresta Chuvosa, 灰 Cavernas de Cinza, 沼 Pântano, 晶 Templo de Cristal.
- O bioma muda o chão, a parede, o tom do papel (`Floor.cs`) e o clima (`Weather.cs`). O anúncio da fase mostra o kanji do bioma.
- Ao descer pelo portal, a sala inicial oferece **três espadas diferentes que o jogador não tem**, sorteadas pela semente da run (`RaiseWeaponAltars`).
- Duas espadas novas: **Lança** (estocadas estreitas de 178–200 px) e **Martelo de Guerra** (dois golpes lentos que quebram guarda: 55/80 de postura).

## Consequências
- Toda fase começa com uma decisão de build.
- Bioma é só visual e clima; a dificuldade sobe pela fase (tier do pool de inimigos).
