# SEPARA VR — Brief para crear un tráiler audiovisual

> **Propósito de este documento:** servir como contexto y guía de producción para Claude, que deberá crear un tráiler en video a partir de **capturas reales del juego Separa VR** y de cualquier otro material audiovisual que se le facilite.
>
> **Importante:** el material de archivo describe diferentes versiones del proyecto. No confundir funcionalidades planeadas con funcionalidades efectivamente implementadas. Las actualizaciones de 2016, 2025 y 2026 fueron confirmadas directamente por el responsable del proyecto.

## 1. ¿Qué es Separa VR?

**Separa VR** es un videojuego educativo de **realidad virtual** desarrollado originalmente desde la **Municipalidad de Rosario (Argentina)** para enseñar a separar residuos y promover la conciencia ambiental.

El jugador está inmerso en una ciudad virtual inspirada en Rosario. Debe identificar distintos residuos y enviarlos al contenedor correcto. **Sus decisiones transforman el entorno:** a medida que clasifica correctamente, la ciudad pasa de un ambiente sucio, oscuro y desagradable a otro más limpio, luminoso, verde y agradable. La música y los sonidos también acompañan esa transformación.

La idea fuerza es simple: **nuestras acciones pueden cambiar el ambiente**.

El juego se creó para actividades educativas y eventos masivos, con una experiencia de aproximadamente **dos minutos**, aprendizaje inmediato y controles accesibles para personas sin experiencia previa en VR.

### Datos clave

| Aspecto | Información |
|---|---|
| Nombre | **Separa VR** |
| Tipo | Videojuego / experiencia educativa inmersiva |
| Tema | Separación de residuos, reciclaje y educación ambiental |
| Origen | Municipalidad de Rosario, Argentina |
| Primera etapa | **2015**, Foro Latinoamericano de Desarrollo Sustentable |
| Tecnología original | Unity 5, Oculus Rift, Oculus Unity Integration, C# |
| Duración de la experiencia | Aproximadamente **2 minutos por persona** |
| Mecánica central | Clasificar residuos y observar la transformación de la ciudad |
| Público | Niños, jóvenes, adultos, adultos mayores y personas con distintas capacidades |
| Alcance documentado | **Más de 5.000 personas** jugaron en las presentaciones relevadas por el material histórico; no presentarlo como un conteo actualizado a 2026 |

## 2. Evolución del proyecto: 2015–2026

Esta cronología **debe tener presencia en el tráiler**, porque muestra la continuidad del proyecto durante más de una década.

| Año | Plataforma / hito | Interacción o novedad |
|---|---|---|
| **2015** | **Oculus Rift** | Versión original para PC VR, con selección basada en la mirada. |
| **2016** | **Google Cardboard** | Adaptación a VR móvil. |
| **2016** | **Samsung Gear VR** | Adaptación a visor móvil. |
| **2025** | **Meta Quest** | Port a visores autónomos. |
| **2026** | **Meta Quest con hand tracking** | Interacción mediante seguimiento de manos. |

**Nota de fidelidad:** las fechas y los ports a Cardboard, Gear VR, Quest y hand tracking provienen de una actualización directa del responsable del proyecto; no están demostrados por el documento académico original. No representar hand tracking en imágenes de versiones anteriores a 2026.

## 3. ¿Qué muestra el gameplay?

1. Una ciudad tridimensional que evoca Rosario, con edificios, calles, vegetación y contenedores.
2. Residuos que el usuario debe identificar y clasificar.
3. Selección del residuo y del contenedor correspondiente.
4. Respuesta inmediata de la escena al acierto o error.
5. Transición progresiva entre **ciudad contaminada** y **ciudad limpia**.
6. En la versión original, selección utilizando la **mirada** (raycast + tiempo de permanencia); en la versión 2026, **seguimiento de manos**, si hay capturas que lo acrediten.

