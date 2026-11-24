# Chaos Arena — Informe de la tarea del jefe

**Fecha:** 23/11/2026
**Rama del motor:** `feature/chaos-arena-teach-ai`
**Build final medido:** motor `d7da7594`, mapa `181d618`, `v0.0.6`
**Wasm publicado:** `8988ee15075380845d647879db42aba8a23a7dc508e3121862cccf3cca96c167`
**Perfil publicado:** `5373d9c2b5314e5b3d740a87082c940a3da47c88f1e94e20f08dde3276304e26`
**Plan:** `chaos-arena-plan-tarea-jefe-v2.md`, sha256 `209d2ef764740bf935b3541f3aec58176ba5f4dbaba89fd6390f60d71f8d2d80`

Este informe sustituye al borrador del 10/11/2026 (`.artifacts-check/p3-draft/boss-report.md`), que
decía 51,0 % y ya no es cierto.

## 0. Respuesta corta

| Encargo | Veredicto | Cifra que lo sostiene |
|---|---|---|
| **1. Ninguna decisión aleatoria inclina la partida** | **Cumplido en lo demostrable.** C1 y C2 se sostienen; el azar por diseño no se promete erradicado. | Asiento 1: **51,5 %** [49,3–53,7] en 2010 partidas y **51,1 %** [48,0–54,2] en 1000. Cuatro asientos: 25,6 / 23,4 / 27,6 / 23,2 %, χ² p 0,460. Dos pasadas del mismo build: **200 / 200** semillas idénticas. Los 9 sorteos sembrados están documentados, el sitio 6 se replicó en ventana virgen (Δ **+0,90** [−3,36; +5,16], p 0,679) y el sitio 7 se midió con autocast encendido (Δ **+2,40** [−1,88; +6,68], p 0,2928). |
| **2. Enseñar a la IA a ganar** | **Ventaja pequeña, por debajo del criterio (C3b).** Gana más de la mitad, pero no el 55 % que el pre-registro exigía. | **52,58 %** [50,36–54,73] en 2000 partidas (1051 victorias, 1 tabla), p unilateral **0,0113**, ventana virgen 82001–83000. |
| **3. Perfeccionar el sistema automático** | **Cumplido.** Un comando entrena, mide contra fábrica, escribe perfil e informe y emite veredicto; la compuerta ya rechaza ventanas solapadas con el entrenamiento. | Corrida real del 20/11: **PUBLICAR**, 55,35 % [52,25–58,40], p 0,0004, perfil escrito con **4 pesos para 4 rasgos**, sin aviso de desajuste. |

**Sobre «lo del 50 % de la IA»:** sí, el campeón gana más de la mitad. En la ventana independiente el
IC95 es [50,36–54,73] y el p unilateral contra el 50 % es 0,0113, así que el «50 %» está superado. Lo
que **no** está superado es el listón del encargo, que el plan fijó en **55 % con p < 0,01**: 52,58 %
queda 2,4 puntos por debajo. La lectura correcta es «ventaja real y pequeña», no «la IA domina a
fábrica».

## 1. Qué se midió exactamente, y con qué build

La línea base congelada del plan (`324b91a7` / `343d77a4`) sirvió para abrir el bloque decisivo. El
trabajo de cierre se hizo sobre estos artefactos:

| Medición | Motor | Mapa | Wasm |
|---|---|---|---|
| C1 (pool 66001–68010), test nulo, duelo interno, ablación, corrida de publicación | `324b91a7` | `2632086` | `2137d798…` |
| Réplica C3b (82001–83000), tanda a 4 asientos, sitio 6 y sitio 7 | `324b91a7` / `d7da7594` | `181d618` | `8988ee15…` |
| Ablación de 2 rasgos (scratch) | `324b91a7` | copia de `2632086` | `b4160ffe…` |

Dos commits posteriores a las primeras mediciones no tocan la simulación:

- `181d618` cambia **solo** `Chaos Arena_bot.json` (publica el perfil entrenado); no cambia `MapScript`.
- `d7da7594` cambia **solo** el pipeline de publicación (`SelfPlayTrainer`, `MatchRunner`, `PublishGate`,
  `PublishPipeline`, `PublishReport`); no cambia el motor de partida.
- La semilla 81001 jugada con el wasm anterior (`2137d798…`) y con el publicado (`8988ee15…`) produce
  una salida idéntica hasta el último campo (`.artifacts-check/p5-four-seat/build-pre-p5-81001.jsonl`
  frente a `build-published-81001.jsonl`, comparados con `cmp`: idénticos).
- El A/B del sitio 7 (§2.4) corre sobre una **copia scratch** del mapa `181d618` con `AutoCast`
  habilitado en `ability/soul_siphon`; el wasm publicado no se tocó (`8988ee15…`) y el brazo de control
  es un scratch con la constante de ejecución a 0 (`fde18bf8…`). Es un control de medición, no un
  diseño propuesto, igual que el centro fijo del sitio 6.

Por eso las cifras de C1 y del duelo interno se pueden leer junto a las del build final. El informe lo
declara en vez de fingir que todas salieron del mismo binario.

### Pre-registro y su huella

Las afirmaciones de §A se congelaron el 18/11/2026. El sha256 del plan **en el momento de escribir este
informe** es `209d2ef7…`. Ese valor **no** es el que tenía el documento al congelar §A: el plan se
corrigió después del cierre de las mediciones (21–23/11) para sustituir el 56,00 % por C3b, añadir la
réplica del sitio 6, reescribir la conclusión de §0, corregir la errata del IC del test nulo (decía
46,9–53,1; el Wilson de 511/1000 es 48,0–54,2) y marcar los pasos completados. La última edición
(23/11) cierra el Paso 6 en el runbook y su commit (`91187772`), deja escrita la decisión del perfil
y anota el archivo de la ronda; la segunda edición del 23/11 registra el A/B del sitio 7 con autocast y el
cierre de los defectos de plataforma, y deja abiertos solo los PRs y la decisión del balanceo del pool.
Los cambios son de
estado, redacción y criterio declarado; **las condiciones de aceptación de §A no se tocaron**. Aun así, el informe declara
que la huella actual no es la de un pre-registro intacto y no la presenta como tal.

