# Pruebas funcionales (pendientes de ejecutar)

Ninguna de estas pruebas se ha realizado: el entorno donde se escribió el código no tiene Unity ni compilador de C# (sin red para instalarlo). Marca cada una al probarla.

## Etapa 1 (Prioridad cero): validación y conexiones

| # | Caso | Cómo comprobarlo | Estado |
|---|---|---|---|
| 1 | Calle aislada y seguir editando | Dibuja una calle; Menú → Validar; sigue dibujando. Debe decir «En edición» y no cambiar ni bloquear el mapa | Pendiente (lógica cubierta por Diagnóstico nº 1; falta probar en pantalla) |
| 2 | Dos calles conectadas comparten nodo | Dibuja la segunda empezando sobre el extremo de la primera: aro verde y ningún nodo nuevo | Pendiente (Diagnóstico nº 2 solo cubre los datos) |
| 3 | Intersección de cuatro brazos | Cuatro calles al mismo nodo, entradas/salidas fuera de la línea roja → Validar | Pendiente (Diagnóstico nº 3) |
| 4 | Glorieta con entradas y salidas | Herramienta Glorieta + cuatro calles → Validar y Jugar | Pendiente (Diagnóstico nº 4; el tráfico real no se ha probado) |
| 5 | Ruta incompleta | Deja un hueco en la ruta → Jugar: mensaje concreto, mapa intacto y editable | Pendiente (Diagnóstico nº 5; el mensaje en pantalla no se ha visto) |
| 6 | Corregir y volver a validar | Une el hueco (arrastrar un nodo sobre otro) → Validar | Pendiente (Diagnóstico nº 6 y 7 cubren la lógica; el arrastre táctil no) |
| 7 | Guardar y cargar | Guardar, salir, abrir: conexiones intactas | Pendiente (Diagnóstico nº 8 cubre el JSON; no el guardado en disco desde la interfaz) |
| 8b | Editar la calle pendiente | Tras soltar: arrastrar cada extremo (se engancha a nodos), curvar con el punto central, botón azul invierte sentido, verde confirma, rojo cancela | Pendiente (requiere dispositivo) |
| 8c | Enlace suave | Dibuja una calle desde el extremo libre de otra, girando ~20–60° | La nueva sale tangente a la anterior (sin quiebre); el punto central al centro la deja recta | Pendiente (requiere dispositivo) |
| 8 | Vista previa de calle | Arrastrar: calzada, carriles, flechas, metros, ángulo, aro verde/rojo; botones verde/rojo al soltar | Pendiente (requiere dispositivo; el tamaño del texto sobre el mapa no está calibrado) |

**Resultado real (reportado por el usuario, 2026-10-21):** el juego compila y el Diagnóstico del sistema dio 11 de 11 pruebas correctas en el dispositivo. Eso cubre solo la lógica de datos y validación (pruebas 1–7 de arriba a nivel de datos). Siguen pendientes las pruebas táctiles y de tráfico en marcha.

**Comprobado en el teléfono (capturas del usuario, 2026-10-21):** la vista previa de calle se dibuja (asfalto, flechas, «44 m · 54°», botones verde/rojo/azul) y la lista de problemas muestra errores con «Cómo corregirlo». Esa lista detectó una calle real de 0,16 de longitud (causa corregida después, ver README). La etiqueta tapaba el botón verde: movida. Sin verificar todavía: «Reparar», el enganche a nodos cerca de extremos y la edición de extremos.

Cómo ejecutar el diagnóstico en el teléfono: abre el editor → Menú → «Diagnóstico del sistema». Muestra cuántas pruebas pasan y el motivo de cada fallo.

## Etapas anteriores