Los documentos del juego presentado describen **dos contenedores principales**: reciclables y no reciclables (naranja y verde, respectivamente, en las propuestas). Algunos borradores iniciales incluían **una compostera como tercer destino**, pero no debe presentarse como una funcionalidad final sin pruebas visuales.

### Rasgos visuales importantes

- El contraste **sucio/oscuro → limpio/verde/luminoso** es el recurso narrativo principal.
- Ciudad 3D construida con modelos modulares, geometría y texturas reutilizadas.
- Interfaces y carteles integrados en el espacio tridimensional.
- Controles sencillos: el usuario no necesita conocer un gamepad en la versión original.
- Diseño confortable: jugador en una posición fija y prevención del mareo.
- Música y paisajes sonoros que acompañan el estado ambiental.

## 4. Equipo original

- **Gregorio D'Angelo:** coordinación del proyecto, game design y arte 3D.
- **Mariano Suligoy:** programación.
- **Germán Tello:** música y sonido.

**Marco institucional:** trabajo desde la Municipalidad de Rosario, articulando la Dirección General de Comunicación Social y la Subsecretaría de Medio Ambiente.

Para los créditos del tráiler usar estos roles y nombres tal como figuran; no atribuirles autoría de los ports posteriores salvo que se confirme.

## 5. Presentaciones y público

### Eventos y espacios documentados

- Foro Latinoamericano de Desarrollo Sustentable (Rosario).
- Calle Recreativa (Rosario).
- Escuelas públicas y actividades escolares.
- Vacaciones de invierno en Centros Municipales de Distrito (Rosario).
- Encuentro de Colectividades (Rosario).
- Feria Saludable (Rosario).
- **Noche de Peatonales (Rosario)** — confirmado por el responsable del proyecto.
- **Encuentro Mayor** — eventos destinados a personas mayores; confirmado por el responsable del proyecto.
- Exposición de Videojuegos Argentina (EVA), en Buenos Aires, Córdoba, Mendoza y Tucumán.
- MICA, Buenos Aires.
- Expolúdica, La Plata.
- Meet de Devs, Centro Cultural San Martín, CABA.
- Congreso Internacional de Videojuegos y Educación (CIVE 2015), Universidad Nacional de Tres de Febrero.

El juego fue utilizado por públicos diversos, desde **niños pequeños hasta adultos mayores** y personas con distintas capacidades. La accesibilidad es un mensaje central, pero evitar afirmaciones absolutas como «100 % accesible» o «funciona para todos».

## 6. Objetivo del tráiler

Producir un video **emocionante, claro y profesional** que permita entender en menos de un minuto y medio:

1. **Qué es** Separa VR y cómo se juega.
2. **Qué enseña:** la separación de residuos y el efecto de las acciones individuales en el ambiente.
3. **Por qué es especial:** interacción sencilla, accesibilidad y demostraciones públicas.
4. **Cómo evolucionó:** de Oculus Rift en 2015 a Meta Quest con hand tracking en 2026.

**Audiencia:** público general, docentes, instituciones educativas, organizadores de eventos, municipios y profesionales de VR.

**Tono:** optimista, ágil, humano, tecnológico sin resultar frío. Que se sienta como un proyecto con historia y vigencia, no como publicidad exagerada.

**Formato principal sugerido:** 16:9, 1920 × 1080, 30 fps, duración objetivo **75 segundos** (aceptable 60–90 s). Idioma: **español de Argentina**, sin modismos forzados. Música con progresión desde un clima más apagado hacia uno luminoso. Subtítulos legibles.

## 7. Guion / storyboard sugerido (75 segundos)

