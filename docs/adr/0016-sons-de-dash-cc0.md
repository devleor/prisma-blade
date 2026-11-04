# ADR 0016: Sons de dash e de dash perfeito (CC0)

- **Status:** Aceita
- **Data:** 2026-11-04

## Contexto
Na RFC 002 o usuário votou nos sons CC0 da pesquisa: no dash, *Erase/Escape* (Fupi) com swishes de bambu (qubodup); no dash perfeito, *Pleasing bell* (Spring Spring) com *Wind, hit, time morph* (qubodup). Os sons antigos eram genéricos, do Kenney.

## Decisão
- **Dash:** toca um de três swishes de bambu (`dash1-3.wav`, sorteado) e, por baixo e mais baixo, o ar do *Erase/Escape* (`dash_air.wav`).
- **Dash perfeito:** toca o sino (`perfect_bell.wav`) junto com o "tempo dobrando" (`perfect_morph.wav`, o `slomo1` do pacote).
- Os arquivos baixados (FLAC e WAV) ficaram fora do repositório, numa pasta isolada. Foram convertidos para WAV mono de 16 bits, aparados no início, com fade no fim e normalizados.
  - A conversão usou um ambiente Python descartável com `soundfile`, porque o Godot não importa FLAC.
- `dash.ogg` e `perfect.ogg` (Kenney) saíram. As fontes e licenças estão em `docs/ASSET_SOURCES.md`.

## Consequências
- A `GameCheck` confere as variações carregadas: 3 de dash, 1 de ar, 1 de sino e 1 de morph.
- O zumbido da Espada Rúnica usa o grupo `perfect` e agora soa como um sino agudo.
