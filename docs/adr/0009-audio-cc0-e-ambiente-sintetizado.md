# ADR 0009: Áudio: só CC0 com fonte anotada; ambiente sintetizado

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O usuário pediu sons de espada melhores e o efeito de chuva usado no Ecos da Fenda. Áudio de terceiros precisa de licença limpa.

## Decisão
- Só sons **CC0**, cada um com origem, autor e licença em `docs/ASSET_SOURCES.md`. Oito sons de golpe vieram do Ecos da Fenda (artisticdude, Kenney, rubberduck), com a fonte original conferida lá.
- Cada arma tem voz própria no golpe (tom e camadas), e o acerto soa conforme o alvo: pedra, metal ou corte. Há também bloqueio, rebatida, parede e quebra de guarda.
- Chuva, garoa, vento de cinza e trovão são **sintetizados em código** (`Weather.cs`: `AudioStreamGenerator`, ruído filtrado, gotas, ruído marrom). Não há sample e não há licença a cuidar.

## Consequências
- Som novo de terceiros só entra com a linha na tabela de fontes.