| Prueba | Pasos | Resultado esperado | Estado |
|---|---|---|---|
| A Construcción | Herramienta Recta: dos toques para una calle; Curva; seleccionar el segmento → A→B +/−; Mediana; Acera | Calle con nodos reales, curva con punto de control, carriles añadidos/quitados sin borrar los demás | Pendiente |
| B Vehículos permitidos | Seleccionar calle → TRÁFICO PERMITIDO → carril → "Solo buses"; subir Autobuses en Ajustes; Jugar → Realista | Solo buses usan ese carril; los demás no entran ni cambian a él | Pendiente |
| C Flechas | Flecha funcional en un carril → solo Derecha; Jugar; cambiar a Recto; poner una Pegatina-flecha | Los coches de ese carril solo giran a la derecha; luego solo recto; la pegatina no cambia nada | Pendiente |
| D Marcas | Decoración → Línea de detención antes de un cruce, moverla; Paso peatonal manual | Los coches paran en la línea; no aparecen pasos automáticos en mapas nuevos | Pendiente |
| E Semáforos | Dos semáforos en un cruce (fase independiente) → en juego tocar uno | Solo cambia el tocado; con la misma fase cambian juntos | Pendiente |
| F Modos | Mismo mapa en Realista y en Caótico (alto) | Realista: sin infracciones; Caótico: saltan rojos, invaden carriles, hay choques | Pendiente |
| G Guardado | Guardar mapa con carril bus, flechas, semáforos (modelo/fase), mediana; cerrar y abrir | Todo se conserva | Pendiente |
| H Estabilidad | Jugar → pausar → volver al editor → cambiar un carril → Jugar | Sin errores en consola ni tráfico duplicado | Pendiente |
| I Android | Instalar el APK en un teléfono, probar con 20/60/120 vehículos | Controles usables, interfaz sin cortes, rendimiento aceptable | Pendiente (requiere dispositivo) |

## Errores conocidos / riesgos
- El código no se ha compilado: es probable que aparezcan errores de compilación en el primer build.
- La lógica de "Inicio/Fin de carril" limita a 3 carriles por sentido.
- El semáforo peatonal se basa en el estado del acceso junto al paso; no hay fase peatonal exclusiva.
- Los perfiles de vehículo no cambian el tamaño de los modelos.


## v5.0 · Modelo por carriles

Probado automáticamente en GitHub Actions (compilación + simulación sin pantalla, 180 s de juego por mapa):
- Diagnóstico del juego: 12 de 12 pruebas correctas.
- Tráfico: 19 escenarios sin choques ni bloqueos (cruces sin señales, con ceda, con semáforos, incorporación, acera, tren, cruce y glorieta antiguos convertidos, y los 9 mapas de ejemplo convertidos).
- Límite conocido: glorieta diminuta con tráfico alto se colapsa (con tráfico medio, bien).

Falta probar en el teléfono:
1. Dibujar un carril de cada tipo (Herramienta → Tipo) y continuar uno desde su extremo.
2. Unir un carril de vehículos con una acera: debe salir en rojo «Los segmentos conectados deben ser del mismo tipo».
3. Seleccionar un nodo y poner Semáforo / Ceda / Stop / Farola; jugar y tocar el semáforo.
4. Abrir un mapa guardado antes de esta versión: debe verse convertido en carriles y funcionar.
5. Panel Segmento: Flechas, KM/H, líneas (Color) y Tráfico permitido.


## v5.1 · Probar en el teléfono
1. Dibujar un carril: el recuadro (✓/✗, ángulo, rampas) debe verse entero y no quedar debajo de los paneles.
2. Tocar un cuadradito lateral del último carril y dibujar el carril de al lado: deben quedar pegados.
3. Hacer un puente: carril con rampa de subida, carril en capa 1 cruzando otro, rampa de bajada. Al jugar no deben cederse el paso.
4. Ventanitas de Segmento: Flechas, Color izquierda/derecha, Color, KM/H, Cables, Barandillado.
5. Lógica y Decoración: menús y panel de un objeto.
Automático (GitHub Actions): compila, diagnóstico 12/12 y 21 escenarios de tráfico sin choques (incluido el puente).