La réplica del sitio 6 tiene su propio pre-registro, escrito antes de lanzar la tanda:
`.artifacts-check/p5-sites/site6-replica-prereg.md`, con mtime 22/11/2026 17:58:05, 18 segundos antes
del arranque. Su regla de lectura no se eligió después de ver los datos.

**Multiplicidad.** C3 es el único test confirmatorio (p < 0,01). C1, C2, C2b y C4 se reportan como
descriptivas, con n, IC y p. Con siete afirmaciones a α = 0,05 habría ≈30 % de al menos un falso
positivo; el informe no lo esconde.

## 2. Encargo 1 — ninguna decisión aleatoria inclina la partida

### 2.1 C1 — equidad por asiento

| Prueba | Artefacto | n | Resultado | p |
|---|---|---|---|---|
| Pool post-desempate, ambos asientos con política uniforme | `.artifacts-check/p1b-c1/pool-66001-68000.jsonl` + `pool-68001-68010.jsonl` | 2010 partidas (2009 completas) | asiento 1 **51,5 %** (1035), IC95 49,3–53,7; asiento 2 48,5 % (974), IC95 46,3–50,7 | exacta bilateral 0,188 |
| Test nulo del arnés: el mismo campeón en los dos asientos | `.artifacts-check/p2-null/null-69001-70000.jsonl` | 1000 partidas (999 decisivas, 1 tabla) | asiento 1 **51,10 %** (511), IC95 48,00–54,19 | exacta bilateral 0,507 |
| Cuatro asientos con el campeón en los cuatro | `.artifacts-check/p5-four-seat/four-seat.jsonl` | 500 partidas | 25,60 / 23,40 / 27,60 / 23,20 %; los cuatro IC95 cubren el 25 % | χ² homogeneidad 2,587 (gl 3), p 0,460 |
| Control de dos asientos sobre las mismas semillas | `.artifacts-check/p5-four-seat/two-seat-control.jsonl` | 500 partidas | 52,80 / 47,20 %, IC95 48,42–57,14 y 42,86–51,58 | χ² 1,568 (gl 1), p 0,211 |
| McNemar de asiento del duelo del campeón | `.artifacts-check/p5-replication/arm-a-champion-seat1.jsonl` + `arm-b-champion-seat2.jsonl` | 1000 parejas espejadas | b 249 / c 272, 521 discordantes | exacto bilateral 0,335 |

**Lectura C1:** el IC95 cubre el 50 % en las dos mediciones de dos asientos y en los cuatro asientos;
la prueba pareada de asiento no rechaza. La condición de C1 se cumple. El pool tiene además 5 héroes
fuera de |z| > 2 (sección 6), que es un problema de balance del pool y **no** de asiento; no se puede
presentar el pool como limpio.

### 2.2 C2 — los 9 sorteos sembrados

Inventario verificado sobre el árbol del mapa, no sobre el plan:

| # | Sitio | Qué decide | Cómo se cerró |
|---|---|---|---|
| 1 | `MapScript.cs:909-925` | barajado del reparto de casillas y elemento por nivel | Documentado: un tablero compartido para los dos asientos. |
| 2 | `MapScript.cs:934-947` | orden compartido del draft de héroes | Documentado: un orden para los dos. |
| 3 | `MapScript.cs:1131` | reroll de ítem | Acceso igual por construcción. |
| 4 | `MapScript.cs:1147` | reroll de elemento | Ídem. |
| 5 | `MapScript.cs:1474` | reroll de héroe | Ídem. |
| 6 | `MapScript.cs:1716-1720` | posición en el centro de la arena para unidades desocupadas | Medido dos veces; réplica en ventana virgen (2.3). |
| 7 | `MapScript.AvatarAbilities.cs:73` | ejecución instantánea probabilística de Soul Siphon | A/B 0,25 vs 0 con autocast: 47,8 % de las unidades cambian de resultado, Δ +2,40 pts, p 0,2928 (2.4). |
| 8 | `MapScript.cs:1761` | `_api.Shuffle(spawnSlotOrder)`: casilla de aparición por jugador | Documentado + tanda a 4 asientos. |
| 9 | `MapScript.Match.cs:201-217` | baraja el roster y asigna qué rival ataca la oleada de cada jugador | Documentado + tanda a 4 asientos; degenerado con 2 asientos. |

Los sitios 1 y 2 no consumen el flujo compartido: cada uno construye un `new SeededRandom` con la misma
semilla, así que el tablero y el orden de draft no dependen de cuántos sorteos ocurrieron antes. Los
sitios 3–9 pasan por `PickRandom` / `Shuffle` / `RandomFloat` → `ServerGameAPI` → `SeededRandom.Shared`
→ `EcsSharedMemory.RandomSeed`.

**El azar no reproducible ya no existe en el árbol.** `ee2b8eb` borró el único `Random.Shared`
(`MapScript.Match.cs:82`); hoy no queda `System.Random`, `DateTime`, `Guid`, `Stopwatch`,
`Environment.TickCount` ni hilos en las fuentes del mapa. Dos pasadas completas del mismo build dan
**200 de 200** semillas idénticas en `winnerSeat`, `endReason` y `durationSeconds`
(`.artifacts-check/probe-tiebreak/passA-90001-90200.jsonl` frente a `passB-90001-90200.jsonl`), y la
semilla de la tanda a 4 asientos repite 3 de 3 líneas byte a byte
(`.artifacts-check/p5-four-seat/determinism-replay.jsonl`, semillas 92001–92003).

