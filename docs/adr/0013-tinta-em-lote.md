# ADR 0013: Tinta em lote: uma chamada de desenho por camada, redesenho uma vez por quadro

- **Status:** Aceita
- **Data:** 2026-11-04

## Contexto
Com ondas maiores, uma sala de 30 inimigos com chuva rodava a **8 fps (133 ms por quadro)**. A medição (`scenes/tests/Stress.tscn` com `scripts/Prof.cs`) mostrou quatro coisas:
- A simulação custava 0,02 ms por passo; não era ela.
- Cada traço de tinta virava uma chamada de desenho no Godot: **7.140 chamadas por quadro**. Mesmo sem inimigos eram 5.000, porque o chão desenhado é repetido pelo motor a cada quadro.
- O `QueueRedraw` ficava no passo de física. Com o quadro lento, o Godot rodava até 8 passos para alcançar o tempo, e o quadro seguinte ficava ainda mais lento (efeito cascata).
- Os arrays grandes criados a cada quadro iam para o heap de objetos grandes (LOH). Isso forçava coletas gen2 e causava engasgos.

## Decisão
- `scripts/InkBatch.cs`: entre `Begin` e `End`, cada traço, aguada, hachura, círculo e retângulo vira triângulos em C#:
  - a linha vira uma faixa com junção em quina, com limite para cantos agudos;
  - a aguada é recortada em orelhas (ear clipping);
  - tudo vai ao motor em `RenderingServer.CanvasItemAddTriangleArray`.
- Os lotes são cortados a cada 4.000 vértices. Os arrays ficam abaixo de 85 KB e não vão para o LOH.
- `Main`, `Floor`, `Stains` e `Weather` desenham em lote. As suas chamadas `DrawCircle`, `DrawLine`, `DrawColoredPolygon`, `DrawPolyline`, `DrawArc` e `DrawRect` são reimplementadas com `new` para cair no lote. Texto e transformações esvaziam o lote antes de desenhar, para manter a ordem.
- O redesenho acontece **uma vez por quadro**, em `_Process`. A física fica limitada a 3 passos por quadro (`max_physics_steps_per_frame=3`).
- O MSAA 2D (2×) suaviza as bordas dos triângulos, que não têm o antialias do `DrawPolyline`.

## Consequências
- O mesmo teste de estresse caiu para **3,5 ms por quadro**: 89 chamadas de desenho e nenhuma coleta gen2.
- Arte nova deve usar o `Ink` ou os `Draw*` da própria classe. Uma chamada direta ao motor, como `base.Draw*` ou `RenderingServer`, dentro de `_Draw` precisa de `InkBatch.Flush()` antes.
- Tudo continua numa thread só. Se for preciso, a próxima melhoria é gerar a geometria de cada inimigo só quando a tinta ferve (10 vezes por segundo) e reaproveitá-la entre um passo e outro, e depois paralelizar essa geração.
- Rodar a checagem de estresse depois de mudanças de arte grandes, sem som:
  `--audio-driver Dummy res://scenes/tests/Stress.tscn`.
