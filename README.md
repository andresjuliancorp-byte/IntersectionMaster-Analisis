# Intersection Master (Unity 6, Android)

Juego de control de tráfico. Todo se genera por código (sin escenas ni prefabs): `GameBootstrap` arranca solo.

## Versión 3: mapas por nodos
- Editor de **nodos y segmentos libres**: calles rectas o curvas (arrastrar el punto medio), 1–3 carriles por sentido, un sentido, ajuste a rejilla de 0,5.
- **Cruces automáticos**: al cruzar o tocar calles se crea el nodo; los bordes del mapa son entradas/salidas.
- Carriles con **flechas y máscaras de giro** (izquierda/recto/derecha), líneas de parada, cebras, rotondas con radio ajustable.
- Señales (stop, ceda, prioridad, prohibido girar, límite de velocidad), peatones, decoración, copiar/pegar zona, deshacer/rehacer.
- Panel contextual al seleccionar una calle o un nodo.
- 9 mapas de ejemplo y 10 niveles rehechos en el formato nuevo.

## Versión 3.1: interfaz y aspecto como la app de referencia
- **Vertical** (720×1280 de referencia, orientación fija en Portrait).
- **Juego:** velocidades (pausa, ×1, ×2, ×4), vehículos y peatones por minuto, barra derecha con Tráfico / Semáforo / Menú / Construir.
- **Editor:** barra izquierda (Menú, Jugar, Centrar, Guardar, Validar, Ajustes, Deshacer, Rehacer), barra derecha (Carril, Lógica, Decoración, Zona), panel naranja «Herramienta» arriba y panel contextual «Segmento» / «Nodo» abajo.
- **Aspecto:** aceras más anchas, tejados y árboles con sombra, coches con techo y sombra, franja luminosa de semáforo en la línea de parada, lluvia o nieve ligera.
- **Niveles:** al elegir uno salen «Alcanzar objetivo», «Sin fin» y «Puntuación más alta», con ranking local (10 mejores por nivel).

## Versión 3.2
- **Vertical u horizontal:** rotación automática; los cuadros de diálogo se acotan a la pantalla.
- **Semáforos individuales:** cada acceso tiene el suyo (herramienta Semáforo en Lógica, o «Semáforos» en el panel del nodo). En juego se toca cada semáforo (la franja de la línea de parada). Con «seguridad» activada, al poner uno en verde los que se cruzan pasan antes a amarillo y rojo y se espera a que el cuadro se despeje. «Auto» rota grupos de semáforos. Los mapas de ejemplo ya traen semáforos; los mapas propios sin semáforos regulan por prioridad.
- **Área visible (línea roja):** herramienta «Área visible» en Zona; arrastra bordes y esquinas. Al jugar, la cámara se limita a esa zona y lo de fuera queda tapado: los coches aparecen en entradas fuera del área y los peatones llegan andando por la acera desde fuera. El validador avisa de entradas o salidas dentro del área.
- **Calle pendiente:** al dibujar una calle se muestra una vista previa con su longitud en metros (1 unidad = 9 m), un punto central para curvarla y botones Confirmar / Cancelar.
- **Segmento:** Visible/Oculta, Marcas, Flechas (abre la herramienta de giros por carril).

## Versión 3.3
- **Propiedades de calle** (panel «Segmento»): color del asfalto, cables con postes, barandillado, tráfico permitido (todos, sin pesados, ligeros, solo emergencias, bus y taxi; los vehículos buscan ruta solo por calles que les admiten) y los ajustes anteriores.
- **Decoración nueva:** farola, coche aparcado, valla y seto (se orientan según la calle más cercana). Los mapas de ejemplo traen farolas.
- **Mapas de ejemplo:** si la pantalla es vertical se giran 90° al cargarse.

## v3.4
- **Cambio de carril:** un vehículo bloqueado >1,5 s por un líder lento adelanta por el carril contiguo si hay hueco y la ruta a su salida no empeora. Por calle: "Cambio carril / Sin cambio carril".
- **Humo:** Decoración > Humo (columna animada).
- **Capa (0-2):** puentes. Las calles de capas distintas no se cruzan, no chocan y se dibujan encima con sombra. Botón "Capa" en la barra izquierda (capa de las calles nuevas) y en el panel de la calle seleccionada.
- **Tabla de tráfico:** botón "Tabla": peso de cada entrada (dónde aparecen vehículos) y salida (destinos). Peso 0 = ninguno. Es una interpretación propia (no se vio abierta en los vídeos).
- Todo sin compilar ni probar en Unity.