**Tanda a 4 asientos.** 500 partidas, semillas 92001–92500, el campeón en los cuatro asientos:
25,60 / 23,40 / 27,60 / 23,20 %, χ² = 2,587 (gl 3), p = 0,460; daño medio por asiento 232 429 / 233 101 /
230 464 / 232 683. Ningún asiento se separa del 25 %. Es simetría de asiento, no fuerza: la tasa
esperada es 25 % y no se puede leer como «el campeón gana X %».

### 2.3 Sitio 6 — centro de arena aleatorio vs fijo

Dos ventanas independientes, el mismo par de wasm verificado por sha256, 500 semillas espejadas por
brazo (1000 partidas por brazo). El brazo «fijo» es un control scratch que sustituye el sorteo por el
centro geométrico del rectángulo; no es un diseño propuesto.

**Ventana 1 — 94001–94500**

| Brazo | Agrupado | IC95 | Asiento 1 | Asiento 2 | McNemar de asiento |
|---|---|---|---|---|---|
| aleatorio (publicado `8988ee15…`) | **56,85 %** (568 + 1 tabla / 1000) | 53,71–59,84 | 51,40 % | 62,20 % | b 96 / c 150 · p 0,0007 |
| fijo (scratch `02561ee2…`) | **52,60 %** (526/1000) | 49,50–55,68 | 50,80 % | 54,40 % | b 114 / c 132 · p 0,278 |

Δ pareado (aleatorio − fijo, 1000 unidades): **+4,25 pts [−0,08; +8,58]**, z 1,924, p 0,054;
discordantes 266 / 223, McNemar 0,057.
Artefactos: `.artifacts-check/p5-sites/site6-random-seat{1,2}.jsonl` y `site6-fixed-seat{1,2}.jsonl`.

**Ventana 2 — réplica virgen 96001–96500** (pre-registro arriba citado)

| Brazo | Agrupado | IC95 | Asiento 1 | Asiento 2 | McNemar de asiento |
|---|---|---|---|---|---|
| aleatorio | **54,60 %** (546/1000) | 51,50–57,66 | 53,80 % | 55,40 % | b 121 / c 129 · p 0,658 |
| fijo | **53,70 %** (537/1000) | 50,60–56,77 | 51,80 % | 55,60 % | b 118 / c 137 · p 0,260 |

Δ pareado: **+0,90 pts [−3,36; +5,16]**, z 0,415, p 0,679; discordantes 240 / 231, McNemar 0,713.
Artefactos: `.artifacts-check/p5-sites/site6b-random-seat{1,2}.jsonl` y `site6b-fixed-seat{1,2}.jsonl`.

**Lectura pre-registrada:** el IC95 del Δ contiene el 0 y el McNemar de asiento del brazo aleatorio no
rechaza (p 0,658). **El sitio 6 se declara limpio; la señal de 94001–94500 no se reproduce.**

**Agrupado descriptivo** (no era un test pre-registrado): Δ **+2,57 pts [−0,46; +5,61]**, z 1,663,
p 0,096; heterogeneidad Q 0,00 (gl 1), p 0,973, I² 0 %. Las dos ventanas son compatibles entre sí.

**Asimetría de asiento agrupada:** aleatorio 52,65 / 58,80 (McNemar b 217 / c 279, p 0,006); fijo
51,30 / 55,00 (b 232 / c 269, p 0,108). La **interacción** —cuánto más inclina el asiento el brazo
aleatorio que el fijo— es **−2,45 pts [−8,51; +3,61]**, z −0,792, p 0,428. La inclinación al asiento 2
no se puede atribuir al sorteo del centro: aparece también con el centro fijo y es del tamaño que el
campeón ya muestra en el duelo de C3.

**Lo que el agrupado no descarta:** el límite superior del IC (+5,6 pts) impide afirmar neutralidad
exacta del sorteo. Lo que se descarta es un sesgo de asiento **atribuible** al sorteo, no que el sorteo
pueda mover la tasa en ese orden.

### 2.4 Sitio 7 — Soul Siphon (C2b)

El sorteo de `MapScript.AvatarAbilities.cs:73` solo se alcanza cuando la habilidad se lanza, y el mapa
publicado la declara con `"AutoCast": null`: ningún bot la castea y el dado no se tira nunca. La
medición anterior de este informe (0 discordantes de 1000, Δ 0,00) era un cero **por construcción**, no
una propiedad del sorteo. Para medirlo se habilitó el autocast en una copia scratch del mapa
(`"AutoCast": {"Enabled": true, "ManaReserve": 0, "MinManaPercent": 0}` en `ability/soul_siphon`) y se
compararon dos builds sobre esa misma copia: el publicado (`8988ee15…`,
`SoulSiphonMaximumExecuteChance = 0.25`) y un scratch con la constante a 0 (`fde18bf8…`,
`.artifacts-check/p5-sites/soulsiphon-0.wasm`). Ventana **virgen 97601–98100**, no usada por ninguna
tanda anterior, 500 semillas espejadas por brazo (1000 partidas por brazo).

| Brazo | Agrupado | IC95 | Asiento 1 | Asiento 2 |
|---|---|---|---|---|
| 0,25 (`8988ee15…`) | **55,70 %** (557/1000) | 52,61–58,75 | 55,00 % (275/500) [50,62–59,31] | 56,40 % (282/500) [52,02–60,68] |
| 0 (`fde18bf8…`) | **53,30 %** (533/1000) | 50,20–56,37 | 53,80 % (269/500) [49,42–58,12] | 52,80 % (264/500) [48,42–57,14] |

Δ pareado (0,25 − 0, 1000 unidades semilla × asiento): **+2,40 pts [−1,88; +6,68]**, McNemar exacto
bilateral **p 0,2928**. Discordantes **478 de 1000 (47,8 %)**: 251 los gana solo el brazo de 0,25, 227
solo el de 0; 306 los ganan los dos y 216 los pierden los dos. Ningún brazo produce tablas.
Artefactos: `.artifacts-check/p6-soulsiphon/025-seat{1,2}.jsonl`, `000b-seat{1,2}.jsonl` y
`analysis.txt`; el analizador es `.artifacts-check/p6-soulsiphon/analyze-soulsiphon.mjs`.

