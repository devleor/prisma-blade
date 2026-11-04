# ADR 0017: O vídeo de devlog é filmado pelo próprio jogo (Movie Maker)

- **Status:** Aceita
- **Data:** 2026-11-04

## Contexto
O devlog 1 precisava mostrar o jogo, a ideia e os bugs resolvidos, com narração em cima. Gravar a tela jogando à mão engasga, não se repete e não acompanha a fala. O usuário pediu:
- nada de legenda nem texto explicativo na tela: a voz explica;
- só a espada de duas mãos (Montante) nas cenas;
- animações "desenhando vetores e explicando em código";
- a comparação com o DOOM;
- transições caprichadas;
- e o vídeo no ritmo da narração (gerada no ElevenLabs).

## Decisão
- **Cena de filmagem:** `scenes/video/Devlog.tscn` (`scripts/Video/Devlog.cs`), gravada pelo Movie Maker do Godot (`--write-movie`, 60 fps fixos, um quadro por vez e sem engasgo).
  - O herói anda sozinho (`Main.Pilot`), sempre com o Montante, e invulnerável.
  - As salas são reabastecidas para nenhuma cena ficar vazia.
  - `Main.ZoomMul` e `Main.Focus` fazem os closes.
  - `InkBatch.Enabled` desliga o lote de tinta e mostra o "antes" do travamento.
- **Ritmo pela voz:** a narração fica **fora do repositório**: é a voz clonada de uma pessoa. São dois arquivos:
  - um WAV;
  - um JSON com o início e o fim de cada frase, obtido transcrevendo o WAV com o Whisper.
  - Cada cena se prende à frase pelas primeiras palavras (`Say("O DOOM")`). Uma narração regravada re-cronometra o vídeo sozinha.
  - A voz toca dentro da cena, e o Movie Maker grava o áudio no AVI já sincronizado, sem ffmpeg. Os sons do jogo ficam ~14 dB abaixo da voz.
- **Quadro-negro de tinta:** `scripts/Video/Board.cs`. São animações desenhadas traço a traço com o próprio `Ink`, com o código real sendo digitado ao lado:
  - a linha que treme;
  - os 7 mil pedidos à placa de vídeo;
  - a cascata de quadros;
  - linha → normais → triângulos → um pedido só;
  - 133 → 3,5 ms;
  - o mapa de linhas e a divisão do DOOM, com o ritmo fixo de 35 por segundo;
  - o relógio do flash de impacto, com o trecho real do `Hud.cs` antes e depois;
  - o código-fonte aberto.
  - O texto do código encolhe para caber no painel.
- **Transição:** `scripts/Video/Wipe.cs`. Uma pincelada de tinta cobre a tela inteira no quadro exato de cada corte e depois sai, revelando a cena seguinte. Os cortes são conhecidos de antemão, então a pincelada começa antes deles.
- O vídeo é sempre renderizado **sem som nos alto-falantes** (`--audio-driver Dummy`). O áudio vai só para o arquivo.

## Consequências
- Para refazer o vídeo basta rodar a cena. Trocar a narração exige transcrever de novo (o Whisper no venv de rascunho) e não exige mexer no código, a não ser que o começo de uma frase mude.
- `Board` e `Wipe` servem para os próximos devlogs.
- Lição da geração de voz: copiar o roteiro do terminal cortou trechos nas quebras de linha, e a voz "pulou" palavras. O roteiro passa a ser entregue em arquivo (`roteiro_v4.txt`), e a transcrição confere cada frase antes da montagem.
