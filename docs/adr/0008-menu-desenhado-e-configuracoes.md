# ADR 0008: Menu desenhado e configurações persistidas

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O usuário pediu um menu no mesmo estilo, com animação de desenho ao selecionar, tremores e uma tela de configurações.

## Decisão
- `scenes/Menu.tscn` é a cena principal. `scripts/Menu.cs` é um Control que desenha o título 光刃, a pausa 休止 e as configurações 設定, na mesma folha de caderno.
- A seleção ganha uma pincelada vermelha que varre o item, um sublinhado e uma espadinha; a página dá um tranco. Confirmar joga gotas que viram manchas.
- `scripts/Settings.cs` guarda volume geral, de música e de efeitos (barramentos próprios `Music` e `SFX`), força do tremor e tela cheia em `user://settings.cfg`.
- O Esc abre a pausa pelo `_UnhandledInput`. O jogo pausa a árvore, e o menu roda com `ProcessMode.Always`.

## Consequências
- Os testes desligam a gravação (`Settings.Persist = false`) para não sujar a configuração do jogador.