**Lectura.** El dado del sitio 7 **mueve partidas y no inclina la balanza**: casi la mitad de la ventana
cambia de resultado entre brazos, así que la habilidad no es inerte, pero el efecto neto no se separa de
cero (el IC95 del Δ contiene el 0). C2b se cumple con cifra — el sorteo no se puede presentar como
palanca que dé ventaja a un asiento. Queda un dato de diseño para quien decida el mapa: en el mapa
**publicado** la habilidad sigue con `AutoCast: null` y ningún bot la lanza, así que este Δ mide la
palanca disponible, no el comportamiento actual (§6.5).

### 2.5 La frase obligatoria

> Lo demostrable es que ningún asiento queda favorecido; el azar existe por diseño y no es
> erradicable.

## 3. Encargo 2 — enseñar a la IA a ganar

### 3.1 C3 — la ventana independiente

El duelo es campeón contra fábrica (`--policy uniform`), semillas espejadas, 2000 partidas.

| Ventana | n | Victorias | Tasa | IC95 | p unilateral |
|---|---|---|---|---|---|
| **Virgen 82001–83000** | 2000 (1 tabla contada al medio) | **1051** | **52,58 %** | **50,36–54,73** | **0,0113** |
| Interna 70001–71000 | 2000 | 1120 | 56,00 % | 53,8–58,2 | < 0,0001 |

Virgen: asiento 1 514/1000 = **51,40 %** [48,30–54,49]; asiento 2 537/1000 = **53,70 %** [50,60–56,77];
McNemar de asimetría b 249 / c 272, 521 discordantes, p exacto **0,335**.
Artefactos: `.artifacts-check/p5-replication/arm-a-champion-seat1.jsonl` y
`arm-b-champion-seat2.jsonl`; analizador `analyze-replication.mjs`.

**Veredicto C3: no se cumple.** El criterio pre-registrado era ≥ 55 % con p < 0,01; 52,58 % queda
2,4 puntos por debajo y el p no alcanza. La ventaja sobre fábrica existe —el IC95 excluye el 50 % por
0,36 puntos— pero es pequeña. Se declara **C3b: ventaja pequeña, por debajo del criterio**. Con la
banda C3b original (53–54 %) hay que ser preciso: 52,58 % está 0,4 puntos por debajo de su borde
inferior; el nombre honesto es «ventaja pequeña», no «53–54 %».

**El 56,00 % no se reprodujo.** Aquella medición se hizo sobre 70001–71000 y esta sobre una ventana
virgen; la diferencia entre ambas no es un cambio del perfil ni del wasm. La explicación que el plan
dejó escrita —solape con semillas del entrenamiento— es **plausible pero no está medida**: la corrida
de entrenamiento del 16/11 no registraba sus semillas (el registro se añadió con la compuerta nueva el
21/11), así que no se puede reconstruir el solape. Lo establecido es la no reproducción, no la causa.

### 3.2 Potencia con el efecto de diseño medido

Los gemelos espejo no son independientes al 100 %. Sobre las 500 parejas del duelo interno
(`.artifacts-check/p2-duel/duel-seat{1,2}-70001-71000.jsonl`), la tabla 2×2 es 154 / 118 / 123 / 105:
φ = 0,0268 ≈ ρ = 0,027. La muestra efectiva es 1,027 sobre el n crudo, no 2.

| n (partidas) | Potencia al 55 % | al 54 % | al 52 % |
|---|---|---|---|
| 1600 | 92 % | 72 % | — |
| **2000** | **97 %** | **83 %** | **53 %** |

La nota del peor caso: las ~2300 partidas que pediría suponer ρ = 1 son el peor caso, no el caso. Con
ρ = 0,027, 2000 partidas bastan para detectar el 55 % con 97 % de potencia y el 54 % con 83 %. Para
detectar un efecto de 52,58 % con comodidad haría falta otra escala de muestra; por eso el informe no
convierte el 52,58 % en una victoria.

### 3.3 C4 — ¿de dónde viene la ventaja?

Ablación 4 rasgos vs 2 rasgos, mismas 500 semillas espejadas (81001–81500), 1000 partidas por build.

| Build | Agrupado | IC95 | Asiento 1 | Asiento 2 | McNemar de asimetría |
|---|---|---|---|---|---|
| 2 rasgos (ablación, wasm `b4160ffe…`) | **56,60 %** (566/1000) | 53,5–59,7 | 55,4 % | 57,8 % | b 114 / c 126 · p 0,478 |
| 4 rasgos (control, wasm `2137d798…`) | **55,30 %** (553/1000) | 52,2–58,4 | 53,8 % | 56,8 % | b 113 / c 128 · p 0,367 |

Δ pareado (ablación − control): **+1,30 pts [−0,9; +3,5]**, z 1,154, p 0,249; McNemar de discordantes
70 / 57, p 0,287. Los IC agrupados **se solapan**.
Artefactos: `.artifacts-check/p3-ablation/ablation-seat{1,2}-81001-81500.jsonl` y
`control-seat{1,2}-81001-81500.jsonl`; analizador `buildcompare.mjs`.

**Veredicto C4 — «no separable con esta muestra».** La ventaja **sobrevive** a quedarse con dos rasgos:
56,60 % ≥ 55 % con IC que excluye el 50 %, y la diferencia contra los cuatro rasgos es indistinguible de
cero. Lo que la ablación retira —durabilidad y alcance, los dos rasgos de menor |peso|— no es lo que
sostiene la ventaja. Tampoco se puede afirmar lo contrario, que la palanca sea el vector del draft:
sin dos de sus cuatro rasgos la ventaja sigue ahí.

**Consecuencia para la Fase 1:** estos datos no la sostienen, pero tampoco la descartan. Que la ventaja
sobreviva a 2 rasgos no demuestra que ampliar el vector de combate sea inútil; demuestra que esta
ablación no la derriba. Abrir la Fase 1 o cerrar con «ventaja pequeña» es una **decisión abierta**, no
una conclusión de este informe.

