# Prisma Blade

Hack-and-slash top-down original em Godot 4.7 .NET (C#). Você é um quadrado com uma espada pesada numa arena
de pedra vista inteira de cima; triângulos, círculos, hexágonos e um Monólito querem te estilhaçar.

## Controles

| Ação | Teclado + mouse | Controle |
|---|---|---|
| Mover | WASD / setas | analógico esquerdo |
| Mirar | mouse | analógico direito |
| Combo leve (3 golpes) | LMB / J | X |
| Golpe pesado (segure para carregar) | RMB / K | Y |
| Dash | Espaço / Shift | A / RB |
| Estilhaçar (gasta 50 de foco) | Q / E / L | B / RT |
| Giro especial (custa 10 de vida) | F / botão do meio | LB |
| Reiniciar | R / Enter | Start |

## Mecânicas

- **Peso e inércia**: a velocidade acelera e freia suave; cada golpe dá um avanço (lunge); hitstop e tremor de tela no impacto.
- **Buffer e cancelamentos**: um botão apertado até 10 frames antes vale; o dash cancela o preparo e a recuperação de um golpe.
- **Golpe pesado**: carga de até 0,75 s; com carga cheia, o golpe causa mais dano, empurra mais e fratura 2x.
- **Fratura**: o 3º golpe do combo, o golpe pesado e tiros rebatidos marcam o inimigo (até 3 marcas).
- **Dash PERFEITO**: dar dash nos primeiros 8 frames antes de um ataque te acertar deixa o tempo lento, fratura quem estiver perto e dá 25 de foco.
- **Estilhaçar**: todo inimigo fraturado explode; cada explosão fratura e fere os vizinhos, que explodem em seguida (reação em cadeia).
- **Rebater**: a lâmina rebate os tiros inimigos de volta.
- **Armadura**: o Hexágono e o Monólito só cambaleiam depois que a guarda (barra amarela) quebra.
- **Paredes e pilares**: inimigo arremessado com força contra a pedra toma dano e fica tonto; a investida do triângulo
  ou do Monólito que termina numa parede atordoa por muito tempo, até o chefe através da guarda. Pilares bloqueiam
  corpos e tiros (atrair investidas para a pedra é tática).
- **Boliche**: um inimigo arremessado derruba quem estiver no caminho, e o derrubado pode seguir derrubando outros.
- **Giro especial**: golpe de 360° com invencibilidade enquanto gira. Custa vida, mas o custo fica como vida
  recuperável (barra listrada): cada acerto devolve 2, e levar um golpe perde o que faltava.
- **Combo**: contador de acertos que zera depois de 2,5 s sem acertar ou ao levar dano; o maior combo aparece na vitória.
- 4 ondas e o chefe **Monólito** (investidas em cadeia, rajadas radiais, invoca triângulos; mais rápido abaixo de 50% de vida).

## Estrutura

- `src/Prisma.Sim`: regras sem Godot, determinísticas, a 60 Hz (`Tune.cs` tem todos os números).
- `tests/Prisma.Sim.Tests`: testes xunit das regras.
- `scripts/`: cena Godot, com desenho vetorial e glow HDR (`Main`), partículas (`Fx`), HUD, áudio (`Sfx`) e controles.
- `assets/audio` e `assets/textures`: sons, músicas, texturas de pedra e partículas, todos CC0; a origem de cada um está em `docs/ASSET_SOURCES.md`.
- `node tools/verify.mjs [--gpu]`: build, testes e GameCheck headless; com `--gpu`, também salva screenshots em `test-results/`.