## v5.2 · Probar en el teléfono
1. Empezar un carril sobre la punta de otro y terminar encima del inicio de otro: deben quedar unidos (Validar no da «casi se tocan»).
2. Cuadraditos: salen a ambos lados de las puntas de todos los carriles de la capa; dibujar desde uno deja el carril vecino pegado y el anterior sigue ahí.
3. Con un carril pendiente, empezar a dibujar en otro sitio lo confirma; ✗ lo descarta.
4. Arriba no aparecen textos de ayuda; solo avisos cuando algo no se puede hacer.
5. Carril seleccionado en azul; papelera en el título de Segmento, Nodo y objetos.
6. Nodo con semáforo: Modelo, Lado, Tamaño y Visible.
7. Decoración → Señal de tráfico abre el catálogo; Tipo, Poste, Girar y Voltear.
8. Tabla: sin rejilla / rejilla / con diagonales, Tamaño y Ajuste.

## v5.3 · Probar en el teléfono
1. Tocar un nodo o un carril no lo mueve ni lo curva; solo al arrastrar.
2. El carril solo se crea con ✓.
3. Ventanitas (flechas, líneas, señales) dentro de la pantalla.
4. «Curva» desde la punta de un carril: sale en su misma dirección, sin pico; juntas sin bultos.
5. Líneas automáticas: dos carriles en sentido contrario → amarilla al medio; mismo sentido → blanca discontinua; bordes blancos. Botones «Mismo sentido», «Doble sentido», «Bordes».
6. Señales pegadas al carril y girar con el círculo azul.
7. Semáforo en el editor: poste y franja roja en la línea de parada.

## v5.4 · Probar en el teléfono
1. Sin categoría elegida al entrar; volver a pulsar Carril/Lógica/Decoración la cierra (solo seleccionar nodos y carriles).
2. Abrir un menú cierra el otro.
3. Nodo: tocar solo selecciona; mantener 3 s (anillo que se llena) y luego arrastrar lo mueve. Sin rombo en los carriles.
4. Carril con dos toques (inicio y final) o arrastrando.
5. Nodo de inicio: coches/camiones/buses/motos por minuto y Probabilidad de destino (números sobre el mapa).
6. Stop: estilos y «Controlado por el usuario» (al jugar se toca para abrir/cerrar). Ceda: estilos.
7. Al jugar: semáforos libres (dos en verde aunque se crucen) y sin círculo blanco.
8. Lógica/Decoración: Acción = Construir / Seleccionar.

## v5.5 · Probar en el teléfono
1. Al jugar: barra de arriba (reloj, pausa y velocidades, vehículos/peatones que salen por min, reiniciar; puntos y choques en pequeño).
2. Semáforos como mancha de color por carril; se tocan sobre la mancha.
3. Tráfico: Cantidad (barra), Información (velocidad, ruta azul, Seguir), Stop (coche detenido con señal), Paciencia (halo de color), Volver.
4. Semáforo: Controlador (toca un semáforo, etapas 1–6, verde y espera, Auto), Botón para varios semáforos (se pone tocando el mapa), Todo rojo.
5. Menú al jugar: Ajustes, Construir, Continuar, Reiniciar.
6. Editor → Menú: rejilla (Ajustes, Ajustes de mapa, Guardar, Copiar mapa, Validar, Deshacer, Rehacer, Salir, Continuar). Ajustes: Sonido, Música, Tamaño del menú.
7. Ajustes de mapa: Nombre, Descripción, Vehículos (rejilla y %), General (terreno, lluvia, eventos), Modo de juego (Diverso, Simulación, Controlador de tráfico con objetivo y tiempo).
8. Tráfico permitido: Coche, Camión, Autobús, Tranvía. Tranvía de uno o dos vagones.
9. Eventos (cada 1–2 min): emergencia, avería con grúa, taxi, incendio con bomberos, obras. Lluvia.
10. Coches: luces de freno, intermitentes, brillo; emergencias pasan en rojo.

## v5.6 · Probar en el teléfono
1. Abrir un mapa: ahora mide 200x200; la línea roja queda en el centro. Sin categoría elegida, arrastrar una esquina roja la cambia.
2. Jugar: sin ventana realista/caótico, sin avisos de texto, sin panel abajo. Puntos y choques solo en Controlador de tráfico.
3. En Diverso y Simulación los coches no chocan.
4. Semáforos: poste, brazo con una cabeza por carril y brillo; al jugar las luces cambian de color.
5. Señales pequeñas con poste y sombra.
6. Un toque pone un objeto.
7. Sin líneas donde un carril cruza otro.