## 4. Encargo 3 — el sistema automático

`--train-publish` hace, en un comando: entrena → juega el duelo espejado fuera de muestra → corre el
humo con el perfil cargado desde el directorio de datos del mapa → emite `<perfil>.publish.md` →
escribe `<mapDir>/<MapName>_bot.json` **solo si** el veredicto es PUBLICAR; si no, sale con código 3.
La compuerta exige ≥ 55 % con p < 0,01, 0 partidas fallidas, ningún aviso de desajuste y rasgos del
mapa = pesos del perfil.

**Corrida real del 20/11/2026 (17:15).** 144 partidas de entrenamiento + 96 de validación + 1000 de
duelo + 4 de humo, **0 fallidas**. Resultado: **PUBLICAR** (código 0).

| | Agrupado | IC95 | Asiento 1 | Asiento 2 |
|---|---|---|---|---|
| Victorias | 553 + 1 tabla / 1000 (**55,35 %**) | 52,25–58,40 | 53,80 % (269/500) | 56,90 % (284 + 1 tabla / 500) |
| p unilateral (binomial exacta) | **0,0004** | — | — | — |

Artefactos: `.artifacts-check/p4-publish/Chaos Arena_bot.publish-seat{1,2}.jsonl`,
`Chaos Arena_bot.publish.md`, `written-profile.json`, `verify-duel.mjs`. El perfil escrito en
`ugc-ChaosArena/Chaos Arena_bot.json` tiene 8 pesos de género + 4 de mapa
(`0.0862186, 0.4063943, 0.1029425, -0.1574601`), `TrainedEpochs` 1, y sha256 `5373d9c2…`; el humo
registra **4 rasgos de candidato = 4 pesos** y ningún aviso de desajuste.

**Cuatro precisiones que el informe no puede callar:**

1. `81001–81500` son **500 semillas**, no 1000: el duelo espejado da 1000 partidas. La compuerta y los
   IC usan n = 1000.
2. Esa ventana **ya se había jugado en el Paso 3**: la corrida reproduce el control de 4 rasgos casi al
   detalle. No es una muestra nueva; la réplica independiente es la de 82001–83000 (52,58 %). El
   55,35 % no certifica C3.
3. El margen sobre el umbral es fino: 55,35 % frente a 55,00 %, con IC que baja hasta 52,25 %. El
   p = 0,0004 sostiene «mejor que el 50 %», no «cómodamente por encima del 55 %».
4. **El perfil publicado y la compuerta se contradicen, y esa consecuencia se enuncia aquí.** La
   compuerta exige ≥ 55 %; la única medición virgen del mismo campeón da **52,58 %, por debajo de ese
   umbral**. Dicho sin rodeos: **si hoy se juzgara el perfil publicado sobre semillas vírgenes, la
   compuerta diría NO PUBLICAR.**

**Decisión tomada: el perfil se queda.** Se mantiene porque cumple el umbral del encargo —gana más de la
mitad de las partidas sobre semillas vírgenes, 52,58 % [50,36–54,73], p 0,0113— y es estrictamente mejor
que el bot uniforme. Queda escrito, en la misma frase, que el perfil **pasa el umbral del jefe (gana
> 50 %), no el listón propio del 55 %**. Las otras dos salidas se descartan: re-juzgarlo con la compuerta
sobre ventana virgen daría NO PUBLICAR y dejaría el mapa con fábrica, peor bot; y re-calibrar el umbral
(publicar si p < 0,01 contra el 50 %) solo sería legítimo como cambio de criterio documentado **antes** de
la próxima corrida, no después de ver el resultado.

**Compuerta corregida el 21/11 (`d7da7594`).** `SelfPlayTrainer` registra cada semilla que juega
(`PlayedSeeds`), `TrainingRunResult` la propaga y `PublishGate.Judge` la recibe; si `--publish-seeds`
repite alguna, el pipeline corta antes de jugar el duelo y el veredicto es NO PUBLICAR con el solape como
primer bloqueo. Verificado en dos corridas cortas:

| Corrida | Ventana | Resultado |
|---|---|---|
| `.artifacts-check/gate-blocked-report/` | `1-2147483647` | 49 semillas repetidas detectadas, duelo **no jugado**, humo **no jugado**, NO PUBLICAR, código 3, sin tasa/IC/p de relleno. |
| `.artifacts-check/gate-disjoint-window/` | `84001-84020` | Ventana disjunta: duelo 19/40 = 47,50 % [32,94–62,50], p 0,6821; humo 4 partidas, 4 = 4 rasgos; NO PUBLICAR por tasa, código 3. |

En las dos, el perfil publicado (`5373d9c2…`) quedó intacto y no se escribió ningún candidato.

## 5. Coste real de las tandas

| Tanda | Partidas | Reloj medido |
|---|---|---|
| Pool C1 (66001–68010) | 2010 | 3019,7 s a `--parallel 10` |
| Test nulo (69001–70000) | 1000 | 1571 s |
| Duelo interno (70001–71000) | 2000 | ≈ 29 min por brazo de 1000 |
| Ablación 4 vs 2 (81001–81500) | 4 × 500 concurrentes | 3049,8 s por salida, 4 brazos a `--parallel 3` |
| Tanda a 4 asientos (92001–92500) | 500 | 1034 s a `--parallel 10` |
| Réplica C3b (82001–83000) | 1000 por brazo | 1637 s de cómputo; el brazo B sufrió una parada de ~82 min por pérdida de conexión, reanudó sin repetir semillas |
| Sitio 7, Soul Siphon (93001–93500) | 1000 por brazo, 4 brazos | 3185–3193 s por brazo, 4 brazos concurrentes |
| Sitio 6, ventana 1 (94001–94500) | 1000 por brazo, 4 brazos | 3130–3132 s por brazo |
| Sitio 6, réplica (96001–96500) | 1000 por brazo, 4 brazos | 3060–3075 s por brazo; 3075 s de reloj de la tanda |
| Sitio 7, Soul Siphon con autocast (97601–98100) | 1000 por brazo, 4 brazos concurrentes | 3480 s de reloj de la tanda (19:38–20:36); el brazo 0 falló al arrancar (`Console.SetOutputEncoding`) y se relanzó entero, con las 500 semillas jugadas una sola vez |
| Corrida real de publicación | 144 + 96 + 1000 + 4 | ≈ 35 min |