| Tiempo | Imagen / edición sugerida | Texto en pantalla | Locución sugerida |
|---|---|---|---|
| **0–7 s** | Plano del escenario contaminado. Inicio sobrio, sonido ambiente, aparición del título. | **SEPARA VR** / *Una experiencia de educación ambiental* | «¿Y si pudieras ver cómo tus decisiones transforman una ciudad?» |
| **7–19 s** | Mostrar residuos, selección y contenedores; corte sincronizado con una clasificación correcta. | **Aprender haciendo** | «En Separa VR, cada residuo tiene su lugar. Y cada elección tiene una consecuencia.» |
| **19–32 s** | El momento más potente: antes/después de la ciudad, iluminación, árboles, limpieza; ritmo musical ascendente. | **Tus acciones cambian el entorno** | «Cuando separás correctamente, la ciudad se transforma: se vuelve más limpia, más verde y más viva.» |
| **32–43 s** | Gameplay centrado en interacción, mira por mirada si existe captura; imágenes auténticas de personas usando el visor, si las hay. | **Simple. Inmersivo. Accesible.** | «Diseñado para que chicos, jóvenes y adultos mayores puedan participar, incluso si nunca probaron la realidad virtual.» |
| **43–59 s** | Montaje cronológico de capturas con rótulos: Rift → Cardboard / Gear VR → Quest → Quest hand tracking. Si no hay capturas por plataforma, usar gameplay neutro y títulos sin simular dispositivos. | **2015 → 2016 → 2025 → 2026** / rótulos de cada plataforma | «Nació en Oculus Rift. Llegó a Cardboard y Gear VR. Y siguió evolucionando hasta Meta Quest y el seguimiento de manos.» |
| **59–68 s** | Tomas de eventos y participantes (si se dispone); alternar con escenas limpias del juego. | **Más de 5.000 participantes*** / **Rosario y eventos de todo el país** | «Una experiencia nacida en Rosario que recorrió escuelas, ferias y encuentros de todo el país.» |
| **68–75 s** | Mejor escena de la ciudad limpia, logo y crédito final. Cierre musical. | **SEPARA VR** / **Más de una década aprendiendo a cuidar el ambiente** | «Separa VR. Aprender a cuidar el ambiente, desde adentro.» |

\* Si se utiliza el dato de 5.000 personas, aclarar en un pequeño texto o en la descripción que es la cifra registrada en la documentación histórica, no una medición actualizada.

### Alternativas de frase final

- **«Cada decisión cuenta. Cada residuo tiene su lugar.»**
- **«Una ciudad mejor empieza con una decisión.»**
- **«Separa VR: el cambio empieza por vos.»**

> **La propuesta narrativa y la locución son sugerencias creativas, no citas literales de los documentos del proyecto.**

## 8. Instrucciones visuales y de edición para Claude

### Prioridad absoluta: fidelidad al material

- Usar como base **capturas, videos y fotografías auténticas de Separa VR** aportados por el usuario.
- No generar ni insertar gameplay inventado y presentarlo como si fuera del juego real.
- No sustituir las escenas del videojuego por una ciudad genérica de stock.
- No mostrar gestos de hand tracking, visores específicos ni interfaces que no estén representados en el material disponible; se pueden mencionar con rótulos cuando la actualización esté confirmada.
- Si solamente hay **capturas estáticas**, crear movimiento de edición mediante *push-ins*, paneos suaves, recortes, capas, transiciones, textos y montaje; evitar movimientos 3D agresivos que deformen el gameplay.
- Respetar la relación de aspecto y no recortar elementos esenciales (contenedores, residuos, mira, evolución del entorno).

### Dirección de arte

- Abrir con tonos apagados del entorno contaminado y evolucionar a una paleta viva y verde, siguiendo lo que hace el propio juego.
- Tipografía sans serif limpia, títulos breves, jerarquía visual fuerte.
- Gráficos minimalistas, sin exceso de HUDs ficticios ni efectos de «tecnología futurista».
- Aprovechar antes/después en corte directo o pantalla dividida cuando existan capturas comparables.
- Conectar las épocas mediante tarjetas breves de fecha y plataforma; que parezca una historia de evolución, no una lista de especificaciones.

### Música y sonido

- Usar música original del proyecto si se dispone y están claros los permisos de uso; si no, música con licencia adecuada.
- Si existen audios del juego, aprovechar sonidos ambientales que acompañan el paso de la contaminación al ambiente limpio.
- Sonorizar aciertos, transiciones y títulos sin tapar la locución.