## Importante
- Los mapas de ejemplo siguen siendo apaisados; en vertical se ve una parte y se mueve con el dedo. Falta rehacerlos en formato vertical.
- Los mapas guardados de la versión de cuadrícula **no se abren** (formato incompatible).
- El código nuevo está escrito a mano y **no se ha compilado ni probado en Unity**. Solo se verificó con comprobaciones de nombres/llaves y un port en Python de la generación de carriles, conectores y rutas sobre los 9 mapas (todas las entradas alcanzan alguna salida). Los errores de compilación aparecerán en los logs de Unity Cloud Build.
- Cloud Build: Bundle ID sin palabras reservadas de Java (p. ej. `com.int.*` falla).

## v3.5 – Editor con la distribución del editor de referencia
Basado en 8 capturas del editor original (vistas, no inventadas). Reproducido:
- Barra izquierda en dos bloques oscuros: Menú / Jugar / Centrar / Ajustes de mapa, y debajo Capa (el número dentro del círculo) y Tabla.
- Arriba a la derecha: cabecera naranja con el nombre de la categoría ("Herramienta", "Lógica", "Decoración") y pestañas redondas Acción / Tipo (la pestaña activa se pone naranja y muestra el icono de la herramienta elegida).
- Abajo a la derecha: categorías Carril / Lógica / Decoración con icono redondo; la activa en naranja.
- Rejilla de herramientas (3 o 4 columnas, icono redondo + rótulo) que se abre a la izquierda de la cabecera; "Propiedades" desplegable bajo las pestañas.
- Carril > Acción: Seleccionar segmento, Seleccionar nodo, Ver restricciones, Recta, Curva. Lógica > Tipo: Semáforo. Decoración > Tipo: Árbol, Edificio, Coche aparcado, Farola, Humo, Valla.
- Paleta del editor: fondo verde claro, calzada gris, aceras claras, sin cuadrícula, nodos y línea roja de área visible.
- Iconos dibujados por código (IconKit.cs), sin imágenes del juego original.

No verificable con las capturas (decisiones propias, marcadas como tales):
- Contenido de Menú (aquí: Guardar, Validar, Deshacer, Rehacer, volver a la lista de mapas), de Tabla (peso de entradas/salidas), qué hace exactamente Capa, y las pestañas Acción de Lógica/Decoración (aquí: Borrar, Mover mapa).
- "Ver restricciones" se enlaza con mis giros por carril; en el original parece mostrar tipos de vehículo.
- Pestaña Tipo de Carril (en el original aparece atenuada) y categoría Zona: añadidos propios para no perder funciones.
- Faltan de la referencia: Remolque, Luz de inundación, Luz, Señal de ferrocarril, Poste de teléfono, Señal de tráfico, Estructura redonda, Parada de autobús, Contenedores, Tendido eléctrico, Grúa, Pegatinas, Texto, Líneas, Misceláneo, Barco, Material, Aparcamiento, Controlador de semáforos, Parada, Peaje, Advertencia, Botón múltiple. No se muestran botones que no funcionan.
- Sin compilar ni probar en Unity.

## v3.6 — Herramientas completas del editor (SIN COMPILAR NI PROBAR)

**Estado:** no hay compilador de C#/Unity en el entorno. Solo se hizo verificación estática (balance de llaves, referencias de enums/métodos/iconos, casos duplicados). Pueden existir errores de compilación y de comportamiento.

### Herramientas de las capturas originales (con función real y datos guardados en el mapa)
Remolque, Luz de inundación, Luz, Señal de ferrocarril, Poste de teléfono, Señal de tráfico, Estructura redonda, Parada de autobús, Contenedores, Tendido eléctrico, Grúa, Pegatinas, Texto, Líneas, Misceláneo, Barco, Material, Aparcamiento, Controlador de semáforos, Parada, Peaje, Advertencia, Botón para múltiples semáforos.
Las decoraciones usan EditorTool.Place + DecorType; son puramente visuales y no afectan a la circulación. Parada/Peaje/Detector son puntos sobre la calle (PointItem) que afectan a la simulación.

### Decisiones provisionales (NO verificadas con el original)
Tabla, Capa, Ver restricciones (superposición + ciclo de presets), pestañas Lógica/Decoración, Tipo de Carril ("Giros por carril"), categoría Zona, y la semántica exacta de Parada, Peaje, Advertencia, Botón múltiple y Controlador.