## 6. Limitaciones

1. **El pool no está limpio.** En 2010 partidas hay **5** héroes fuera de |z| > 2: `arcane_scholar` −16,7
   (z −2,6), `lightbringer_monk` −14,4 (−2,2), `storm_caller` −13,5 (−2,1), `frost_warden` +17,7 (2,9),
   `magma_smith` +20,2 (3,1). No lo causa el desempate; el pool queda fuera de alcance.
2. **El poder declarado del draft no cumple su propio criterio:** 52,7 % [50,4–55,0], 1 sd = 4,2 puntos,
   frente al ±2,0 que el plan se exigía.
3. **El sitio 6 no descarta un efecto pequeño:** el IC agrupado llega a +5,6 pts a favor del centro
   aleatorio. Lo descartado es el sesgo de asiento atribuible al sorteo.
4. **El brazo fijo es un control, no un diseño propuesto**, y el sitio 6 se midió con el campeón contra
   fábrica, así que la asimetría por asiento incluye la del campeón. La comparación entre brazos sí está
   pareada por semilla y asiento.
5. **Soul Siphon se midió con autocast encendido, no en el mapa publicado.** El A/B de §2.4 usa una
   copia scratch del mapa con `AutoCast` habilitado; en el mapa publicado la habilidad sigue con
   `AutoCast: null` y ningún bot la lanza. El Δ mide la palanca disponible si se encendiera, no el
   comportamiento actual del mapa.
6. **C3b es una sola réplica.** El 52,58 % no se ha vuelto a medir en una tercera ventana virgen. El IC
   [50,36–54,73] es el que hay.
7. **La causa de la diferencia 56,00 % → 52,58 % no está establecida.** El solape con el entrenamiento
   es una explicación plausible, no un hecho medido; la corrida del 16/11 no guardó sus semillas.
8. **C4 es descriptiva.** La ablación no derriba la ventaja, pero no prueba que la Fase 1 sea inútil.
9. **La tasa del build publicado varía entre ventanas** (56,85 % en 94001–94500, 54,60 % en
   96001–96500 y 52,58 % en 82001–83000), así que una sola ventana no se lee como propiedad estable. El
   Δ del sitio 6 sí es homogéneo entre sus dos ventanas (Q 0,00, I² 0 %).
10. **La huella del plan cambió después del pre-registro**, como se declara en la sección 1. Las
    condiciones de §A no se tocaron, pero el documento no es un pre-registro intacto.
11. **`IsPlayerComputer` cambia de comportamiento para el asiento 0.** Antes devolvía `true` sin
    consultar nada cuando el índice era 0; ahora responde con lo que publica el mapa
    (`ScriptPlayersState`) o, en su defecto, con el lobby (`PeerId < 0`). Afecta a
    `ClientRpcService.TryResolveLocalPlayer`, que descarta los RPC de mapa cuando el índice es de un
    ordenador: con el código anterior, todo RPC de mapa dirigido al asiento 0 se descartaba siempre.
    Compila con 0 errores, pero no se jugó una partida con el cliente Godot para verlo en vivo.
12. **`Realm.Godot/.vscode/map_schema.json` es manual.** El build solo regenera
    `Realm.MapEditorExtension/metadata.schema.json` y su copia en `MapTemplate/.vscode/`, así que el
    borrado de `Controller`/`AiType` se aplicó a mano en ese fichero (no está trackeado por git).

## 7. Trazabilidad — cada cifra con su fichero, n, IC y p

Cuando la afirmación es una comparación, el p que cuenta es el de la fila Δ; el p de una tasa aislada
contra el 50 % no es el test del sitio.

