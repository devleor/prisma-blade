# Estilo de arte: caderno de tinta

O Prisma Blade é desenhado inteiro em código, sem nenhum sprite. A ideia é parecer uma página de caderno de esboços
onde alguém desenhou a luta com nanquim e aquarela barata: linhas tremidas, cor que vaza do traço, hachura na sombra
e manchas de tinta que ficam no papel.

Tudo que está aqui vive em `scripts/Ink.cs` (o pincel), `scripts/MainArt.cs` (personagens, armas, criaturas,
altares, portões), `scripts/Floor.cs` (o chão de cada sala), `scripts/Stains.cs` (a tinta que fica no mapa)
e `scripts/Hud.cs` (HUD, banners e o anúncio de rodada).

## As cinco regras

1. **Tinta que ferve (line boil).** Toda linha é redesenhada com um tremor novo a cada 6 quadros (10 vezes por
   segundo), como animação feita à mão. `Ink.Boil` é o passo atual; `Ink.J(seed, i, amp)` dá o tremor do ponto `i`.
   Cada forma tem a sua **semente**: a irregularidade é dela e não pisca entre os quadros.
2. **Traço duplo.** `Ink.Stroke` passa duas vezes: uma linha firme e outra mais fina e solta por cima, como caneta.
   Formas fechadas passam um pouco do ponto de partida; linhas abertas "escorrem" além das pontas.
3. **Aquarela fora do registro.** `Ink.Fill` pinta a cor 1 a 2 px deslocada do traço, como impressão barata.
4. **Luz do alto à esquerda.** O lado de baixo à direita ganha hachura (`Ink.ShadeCrescent` + `Ink.Hatch`).
   Nunca sombra lisa: sempre linhas paralelas a ~50°.
5. **A tinta fica.** Abates deixam um borrão da cor do inimigo com gotas na direção do golpe, acertos respingam,
   bombas e esmagadas deixam fuligem. Cada sala guarda as suas manchas (até 420; as mais velhas secam).

## Paleta

| Papel | Uso | Cor |
|---|---|---|
| Papel | chão, faixas de banner | `Ink.Paper` (0.93, 0.89, 0.79) |
| Nanquim | todo contorno e texto | `Ink.Line` (0.11, 0.09, 0.08) |
| Capa do andarilho | o herói, o vermelho do anúncio | (0.72, 0.16, 0.14) |
| Perigo | avisos de ataque, círculo da bomba | (0.82, 0.16, 0.12) |
| Magia (HDR > 1) | fratura, estilhaço, runas, portal | (1.3, 1.15, 1.8) |

Só a magia usa cor HDR (acima de 1), e por isso só ela brilha (glow com limiar 1.05). Todo o resto é tinta fosca.
Cada inimigo tem uma cor de aquarela (`Main.KindColor`) que também é a cor da mancha que ele deixa.

## Como cada coisa é desenhada

Vista de cima, com o personagem virado para onde mira. `f` é o vetor da frente e `s` o lado; todas as peças são
posicionadas como `pos + f * a + s * b`.

**O andarilho (herói).** De baixo para cima: dois pés ovais que alternam com a distância andada; a capa (7 pontos)
que sai dos ombros e fica para trás atrasada pela velocidade, com uma onda; a túnica (elipse); a espada e a mão; o
capuz com a ponta para trás e uma abertura escura na frente com um brilho de olho.

**Espadas.** Cada uma tem silhueta própria em `DrawWeapon`:
- Espada Longa: lâmina que afina até a ponta, guarda reta, cabo de couro e pomo.
- Montante: lâmina larga com sulco central, guarda grande com volutas nas pontas.
- Adagas Gêmeas: lâmina curta, uma em cada mão (a segunda descansa na mão de trás).
- Foice: cabo de madeira e lâmina em crescente na ponta.
- Katana do Eco: lâmina fina com curva suave, tsuba redonda, cabo trançado.
- Lâmina Rúnica: aço escuro com runas que brilham (HDR).

