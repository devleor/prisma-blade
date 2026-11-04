# ADR 0014: Impacto: crítico, 破, quadro de impacto, câmera lenta, efeitos de tela e fontes OFL

- **Status:** Aceita
- **Data:** 2026-11-04

## Contexto
A RFC 002 (ADR 0012) aprovou cinco coisas para os golpes terem mais peso:
- um crítico fraco na base, que vira build pelas cartas;
- uma quebra de guarda maior;
- um quadro de impacto que se pode desligar e que não dispara em toda quebra numa onda grande;
- uma câmera lenta no último abate;
- tinta na tela (só nas bordas), vinheta de vida baixa e linhas de velocidade.

Também aprovou trocar a fonte do Windows (Ink Free, que não pode ser distribuída) por fontes livres.

## Decisão
- **Crítico (Sim):** chance de 5% (`Relics.CritChance`, lida por `Player.CritChance`), dano ×1,8 e +3 quadros de pausa no golpe.
  - O sorteio é um hash de (semente da run, inimigo, golpe, quadro). Não consome o `Rng`, então não muda o que mais a run decide, e o replay da mesma run repete os mesmos críticos.
  - A simulação emite `Ev.Crit` logo antes do `Ev.Hit`. O `Ev.Hit` continua igual.
- **Visual do crítico:** número vermelho grande com 会心, anel vermelho e som de aço. No golpe pesado crítico, quadro de impacto.
- **Quebra de guarda:** 破 vermelho carimbado sobre o inimigo, no lugar do "QUEBROU!".
- **Quadro de impacto:** um shader de negativo de tinta sobre a tela inteira por ~75 ms.
  - No máximo um por segundo. Em Configurações → "Quadro de impacto" (salvo em `video/impact_frames`).
  - Dispara no crítico pesado, na quebra de guarda de inimigo grande ou com 6 inimigos ou menos, e na vitória.
- **Câmera lenta:** quando a sala se limpa (`GatesOpen`) e na vitória, `Engine.TimeScale = 0,25` por 550 ms reais, com zoom de até 12% puxando para o último abate.
  - O menu de pausa e a saída da cena restauram a velocidade.
- **Tela:** tinta nas bordas ao apanhar, vinheta em degradê abaixo de 30% de vida e linhas de velocidade no dash e no dash perfeito.
- **Fontes:** Caveat Brush (texto e números) e Yuji Boku (kanji), OFL, em `assets/fonts` com as licenças. As fontes do sistema ficam como reserva.

## Consequências
- As cartas de crítico (story de cartas e selos) só precisam aumentar `Player.CritChance`.
- Conferido assim:
  - testes da simulação de taxa (~5%), dano e replay;
  - checagens no GameCheck de fontes empacotadas, crítico, 破, opção e limite do quadro de impacto, e câmera lenta voltando ao normal;
  - capturas em `scenes/tests/ImpactShots.tscn`, tiradas sem som.
- Desempenho no teste de estresse: 3,65 ms por quadro, sem coletas gen2.