### Edición
Propiedades reales por segmento/nodo/objeto/punto/botón con Confirmar/Cancelar (sesión por instantánea), deshacer/rehacer por JSON.

### Sin probar
Todo. Especialmente: orientación del sprite Triangle, ramas nuevas del panel de selección, integración de paradas/peaje/detector en VehicleController, round-trip de guardado.

## v3.7 — Tráfico por carril, modos realista/caótico, bloqueo, validación (SIN COMPILAR NI PROBAR)

Corregido el único error del build anterior (`menuRoot` duplicado → `edMenuRoot`).

**Nuevo**
- Tráfico permitido por carril: `RoadData.allowAB/allowBA` (0 = hereda de la calle). Panel de calle: elegir carril A→B / B→A, activar/desactivar cada tipo (8), "Permitir todos", "Solo buses", copiar a todos los carriles / al vecino. Afecta a rutas (Pathfinding), generación (si el carril de entrada no admite el tipo se elige uno admitido) y cambio de carril. Se guarda en el mapa; los mapas antiguos cargan igual.
- Modos de simulación: al pulsar Jugar (editor o lista de mapas) aparece la selección Realista / Caótico + intensidad (bajo, medio, alto, extremo) y opción de guardarla en el mapa. Caótico: un % de conductores (10/25/50/80 %) ignora semáforos, stops, cesiones y restricciones de carril, va más rápido (×1.1–1.6), reduce el hueco al líder y cambia de carril con riesgo. Los choques usan el sistema existente (vehículos detenidos que obstruyen).
- Decoración vial manual: Línea de detención, Acera, Mediana/separador (se dibujan arrastrando, se editan como cualquier objeto; no afectan al tráfico).
- Bloquear/Desbloquear calles y objetos decorativos (no se mueven, editan ni borran; nodos de calles bloqueadas no se mueven).
- Validador: tipos de vehículo sin ruta, carriles exclusivos que ningún vehículo generado puede usar, curvas cerradas para bus/camión, semáforos sin modo seguro, aviso de modo caótico. Los avisos con posición se tocan para centrar la cámara.

**Aún NO implementado (honesto)**
Flechas funcionales colocadas a mano con posición %; aceras/medianas como entidades ligadas a la calle (ahora son decoración dibujada); modelos de semáforo variados; fases de semáforo editables; perfiles de vehículo editables (radio de giro, etc.); agrupar objetos; modelos de vehículo más detallados; semáforos peatonales ligados a movimientos.

**Sin probar:** todo (no hay Unity en el entorno). Pruebas A–I del documento de requisitos: pendientes.

## v3.8 — Cuadrícula, flechas funcionales, semáforos, perfiles de vehículo (SIN COMPILAR NI PROBAR)

**Nuevo**
- Cuadrícula: botón "Rejilla" (panel izquierdo) con mostrar sí/no, ajuste sí/no y tamaño de celda (1,1 – 18 m). Se guarda en el dispositivo (PlayerPrefs). No mueve lo ya colocado. Escala única: 1 unidad = 9 m, carril = 3,2 m. El panel de calle muestra largo y ancho en metros; botón Centrar en calle, nodo, objeto, flecha y semáforo.
- Flechas funcionales (`MapData.arrows`): herramienta "Flecha funcional" (Carril → Tipo). Tocar un carril crea una flecha (o selecciona la cercana). Panel: izquierda/recto/derecha, posición ±5 % (5–95 %), tamaño, duplicar, copiar a los carriles del mismo sentido, borrar. Los giros de las flechas sustituyen a los automáticos al construir los conectores (afectan a rutas y maniobras). Las máscaras antiguas se convierten en flechas al cargar.
- Flechas automáticas: desactivadas por defecto (Ajustes de mapa → "Flechas automáticas"). Las pegatinas siguen siendo flechas decorativas sin efecto.
- Semáforos: herramienta Semáforo → tocar uno existente lo selecciona. Modelo (vertical, horizontal en báculo, con flechas, con peatonal), tamaño, lado, fase (0 = independiente, 1–6 = fase compartida), copiar aspecto al cruce, quitar. Visual con 3 luces (encendida/apagada). Las fases: los semáforos de la misma fase cambian juntos al tocarlos y forman las fases del ciclo automático. El peatonal muestra verde cuando el acceso de coches está en rojo (solo visual: los peatones no lo consultan).
- Vehículos: perfil por tamaño (giros más lentos, más hueco y headway para bus/camión; lo contrario para motos); ruedas, faros, pilotos, ventanas de bus y caja de camión. Máximo de vehículos simultáneos configurable (Ajustes de mapa, 10–200, por defecto 60).
- Validador: flechas que piden giros sin conexión, flechas en carriles sin cruce, fases con movimientos que se cruzan.

