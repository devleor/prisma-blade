# ADR 0012: RFC 002 aprovada: cartas, chefes, forja, mais monstros e impacto

- **Status:** Aceita
- **Data:** 2026-11-04

## Contexto
A RFC 002 (página com votos, artifact `72ca845f`) comparou o jogo com o que faz um roguelike dar certo. As maiores lacunas eram três: escolhas que não se combinam, um chefe só nas cinco fases e nada que passe de uma run para a outra. A RFC também listou as armas e as ofertas das salas para revisão. O usuário votou em 46 itens; parte dos votos foi registrada por um agente dele.

## Decisão
**Aprovado como está:** sete das oito armas, todas menos a Foice. Das relíquias, Coração, Brasa, Sede, Olho de Prisma e Espinhos.

**Mudanças:**
- **Foice:** arco de até ~220° (era 330°) e alcance de 140 a 150 px (era 150 a 165).
- **Pedra de Afiar:** +10% por cópia (era 15%).
- **Botas de Vento:** empilham, mas a recarga do dash nunca fica abaixo de 40% da recarga da arma.
- **Poção Rubra:** sai das ofertas. Cura competindo com poder é uma escolha chata; agora o guardião da fase a deixa cair (+40).
- **Golpe crítico:** chance base de 5% e dano ×1,8. A chance alta vem de cartas, para o crítico virar build.

**Monstros:**
- Ondas com orçamento ×1,8 e teto de 18 por onda.
- Hordas: 40% das salas de luta têm uma onda de 12 a 20 bichos frágeis.
- Os inimigos entram de três em três pelas bordas, com uma marca de tinta no chão antes.

**Impacto aprovado:**
- quebra de guarda maior com 破;
- quadro de impacto em negativo, com opção de desligar no menu e sem disparar em toda quebra quando a onda é grande;
- câmera lenta no último abate;
- tinta na tela ao apanhar, só nas bordas;
- vinheta de vida baixa;
- linhas de velocidade.

**Fontes:** Yuji Boku nos kanji e Caveat Brush nos números, ambas OFL. Elas substituem a Ink Free, que é do Windows e não pode ser distribuída.

**Áudio CC0:**
- Dash: *Erase/Escape* e swishes de bambu.
- Dash perfeito: sino e um sopro grave.
- Menu: *Yoiyami*.
- Luta: *Determined Pursuit*, ou *Dark Forest* se a orquestra destoar do traço.
- Chefe: *Boss_Koto*, se passar na escuta; senão *Basilisk*.
- Ambientes por bioma.

**Ordem do épico** `ef0f2f42`:
1. ondas e impacto;
2. cartas e selos;
3. um chefe por bioma, mais difíceis;
4. elites com afixos;
5. inimigos de bioma;
6. forja;
7. risco e loja;
8. Caderno;
9. Pressão;
10. Espada e Broquel;
11. música.

**A forja:** reforja a espada atual, começa de um molde e só ajusta. Os números ficam presos às faixas das 8 armas.

## Consequências
- Cada item vira uma story do épico. Cada um ganha o seu ADR quando for feito.
- Pedidos que vieram depois da votação também entram no épico:
  - a Aranha-mãe solta muitas aranhinhas de uma vez, como uma aranha de verdade;
  - os cortes da espada ficam no chão por um tempo;
  - pedras que monstros e chefes destroem, em lugares estratégicos.