O rastro do golpe é uma aquarela clara na cor da espada, com a borda de fora passada a nanquim.

**Criaturas.** Cada uma é montada com blobs (`Ink.Blob`) e polígonos, em `MainArt.cs`:
- Chifrudo: corpo oval, cabeça à frente, chifres creme, cerdas nas costas, perninhas que andam.
- Olho Errante: bola com tentáculos que flutuam para longe do herói; o olho segue o jogador.
- Golem: pedra irregular (blob com muita irregularidade), musgo, rachadura, punhos e olhos em brasa.
- Diabrete: bolinha vermelha, chifres, asas de morcego que batem, rabo em ziguezague.
- Bombardeiro: goblin de orelhas pontudas com bolsa de bombas; ergue uma bomba acesa antes de jogar.
- Escudeiro: elmo com viseira, tabardo e escudo curvo na frente; o escudo abaixa quando ele está aberto.
- Ogro (grande): costas largas e peludas, cabecinha com presas à frente, punho de um lado e clava cravejada do
  outro, que sobe durante a preparação e desce no golpe; o chão treme e fica pegada a cada passo.
- Aranha-mãe (grande) e aranhinhas: oito patas articuladas que andam aos pares, abdômen com ampulheta vermelha,
  cacho de olhos; a mãe carrega ovos nas costas e incha uma bolsa de ovos antes de chocar.
- Ídolo Monólito: losango de pedra entalhado, rachaduras que viram brasa na fase 2, olho que se abre e queima.

O elite ganha coroa dourada e uma aura rabiscada. O atordoado ganha estrelinhas desenhadas, o que está
queimando ganha chama, e o que leva golpe incha um pouco por um instante.

**Chão e paredes.** Lajotas de largura irregular em fileiras alternadas, cantos gastos hachurados, rachaduras,
manchas de aquarela no papel, tufos de grama e pedrinhas perto das paredes e dos pilares. Parede de tijolo com
hachura na sombra. Cada sala sorteia o chão pela semente dela.

**Biomas** (um por fase, `Floor.cs`, papel tingido para cada um):
- 牢 Masmorra: lajotas, tijolos e tufos (o chão acima).
- 森 Floresta Chuvosa: terra e musgo hachurado, poças com reflexo, folhas caídas, raízes saindo das bordas;
  parede de sebe com troncos. Chuva.
- 灰 Cavernas de Cinza: basalto hexagonal com rachaduras de brasa (HDR, brilham), montes de cinza, cascalho;
  parede de rocha com veios de brasa. Cai cinza e sobem fagulhas.
- 沼 Pântano: lama, charcos hachurados com vitórias-régias, juncos e taboas em volta; paliçada de madeira podre.
  Garoa.
- 晶 Templo de Cristal: treliça em losango, círculos de runas, cristais brilhando junto às paredes; blocos claros
  com veios de cristal. Pontos de luz flutuando.

**Clima** (`Weather.cs`, por cima de tudo, abaixo do HUD): a chuva é feita de traços de caneta inclinados pelo
vento que caem até um ponto do chão e viram um anelzinho com dois respingos; na floresta, relâmpago de vez em
quando (flash e tremor). Feito em código, como a chuva do Ecos da Fenda.

**Armas novas.** Lança: haste de freixo, amarração vermelha, ponta em folha. Martelo de Guerra: cabo de madeira,
cabeça de ferro com cinta de latão e um espigão atrás.

**Portões.** Aberto: passagem escura com uma seta e a marca do que tem atrás (? desconhecido, 宝 tesouro,
鬼 chefe, ★ elite). Fechado: grade de ferro.

## Letras