**Corrección interna**: un error de edición duplicó media `MapEditorController.cs` durante esta sesión; se reconstruyó comparando con la v3.7 (diff revisado).

**Sigue sin hacer**: aceras/medianas ligadas a la calle (son decoración), agrupar objetos, autobús articulado y más modelos, perfiles editables desde la UI, peatones que obedezcan su semáforo, carriles que empiezan/terminan a mitad de calle.

## v3.9 — Fases pendientes completadas en código (SIN COMPILAR NI PROBAR)

**Nuevo**
- Furgoneta como tipo propio (`VehicleType.Van`, bit 8). Las máscaras de mapas anteriores se adaptan al cargar (featureLevel). Autobús articulado (25 % de los buses, 1,32 de largo, fuelle, giros más lentos). Coches compacto/sedán/grande.
- Perfiles de vehículo editables y guardados en el mapa: Ajustes de mapa → "Perfiles de vehículo" (velocidad, aceleración y separación por tipo, 50–160 %). Porcentaje de taxis, buses, camiones, motos y furgonetas en Ajustes de mapa.
- Acera y mediana propias de cada calle (panel de calle): acera normal/sin/estrecha/ancha; mediana 0/2,7/5,4/9 m de tipo hierba, hormigón o barrera. La mediana separa realmente los carriles (cambia la geometría de la red) y quita la línea central.
- Copiar/pegar propiedades de una calle a otra (carriles, tráfico permitido por carril, aspecto, acera, mediana).
- Carriles que empiezan o terminan a mitad de calle: herramientas "Inicio de carril" y "Fin de carril" (dividen la calle en el punto tocado y cambian los carriles del tramo siguiente). En nodos de paso, un carril que empieza recibe tráfico del carril exterior; uno que termina se incorpora en cremallera.
- Pasos de peatones y líneas de detención: automáticos opcionales (Ajustes de mapa). En mapas nuevos, desactivados: se colocan a mano. Un paso colocado junto a un cruce adelanta la línea de parada; una "Línea de detención" dibujada que cruza un carril cerca del cruce fija dónde se paran los vehículos. Los mapas anteriores conservan sus marcas automáticas.
- Peatones: si el paso está junto a un acceso con semáforo, solo cruzan cuando ese acceso está en rojo.
- Agrupar/desagrupar decoración (Zona): el grupo se mueve, duplica y borra junto. "Borrar zona" respeta los objetos y calles bloqueados.

## v4.0 — Prioridad cero: validación, conexiones y vista previa (SIN COMPILAR NI PROBAR)

**Diagnóstico (causas de los falsos rechazos y de las conexiones rotas)**
- El validador mezclaba «mapa incompleto» con «mapa roto» y daba un único error genérico al intentar jugar («ábrelo en el editor y pulsa Validar»). No decía qué elemento fallaba ni dónde.
- No detectaba conexiones rotas: dos extremos casi juntos sin unir, un extremo pegado al lateral de otra calle sin nodo, calles de longitud casi cero. Normalize() borra en silencio calles con nodos inexistentes y repetidas, así que esos casos desaparecían sin aviso.
- «Ruta imposible» era Error aunque otras entradas funcionaran, y no decía dónde se cortaba la ruta.
- Causa de huecos al editar: al soltar un nodo, solo se unía a otro si estaba a menos de 0,45 unidades, mientras que al dibujar una calle el enganche es de 30 px. Y soltar un extremo sobre el lateral de otra calle no creaba el cruce.
- La validación nunca bloqueó la edición (solo se llama al pulsar Validar o Jugar); lo que bloqueaba era la simulación, con mensajes poco útiles.

