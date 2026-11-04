# ADR 0011: Verificação: testes sempre sem som

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O usuário pediu que o jogo seja sempre testado sem som. Só a execução para ele jogar tem áudio.

## Decisão
- Toda execução de teste usa `--audio-driver Dummy`: a `GameCheck` headless, o `Snapshot` e o `BiomeShots` com janela, e o `tools/verify.mjs`.
- O verificador do projeto é `node tools/verify.mjs`: build, xUnit e a `GameCheck`. Com `--gpu`, ele também gera as capturas.

## Consequências
- Nenhum teste faz barulho na máquina do usuário.