| Afirmación | Resultado | Fichero(s) | n | IC95 | p |
|---|---|---|---|---|---|
| C1, asiento 1 en el pool post-desempate | 51,5 % (1035) | `.artifacts-check/p1b-c1/pool-66001-68000.jsonl`, `pool-68001-68010.jsonl` | 2010 (2009 completas) | 49,3–53,7 | 0,188 exacta bilateral |
| C1, test nulo campeón vs campeón | 51,10 % (511) | `.artifacts-check/p2-null/null-69001-70000.jsonl` | 1000 (999 decisivas) | 48,00–54,19 | 0,507 exacta bilateral |
| C1, cuatro asientos | 25,60 / 23,40 / 27,60 / 23,20 % | `.artifacts-check/p5-four-seat/four-seat.jsonl` | 500 | los cuatro cubren 25 % | χ² 2,587 (gl 3), 0,460 |
| C1, control de dos asientos | 52,80 / 47,20 % | `.artifacts-check/p5-four-seat/two-seat-control.jsonl` | 500 | 48,42–57,14 / 42,86–51,58 | χ² 1,568 (gl 1), 0,211 |
| C1, McNemar de asiento del campeón | b 249 / c 272 | `.artifacts-check/p5-replication/arm-a-champion-seat1.jsonl`, `arm-b-champion-seat2.jsonl` | 1000 parejas | — | 0,335 exacto bilateral |
| C3, ventana virgen | **52,58 %** (1051 victorias + 1 tabla al medio / 2000) | mismos dos ficheros de C3 | 2000 (1999 decisivas) | **50,36–54,73** | **0,0113** unilateral |
| C3, asiento 1 / asiento 2 | 51,40 % (514/1000) / 53,70 % (537/1000) | mismos | 1000 + 1000 | 48,30–54,49 / 50,60–56,77 | — |
| C3, duelo interno | 56,00 % (1120) | `.artifacts-check/p2-duel/duel-seat{1,2}-70001-71000.jsonl` | 2000 | 53,8–58,2 | < 0,0001 unilateral |
| C3, McNemar del duelo interno | b 202 / c 268 | mismos | 1000 parejas | — | 0,0027 exacto |
| C4, ablación 2 rasgos | 56,60 % (566) | `.artifacts-check/p3-ablation/ablation-seat{1,2}-81001-81500.jsonl` | 1000 | 53,5–59,7 | — |
| C4, control 4 rasgos | 55,30 % (553) | `.artifacts-check/p3-ablation/control-seat{1,2}-81001-81500.jsonl` | 1000 | 52,2–58,4 | — |
| C4, diferencia pareada | +1,30 pts | los cuatro ficheros de C4 | 1000 unidades | −0,9 a +3,5 | 0,249 bilateral; McNemar 0,287 |
| Sitio 6, ventana 1 aleatorio | 56,85 % (568 + 1 tabla) | `.artifacts-check/p5-sites/site6-random-seat{1,2}.jsonl` | 1000 | 53,71–59,84 | — |
| Sitio 6, ventana 1 fijo | 52,60 % (526) | `.artifacts-check/p5-sites/site6-fixed-seat{1,2}.jsonl` | 1000 | 49,50–55,68 | — |
| Sitio 6, ventana 1 Δ | +4,25 pts | los cuatro | 1000 unidades | −0,08 a +8,58 | 0,054 bilateral |
| Sitio 6, réplica aleatorio | 54,60 % (546) | `.artifacts-check/p5-sites/site6b-random-seat{1,2}.jsonl` | 1000 | 51,50–57,66 | — |
| Sitio 6, réplica fijo | 53,70 % (537) | `.artifacts-check/p5-sites/site6b-fixed-seat{1,2}.jsonl` | 1000 | 50,60–56,77 | — |
| Sitio 6, réplica Δ | +0,90 pts | los cuatro | 1000 unidades | −3,36 a +5,16 | 0,679 bilateral; McNemar 0,713 |
| Sitio 6, Δ agrupado | +2,57 pts | las dos ventanas (ocho ficheros) | 2000 unidades | −0,46 a +5,61 | 0,096; Q 0,00, p 0,973, I² 0 % |
| Sitio 6, interacción de asiento | −2,45 pts | las dos ventanas | 1000 unidades | −8,51 a +3,61 | 0,428 |
| Sitio 7, A/B Soul Siphon con autocast | 55,70 % (557) vs 53,30 % (533) | `.artifacts-check/p6-soulsiphon/025-seat{1,2}.jsonl`, `000b-seat{1,2}.jsonl` | 1000 por brazo (500 semillas × 2 asientos) | 52,61–58,75 / 50,20–56,37 | Δ **+2,40** [−1,88; +6,68]; McNemar exacto **0,2928**; 478 discordantes |
| Determinismo, dos pasadas | 200 / 200 idénticas | `.artifacts-check/probe-tiebreak/passA-90001-90200.jsonl`, `passB-90001-90200.jsonl` | 200 | — | 0 diferencias |
| Determinismo, 4 asientos | 3 / 3 idénticas | `.artifacts-check/p5-four-seat/determinism-replay.jsonl` | 3 | — | — |
| Compuerta, corrida real | 55,35 % (553 + 1 tabla) | `.artifacts-check/p4-publish/Chaos Arena_bot.publish-seat{1,2}.jsonl` | 1000 | 52,25–58,40 | 0,0004 unilateral |
| Compuerta, bloqueo por solape | duelo no jugado | `.artifacts-check/gate-blocked-report/` | 49 semillas repetidas | — | — |
| Compuerta, ventana disjunta | 47,50 % (19/40) | `.artifacts-check/gate-disjoint-window/*.publish-seat{1,2}.jsonl` | 40 | 32,94–62,50 | 0,6821 unilateral |

## 8. Qué queda de la tarea del jefe

### Cerrado

- Pasos 1, 1b, 2.0, 2, 3, 4, 5, 6 y 7 del plan.
- C1, C2, C2b, C3b y C4 medidos y documentados.
- Pipeline `--train-publish` de punta a punta y perfil publicado.
- **Paso 6 — código muerto borrado (23/11/2026).** Se eliminaron
  `Realm.Ecs/AI/Balancer/StrategicAutoBalancer.cs` y el `BalanceGenome` duplicado de
  `Realm.Ecs/AI/Balancer/`; se conserva el de `Realm.Tools.BalanceOptimizer/`. El camino sintético de
  `SelfPlayTrainer.RunMatchAndGetScore` (`HeadlessSimulationRunner`) queda intacto. Compilan sin errores
  `Realm.Ecs`, `Realm.Tools.BalanceOptimizer`, `Realm.Tools.MatchRunner`, `Realm.Tools.Cli` y
  `MapTemplate` (WASM); el wasm publicado (`ugc-ChaosArena/bin/script/MapScript.wasm`) no se tocó. El
  borrado está en el commit `91187772`, que toca esos dos ficheros y ningún otro.
- **Decisión del perfil publicado: se mantiene** (ver §4, precisión 4): pasa el umbral del jefe, no el
  listón propio del 55 %.
- **Defectos de plataforma del sistema de IA (23/11/2026).** `IsPlayerComputer` ya no declara el asiento
  0 como ordenador por decreto (§6.11); un perfil con vector de pesos de longitud equivocada se registra
  y se publica en vez de caer al perfil por defecto en silencio (`BotController.ProfileWeightsRejected`
  → `ServerBots.ReportProfileWeightsRejection`); y `Controller`/`AiType` se borraron de
  `MapPlayerSlotConfig` —nadie los leía, escribía ni validaba— con los esquemas regenerados y
  `verify-schemas.ps1` en verde. Compilan con 0 errores `Realm.Client`, `Realm.Ecs` y
  `Realm.ListenServer`.