**Cambios**
- `MapValidator`: estados `MapReadiness` (En edición / Con advertencias / Listo). Los errores solo impiden simular; el mapa sigue editable y no se modifica. Cada problema tiene categoría (Configuración, Conexión, Geometría, Ruta, Tráfico, Info), qué falla (con coordenadas), cómo corregirlo y posición para centrar la cámara. Nuevas comprobaciones: hueco entre extremos, extremo flotante sobre otra calle, calle casi nula, calle sin carriles, datos inválidos, cruces sin nodo (ahora Error). Ruta incompleta: indica el punto donde se corta; es Error si ninguna entrada llega a una salida y Aviso si solo falla una. Todo va dentro de try/catch.
- `PathfindingSystem.LastRunDeadEnd`: punto donde se corta una ruta.
- Editor: al soltar un nodo se une a otro nodo con la misma tolerancia que al dibujar (0,6–1,0) y, si se suelta sobre el lateral de otra calle, se parte esa calle y se crea el cruce. «Validar» ordena los problemas, centra el primero y muestra el estado. La lista de problemas permite elegir uno y ver «Cómo corregirlo».
- Al pulsar Jugar con un mapa no válido se muestra el primer problema concreto; el mapa sigue abierto.
- Vista previa de calle (`MapEditorPreview.cs`): mientras se arrastra y mientras está pendiente se dibuja la calzada con su ancho real, bordes, línea central amarilla, divisiones de carril, flechas de sentido, longitud aproximada (m), ángulo de orientación, ángulo con la calle a la que conecta (aviso si <25°) e indicadores de conexión (verde = engancha a nodo o calle, rojo = imposible, con el motivo). La calle pendiente tiene botones verde (confirmar) y rojo (cancelar) sobre el mapa, además de los del panel.
- Calle pendiente (tras soltar el dedo): se arrastran los extremos para alargar, acortar o cambiar la dirección (se reenganchan a nodos y calles), el punto central curva, y hay botón azul para invertir el sentido. Mientras el dedo sigue apoyado se alarga/acorta y gira en vivo; la curvatura se ajusta después de soltar, antes de confirmar.
- Menú del editor → «Diagnóstico del sistema»: ejecuta 11 pruebas internas en el propio teléfono (`MapSelfTest.cs`) y guarda `diagnostico.txt`.

- Interfaz móvil: paneles laterales del editor algo más estrechos (112→96) y celdas más bajas (98→90), iconos más pequeños. Es un ajuste modesto: no he visto cómo queda en pantalla.

- Curvas (fase 3, parcial): al continuar un extremo libre con un giro suave (4°–75°), la calle nueva arranca con la curvatura que la une a la anterior sin quiebre. Solo en el momento de dibujar; no se recalcula al mover después.

- Corrección tras la primera prueba en el teléfono: al engancharse a una calle cerca de uno de sus extremos (<0,6) ahora se engancha al nodo, para no crear tramos diminutos (con zoom alto la tolerancia en píxeles era menor que eso y salía una calle de 0,16). Botón «Reparar» en la lista de problemas: funde nodos casi juntos y calles casi nulas, y crea el cruce en extremos pegados a otra calle (un solo Deshacer). La etiqueta de metros/ángulo ya no tapa el botón de confirmar.

**Lo que NO está hecho en esta etapa:** curvas con continuidad entre todas las calles (sigue siendo una curva cuadrática por calle), cambios en la simulación (glorietas, buses articulados, bloqueos) y rediseño completo de la interfaz móvil. Tampoco compilar, ejecutar y probar.

**Anterior:** compilar, ejecutar y probar. Las pruebas del diagnóstico solo cubren datos y validación, no toques ni tráfico en marcha. Ver TESTING.md.

## Pruebas (estado real)
No hay Unity ni compilador de C# en el entorno de trabajo: **ninguna prueba se ha ejecutado**. Ver `TESTING.md` para el procedimiento de las pruebas A–I.

## Compilar la versión Android
1. Abrir la carpeta del proyecto con Unity 6000.0.23f1 (o 6.0 LTS) con el módulo Android Build Support (SDK/NDK/OpenJDK).
2. File → Build Profiles → Android → Switch Platform. La escena es `Assets/Scenes/Main.unity` (todo se crea por código desde GameBootstrap).
3. Player Settings: Company/Product "Intersection Master", Bundle ID `com.intersectionmaster.game`, Minimum API 24+, Scripting Backend IL2CPP, ARM64.
4. Build (APK) o Build App Bundle (AAB). En Unity Cloud Build se usa `BuildTools.cs`.
5. Si la consola muestra errores de compilación, pásame el log: el código no se ha compilado nunca fuera de Cloud Build.