### Texto / créditos

- No afirmar que hubo mediciones de aprendizaje: el material documenta participación, no un estudio cuantitativo de eficacia educativa.
- El dato **«más de 5.000 personas»** viene de documentación histórica: evitar proyectarlo como total acumulado hasta 2026.
- Créditos recomendados en el cierre o descripción: **Gregorio D'Angelo (Game Design / Arte 3D / Coordinación), Mariano Suligoy (Programación), Germán Tello (Música / Sonido)**; proyecto original desarrollado desde la **Municipalidad de Rosario**.
- Si se cuenta con logos oficiales, utilizarlos sin alterarlos; no fabricar escudos o identidades visuales oficiales.

## 9. Material que Claude debe recibir o identificar

**Imprescindible:**

- Capturas o video del gameplay original que incluyan la ciudad, residuos y contenedores.
- Al menos una captura de la ciudad contaminada y otra de la ciudad limpia, idealmente desde ángulos similares.
- Nombre o imagen del logo oficial «Separa VR» (si existe).

**Muy conveniente:**

- Gameplay o imágenes de la versión **2026 con hand tracking**.
- Capturas de las versiones **Cardboard, Gear VR y Quest**, claramente identificadas.
- Fotografías y grabaciones de actividades públicas, especialmente con niños y personas mayores (con permisos de imagen adecuados).
- Música y efectos originales.
- Datos de resolución / orientación de las capturas y destino de publicación.

**Si faltan recursos:** no detener el diseño ni inventar material. Crear la mejor versión posible con lo recibido y señalar claramente los planos sustituidos por títulos o gráficos.

## 10. Entregables solicitados a Claude

1. **Tráiler principal** de ~75 s, 16:9, Full HD, H.264 en MP4, listo para compartir.
2. **Proyecto editable**, si la herramienta o flujo de trabajo lo permite (por ejemplo, código, timeline o archivos de edición).
3. **Guion de locución y textos en pantalla** en un archivo independiente.
4. **Listado de materiales usados y faltantes**, indicando el origen de cada captura/clip y cualquier afirmación que deba verificarse.
5. Si se dispone de tiempo y material, un **recorte vertical 9:16 de 25–35 s** para redes (opcional; no inventar encuadres imposibles).

## 11. Fuentes de contexto consultadas

Fuentes de Google Drive usadas para reconstruir el proyecto:

- **Separa VR Congreso Medio Ambiente** — descripción académica del juego, decisiones de diseño, accesibilidad y alcance histórico.
- **Separa CIVE** y **Separa VR.ppsx** — presentaciones del desarrollo técnico, diseño y participación en eventos.
- **SeparaVR.mx / Separa SF** — síntesis y presencia en eventos.
- **Separación VR** y propuestas/presupuestos iniciales — concepto original y planificación; algunas características propuestas no equivalen a funcionalidades finales.

**Actualizaciones confirmadas directamente por Gregorio D'Angelo:** ports a **Cardboard y Gear VR (2016)**, **Meta Quest (2025)**, **Quest con hand tracking (2026)**; exhibiciones en **Noche de Peatonales** y **Encuentro Mayor**.

---

## 12. Pedido concreto para Claude

**Creá un tráiler emotivo y profesional sobre Separa VR usando las capturas reales del juego que te voy a proporcionar.** Seguí el resumen, la cronología y la estructura narrativa de este documento como guía, pero priorizá el ritmo audiovisual y la claridad. El eje emocional debe ser la transformación de la ciudad causada por las acciones del jugador; el segundo eje, la evolución tecnológica del proyecto entre 2015 y 2026. Entregá el MP4 final y el proyecto editable si tu entorno permite producirlos. Si la herramienta no puede renderizar video, entregá un proyecto de edición reproducible y todas las instrucciones necesarias, indicando con precisión lo que quedó pendiente. **No inventes gameplay, cifras ni funciones no acreditadas por el material.**