- Texto do jogo: **Caveat Brush** (`Ink.Hand`) com halo de papel, e kanji em **Yuji Boku** (`Hud.Kanji`). As duas são OFL e vêm junto com o jogo, em `assets/fonts`. As fontes do Windows ficam só de reserva.
- **Crítico:** número grande em vermelho (`CritCol`) com 会心 em cima. Ele aparece maior e encolhe, e solta um anel vermelho.
- **Quebra de guarda:** um 破 vermelho carimbado sobre o inimigo, do tamanho dele. Cai grande, assenta e some.
- **Quadro de impacto:** um quadro em negativo de tinta, em que o claro vira tinta, o escuro vira papel e o vermelho continua vermelho. Aparece no máximo uma vez por segundo, pode ser desligado em Configurações e só dispara nos golpes grandes (crítico pesado, quebra de guarda de inimigo grande ou de sala com poucos inimigos, vitória).
- **Cortes no chão:** cada golpe risca uma lente curva de tinta cruzando o golpe, com um fio de papel na borda. O corte é vermelho no crítico, fica 7 s e desbota (ADR 0015).
- **Tela:**
  - tinta respingada nas bordas quando o herói apanha (só nas bordas);
  - vinheta vermelho-escura que pulsa abaixo de 30% de vida;
  - linhas de velocidade das bordas para dentro no dash e no dash perfeito;
  - câmera lenta (¼) de meio segundo, com a câmera se aproximando do último abate da sala.
- Anúncios: kanji em pincel (`Hud.Kanji`: HGGyoshotai, Yu Mincho...). 第一波 (onda um), 守護者 (guardião),
  決戦 (chefe), 開門 (portões abertos), 第二層 (fase dois), 完璧 (perfeito), 勝利 (vitória), 敗北 (derrota).
- O anúncio de rodada: uma pincelada vermelha cresce da esquerda para a direita em 0,18 s, gotas voam, o kanji
  bate na tela grande (2,6x) e assenta com um pequeno rebote, a câmera treme, e o nome é escrito embaixo.

## Menu

O menu (`scripts/Menu.cs`) é a mesma folha de caderno, sem tela de fundo do jogo:
- O título 光刃 ("lâmina de luz") bate na tela grande e assenta com rebote, sobre uma pincelada vermelha que cresce.
- O item escolhido ganha uma pincelada vermelha que varre da esquerda para a direita em 0,16 s, um sublinhado
  rabiscado e uma espadinha a nanquim apontando para ele; a página dá um tranco a cada troca.
- Confirmar joga gotas de tinta que caem e viram manchas que ficam no papel.
- Configurações (設定): barras hachuradas com uma gota de tinta como botão. Mexer no tremor já treme a página
  na força nova. Pausa (休止, Esc) é a mesma folha menor, sobre o jogo escurecido.

## Impacto

Tremor de câmera aleatório (trauma ao quadrado), tranco direcional na direção do golpe, hitstop da simulação,
flash de tela nos impactos grandes (pesado carregado, estilhaço, explosão, chefe, portões abertos) e o inchaço
do inimigo atingido.

## Ao adicionar algo novo

- Dê uma semente própria a cada forma (`e.Id * 131 + n` nas criaturas); nunca reaproveite a de outra peça.
- Monte com `Ink.Shape(pontos, cor, semente, largura, hachura)`; contorno de 2,5 a 3 para corpos, 1,5 para detalhes.
- Cor nova de inimigo: tom de aquarela meio apagado (saturação média), nunca neon. Brilho só para magia.
- Desenhe pelo `Ink` ou pelos `Draw*` da própria classe: dentro do `_Draw` tudo cai num lote só (`InkBatch`, ADR 0013).
  Chamada direta ao motor ali dentro precisa de `InkBatch.Flush()` antes. Depois de arte grande, rode
  `scenes/tests/Stress.tscn` (sem som) e confira o tempo por quadro.
- **Marca de entrada:** onde um inimigo vai entrar, dois anéis de tinta na cor dele se fecham por 0,66 s, e fica uma gota.
- Confira com a cena `scenes/tests/Snapshot.tscn` (`node tools/verify.mjs --gpu`), que salva capturas em
  `test-results/`, sempre sem som (`--audio-driver Dummy`).