### v4.1 · Propiedades en iconos (sin compilar aún)
- Panel de propiedades de la calle en casillas con icono (3 columnas), como en Intersection Controller 2.
- Ventana «Tráfico permitido»: carril + vehículos, Permitir todos, Solo buses, Copiar a todos / al vecino.
- Estado: solo revisado estáticamente (llaves y nombres de miembros). Falta compilar y probar en el dispositivo.
- Calle peatonal: casilla «Peatonal» en Calle nueva y en Segmento. Ningún vehículo la usa (máscara propia), se dibuja como plaza clara sin marcas. Sin compilar ni probar en dispositivo.

## v5.0 · Editor por carriles (como Intersection Controller 2)

El editor de calles se reemplazó por el modelo por carriles:

- **Cada trazo es un carril de un solo sentido** (va en la dirección del dedo). Una calle de doble sentido son dos carriles pegados (cada carril ocupa una celda de 0,5).
- **Tipos de carril** (Herramienta → Tipo): carril del vehículo, acera (peatones), ferrocarril, carril bici y ruta de barco. Unir carriles de distinto tipo se marca en rojo: «Los segmentos conectados deben ser del mismo tipo».
- **Sin entradas ni salidas**: un carril que no tiene ninguno antes genera vehículos; uno que no sigue a ninguno los retira. Conviene que empiecen y terminen fuera de la línea roja.
- **Los carriles se pueden cruzar sin nodo**: donde se pisan (o pasan muy cerca) los vehículos se ceden el paso. Los peatones y los trenes tienen prioridad.
- **Nodo** (Seleccionar nodo): Semáforo, Ceda el paso, Stop, Farola y Cambiar fase. Las señales se aplican a los carriles que llegan al nodo. Los semáforos cercanos forman un grupo con fases automáticas.
- **Segmento**: Visible, Flechas, Cambio de carril, Tráfico permitido, KM/H, Marcas, líneas izquierda y derecha, color, cables y barandillado.
- **Mapas antiguos**: se convierten solos al abrirlos (cada carril y cada giro del cruce pasa a ser un carril; las señales, paradas, flechas y botones se conservan; las glorietas tienen prioridad en el anillo).

### Comprobación automática (GitHub Actions)
Cada subida compila todos los scripts y ejecuta una simulación sin pantalla (`ci/sim`): el diagnóstico del juego y 20 escenarios de tráfico (cruces, semáforos, ceda el paso, incorporación, acera, tren, mapas antiguos y los 9 de ejemplo) contando llegadas, choques y bloqueos. El resultado queda en la rama `ci-logs`.

Límite conocido: una glorieta diminuta (radio 1,3) con 30 vehículos/min se colapsa; con tráfico medio funciona. No se ha probado todavía en el teléfono (dibujo, paneles y aspecto).

## v5.1 · Dibujo y paneles como Intersection Controller 2

- **Recuadro de confirmación** junto al final del carril, grande y siempre fuera de los paneles: rampa de puente (bajar / plano / subir), ángulo, ✓ y ✗. Muestra en rojo «Los segmentos conectados deben ser del mismo tipo».
- **Capas de −4 a 4** (selector como el original) y **rampas** para puentes y túneles; un carril en otra capa no se cruza con los de abajo.
- **Cuadraditos laterales** en los extremos del último carril para dibujar el de al lado.
- **Recta** siempre recta; enganche preferente al final libre de un carril (al empezar) o al inicio libre (al terminar); unirse a mitad de carril solo tocando su eje.
- **Ventanitas de opciones**: Flechas (dibujo, posición y color), líneas izquierda y derecha, color, KM/H, cables, barandillado y tráfico permitido.
- **Al jugar**: calzada gris clara como el original.
- **Lógica y Decoración** con las mismas herramientas y en el mismo orden que el original; el panel de un objeto usa iconos (tamaño, girar, voltear, tipo, color, duplicar, bloquear).
- Se quitó la categoría «Zona» y el botón azul de invertir.

## v5.2 — Editor más parecido al original
Unión de carriles más tolerante (el extremo que cae sobre un nodo se une a él), cuadraditos laterales en todos los extremos, sin textos de ayuda arriba, selección azul con papelera, modelos de semáforo y «Visible» en el nodo, catálogo de 40 señales dibujadas por código en Decoración y Tabla con estilos y tamaño.