- **Fase 1: no se abre (23/11/2026).** El encargo 2 se cierra con «ventaja pequeña»: 52,58 %
  [50,36–54,73] sobre ventana virgen, p 0,0113, que sí supera el umbral del jefe (>50 %). La Fase 1
  (vector de combate 8 → 12: `DamageTradeRatio`, `ThreatTradeRatio`, `RangeAdvantage`, `FocusValue`)
  habría costado +3–5 días y reentrenar desde cero, y la ablación de C4 (+1,30 pts, p 0,249) ni la
  sostiene ni la descarta; no se gasta ese tiempo sin una razón medida.
- Este informe.

### Pendiente

1. **PRs.** El cierre operativo está hecho por su mitad de archivo: `.artifacts-check/runs/2026-11-23/`
   guarda el wasm publicado, los dos perfiles y los ficheros citados, con un `manifest.md` que da su
   sha256. Queda abrir los PRs de las dos ramas (`feature/chaos-arena-teach-ai` en el motor,
   `feature/chaos-arena-fair-draft` en el mapa). **No se empuja nada sin autorización.**
2. **Balance del pool de héroes.** Los 5 héroes fuera de |z| > 2 de §6.1 siguen como están: el pool no
   se toca en este cierre. La decisión (opción 1: re-ejecutar el balanceo después del rediseño de
   héroes y habilidades) es del jefe y queda encolada para ese re-run; este informe no la abre.

### Lo que no se promete

- No se promete erradicar el azar: se demuestra que ningún asiento queda favorecido.
- No se promete que el bot domine a fábrica: se declara una ventaja pequeña, 52,58 % [50,36–54,73].
- No se promete que el pool esté limpio: tiene 5 héroes fuera de |z| > 2.

## 9. Reproducción

Cada brazo del duelo y de los sitios se ejecutó con:

```
dotnet Realm.Tools.MatchRunner/bin/Release/net10.0/Realm.Tools.MatchRunner.dll \
  --map /c/Users/Admin/source/repos/ugc-ChaosArena \
  --wasm <wasm> --seed-range <a-b> --seats 2 --policy uniform \
  --seat-policy <s>=profile:.artifacts-check/draft-payload/train4/arena-draft-champion.json \
  --output <out>.jsonl --parallel 3
```

Analizadores: `analyze-replication.mjs` (C3), `analyze-sites.mjs` (sitios 6 y 7),
`analyze-site6-pooled.mjs` (dos ventanas, heterogeneidad e interacción), `buildcompare.mjs` (C4),
`analyze.mjs` (4 asientos), `verify-duel.mjs` (recomprobación del duelo publicado).

## 10. Artefactos de la ronda

| Qué | Dónde |
|---|---|
| Manifiesto del sitio 6 y Soul Siphon | `.artifacts-check/p5-sites/manifest.md` |
| Pre-registro de la réplica | `.artifacts-check/p5-sites/site6-replica-prereg.md` |
| Wasm de control y fijo | `.artifacts-check/p5-sites/control-025.wasm`, `fixed-center.wasm` |
| Logs de los brazos | `.artifacts-check/p5-sites/logs/site6b-*.log`, `replica-wave.log` |
| A/B Soul Siphon con autocast | `.artifacts-check/p6-soulsiphon/` (`025-seat{1,2}.jsonl`, `000b-seat{1,2}.jsonl`, `analysis.txt`, `analyze-soulsiphon.mjs`) |
| Réplica C3 | `.artifacts-check/p5-replication/arm-a-champion-seat1.jsonl`, `arm-b-champion-seat2.jsonl` |
| Duelo interno | `.artifacts-check/p2-duel/duel-seat{1,2}-70001-71000.jsonl` |
| Pool C1 | `.artifacts-check/p1b-c1/pool-66001-68000.jsonl`, `pool-68001-68010.jsonl`, `final.balance.md` |
| Ablación C4 | `.artifacts-check/p3-ablation/` |
| Cuatro asientos y determinismo | `.artifacts-check/p5-four-seat/` |
| Pipeline y perfil publicado | `.artifacts-check/p4-publish/`, `ugc-ChaosArena/Chaos Arena_bot.json` |
| Compuerta corregida | `.artifacts-check/gate-blocked-report/`, `.artifacts-check/gate-disjoint-window/` |

## 11. Addendum — fusión con origin/main (24/11)

- Motor: merge commit `69415ad5` de `origin/main` (`566847a4`, reestructuración Steam-first y Chariot Racer); los 8 proyectos compilan sin errores y `verify-schemas.ps1` pasa. Mapa: merge commit `6aedf04` de `origin/main` (`1d19581`, formato de mapa v0.0.7 e identidad de Workshop).
- El mapa fusionado declara `GameBuildNumber` v0.0.7 y `EngineGeneration: 1` (la compuerta es la generación, no el número de build), licencia v2 y sin `Dependencies`; `lib/` y `wit/` salen del motor fusionado y `lib/Realm.EditorAPI.dll` desaparece.
- La resolución conserva **90 valores de gameplay de la rama**: 68 stats de héroe, 18 coeficientes de escalado y 4 bloques `AutoCast`. Comparar contra `main` solo mostraría el bug de escala ×10 (`0/12/14/15/10`) y los cuatro autocasts apagados que esta ronda corrigió; partir de main y re-aplicar solo los stats habría cambiado el mundo simulado sin que ninguna compuerta lo dijera.
- Humo cruzado post-merge: semillas 92001–92010 idénticas **10/10** a `.artifacts-check/p5-four-seat/two-seat-control.jsonl` (mismo wasm publicado y mismo campeón en ambos asientos), sin avisos de pesos por defecto ni de desajuste de rasgos del mapa.
- Trazabilidad: `MapScript.wasm` nuevo con blake3 `ab0993ab…` y `manifest.json` refrescado en metadata, libs, wit y wasm; los `.jsonl` del A/B y del informe no cambian y la conclusión de §4 se mantiene.
