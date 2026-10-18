// Arma el mini tutorial en la escena Intro (menú SeparaVR/Construir tutorial Intro).
// Copia de NuevoJuego los contenedores naranja y verde y los residuos Agua y panial, los acomoda frente al jugador
// bajo una raíz "Tutorial", reusa el Canvas 1 como texto de instrucciones, mueve el Push Button a un costado como
// botón "Saltar" y conecta todo a TutorialIntro. Es idempotente: borra la raíz "Tutorial" anterior y la rehace.
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class TutorialIntroBuilder
{
	const string IntroPath = "Assets/Scenes/Intro.unity";
	const string JuegoPath = "Assets/Scenes/NuevoJuego.unity";
	const string RaizNombre = "Tutorial";
	const string FuentePath = "Assets/Resources/fonts & materials/LiberationSans SDF.asset";
	const string ClipPasoPath = "Assets/AudioClip/Button_22_click.ogg";
	const string ClipFinalPath = "Assets/AudioClip/up.ogg";
	const string ClipErrorPath = "Assets/AudioClip/nana.ogg";

	// ---- contenedores: ~3 m adelante del jugador, a ±35°, naranja a la derecha y verde a la izquierda ----
	const float DistanciaContenedores = 3f;
	const float AnguloContenedores = 35f;
	const float AlturaContenedores = 0.99f;

	// ---- objetos de práctica: arriba del pedestal "Cube (1)" ----
	const float SeparacionPedestal = 0.02f;

	// ---- botón Saltar: a la izquierda del jugador, más bajo y más chico ----
	const float SaltarIzquierda = 0.6f;
	const float SaltarAdelante = 0.25f;
	const float SaltarAlturaSoporte = 0.8f;
	const float SaltarEscala = 0.6f;
	// Altura del pivote del Push Button sobre la superficie donde apoya, a escala 1 (en la Intro original: y 1.071
	// sobre el pedestal de tope 0.998). Constante para que reconstruir dé siempre el mismo resultado.
	const float BotonSobreApoyo = 0.073f;
	const float SaltarAnchoSoporte = 0.2f;
	const float SaltarEtiquetaAltura = 0.16f;
	const float SaltarEtiquetaFontSize = 0.7f;
	const float AlturaCabeza = 1.6f;

	// ---- texto de instrucciones (Canvas 1/Panel/Text (TMP)) ----
	const float TextoFontSizeMin = 6f;
	const float TextoFontSizeMax = 12f;

	// ---- flecha de ayuda (TextMeshPro 3D: fontSize 10 ≈ 1 m) ----
	const string FlechaTexto = "▼";
	const float FlechaFontSize = 1.5f;

	// Posición del rig de NuevoJuego si no se encuentra su XROrigin.
	static readonly Vector3 RigJuegoFallback = new Vector3(-8.088f, 0.5f, 8.562f);
	// Rig de la Intro si no se encuentra su XROrigin.
	static readonly Vector3 RigIntroFallback = new Vector3(0f, 0.1f, -1f);

	[MenuItem("SeparaVR/Construir tutorial Intro")]
	static void Construir()
	{
		if (Application.isPlaying)
		{
			Debug.LogError("TutorialIntroBuilder: salí del modo Play antes de construir el tutorial.");
			return;
		}

		// ---- 1. Intro tiene que ser la escena activa ----
		Scene intro = SceneManager.GetActiveScene();
		if (intro.path != IntroPath)
		{
			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				Debug.LogWarning("TutorialIntroBuilder: cancelado (no se guardaron las escenas abiertas).");
				return;
			}
			intro = EditorSceneManager.OpenScene(IntroPath, OpenSceneMode.Single);
			if (!intro.IsValid() || intro.path != IntroPath)
			{
				Debug.LogError("TutorialIntroBuilder: no se pudo abrir " + IntroPath);
				return;
			}
		}

		StringBuilder log = new StringBuilder("TutorialIntroBuilder: tutorial construido en " + IntroPath + "\n");
		List<string> errores = new List<string>();

		// ---- 2. Objetos necesarios de la Intro (antes de tocar nada) ----
		GameObject pedestal = BuscarRaiz(intro, "Cube (1)");
		GameObject pushButton = BuscarRaiz(intro, "Push Button");
		GameObject canvas = BuscarRaiz(intro, "Canvas 1");
		TMP_Text texto = null;
		Transform logo = null;
		if (canvas != null)
		{
			Transform textoTransform = canvas.transform.Find("Panel/Text (TMP)");
			if (textoTransform != null)
			{
				texto = textoTransform.GetComponent<TMP_Text>();
			}
			logo = canvas.transform.Find("Panel/Separa");
		}
		if (pedestal == null)
		{
			errores.Add("No se encontró el pedestal 'Cube (1)' en la raíz de Intro.");
		}
		if (canvas == null)
		{
			errores.Add("No se encontró 'Canvas 1' en la raíz de Intro.");
		}
		else if (texto == null)
		{
			errores.Add("No se encontró el texto TMP 'Canvas 1/Panel/Text (TMP)'.");
		}
		if (errores.Count > 0)
		{
			Reportar(errores);
			return;
		}
		if (logo == null)
		{
			Debug.LogWarning("TutorialIntroBuilder: no se encontró el logo 'Canvas 1/Panel/Separa'; el tutorial no lo va a achicar.");
		}
		XRBaseInteractable botonSaltar = pushButton != null ? pushButton.GetComponent<XRBaseInteractable>() : null;
		if (botonSaltar == null)
		{
			Debug.LogWarning("TutorialIntroBuilder: no se encontró 'Push Button' con un XRBaseInteractable; no habrá botón Saltar.");
		}
		GameStart gameStart = BuscarComponente<GameStart>(intro);
		if (gameStart == null)
		{
			Debug.LogWarning("TutorialIntroBuilder: no hay GameStart en Intro; al terminar se cargará el juego directo con ScreenFader.");
		}
		XRInteractionManager managerIntro = BuscarComponente<XRInteractionManager>(intro);

		Vector3 rigIntro = RigIntroFallback;
		Vector3 adelante = Vector3.forward;
		XROrigin origenIntro = BuscarComponente<XROrigin>(intro);
		if (origenIntro != null)
		{
			rigIntro = origenIntro.transform.position;
			adelante = Horizontal(origenIntro.transform.forward, Vector3.forward);
		}
		else
		{
			Debug.LogWarning("TutorialIntroBuilder: no se encontró el XROrigin de Intro; se usa " + rigIntro);
		}
		Vector3 derecha = Vector3.Cross(Vector3.up, adelante);

		// ---- 3. Copias desde NuevoJuego ----
		Scene juego = SceneManager.GetSceneByPath(JuegoPath);
		bool juegoYaAbierta = juego.IsValid() && juego.isLoaded;
		if (!juegoYaAbierta)
		{
			juego = EditorSceneManager.OpenScene(JuegoPath, OpenSceneMode.Additive);
			if (!juego.IsValid() || !juego.isLoaded)
			{
				Debug.LogError("TutorialIntroBuilder: no se pudo abrir " + JuegoPath);
				return;
			}
		}

		GameObject naranjaOriginal = BuscarRaiz(juego, "naranja");
		GameObject verdeOriginal = BuscarRaiz(juego, "verde");
		GameObject residuos = BuscarRaiz(juego, "Residuos");
		Transform aguaOriginal = residuos != null ? residuos.transform.Find("Agua") : null;
		Transform panialOriginal = residuos != null ? residuos.transform.Find("panial") : null;
		if (naranjaOriginal == null || naranjaOriginal.GetComponent<Container>() == null)
		{
			errores.Add("No se encontró el contenedor raíz 'naranja' (con Container) en NuevoJuego.");
		}
		if (verdeOriginal == null || verdeOriginal.GetComponent<Container>() == null)
		{
			errores.Add("No se encontró el contenedor raíz 'verde' (con Container) en NuevoJuego.");
		}
		if (aguaOriginal == null || aguaOriginal.GetComponent<CustomGrabInteractable>() == null)
		{
			errores.Add("No se encontró 'Residuos/Agua' (con CustomGrabInteractable) en NuevoJuego.");
		}
		if (panialOriginal == null || panialOriginal.GetComponent<CustomGrabInteractable>() == null)
		{
			errores.Add("No se encontró 'Residuos/panial' (con CustomGrabInteractable) en NuevoJuego.");
		}
		if (errores.Count > 0)
		{
			CerrarJuego(juego, juegoYaAbierta);
			Reportar(errores);
			return;
		}

		// Frente de los contenedores en el juego: hacia el rig (promedio de las dos direcciones, como están en fila).
		Vector3 rigJuego = RigJuegoFallback;
		XROrigin origenJuego = BuscarComponente<XROrigin>(juego);
		if (origenJuego != null)
		{
			rigJuego = origenJuego.transform.position;
		}
		Vector3 frenteJuego = Horizontal(
			Horizontal(rigJuego - naranjaOriginal.transform.position, Vector3.back) +
			Horizontal(rigJuego - verdeOriginal.transform.position, Vector3.back), Vector3.back);

		// Borra el tutorial anterior (recién ahora, con todo lo necesario encontrado).
		int borrados = 0;
		foreach (GameObject raizVieja in intro.GetRootGameObjects())
		{
			if (raizVieja.name == RaizNombre)
			{
				Object.DestroyImmediate(raizVieja);
				borrados++;
			}
		}
		if (borrados > 0)
		{
			log.Append("- Borrada la raíz '" + RaizNombre + "' anterior (" + borrados + ").\n");
		}

		GameObject raiz = new GameObject(RaizNombre);
		SceneManager.MoveGameObjectToScene(raiz, intro);
		raiz.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
		raiz.transform.localScale = Vector3.one;

		GameObject naranja = Copiar(naranjaOriginal.transform, raiz.transform, intro, juego, managerIntro, log);
		GameObject verde = Copiar(verdeOriginal.transform, raiz.transform, intro, juego, managerIntro, log);
		GameObject agua = Copiar(aguaOriginal, raiz.transform, intro, juego, managerIntro, log);
		GameObject panial = Copiar(panialOriginal, raiz.transform, intro, juego, managerIntro, log);

		CerrarJuego(juego, juegoYaAbierta);

		// ---- 4. Contenedores ----
		UbicarContenedor(naranja, rigIntro, adelante, AnguloContenedores, frenteJuego);
		UbicarContenedor(verde, rigIntro, adelante, -AnguloContenedores, frenteJuego);
		log.Append("- naranja en " + naranja.transform.position.ToString("F2") + " (derecha), verde en " + verde.transform.position.ToString("F2") + " (izquierda).\n");

		// ---- 5. Objetos de práctica sobre el pedestal ----
		Bounds pedestalBounds = BoundsDe(pedestal);
		Vector3 apoyo = new Vector3(pedestalBounds.center.x, pedestalBounds.max.y + SeparacionPedestal, pedestalBounds.center.z);
		ApoyarEn(agua, apoyo);
		ApoyarEn(panial, apoyo);
		panial.SetActive(false);
		log.Append("- Agua y panial sobre '" + pedestal.name + "' en " + apoyo.ToString("F2") + " (panial inactivo hasta el paso 4).\n");

		// ---- 6. Botón Saltar ----
		if (pushButton != null)
		{
			ArmarBotonSaltar(pushButton, pedestal, pedestalBounds, raiz.transform, rigIntro, adelante, derecha, log);
		}

		// ---- 7. Texto de instrucciones ----
		texto.enableAutoSizing = true;
		texto.fontSizeMin = TextoFontSizeMin;
		texto.fontSizeMax = TextoFontSizeMax;
		texto.textWrappingMode = TextWrappingModes.Normal;
		texto.alignment = TextAlignmentOptions.Center;
		texto.richText = true;
		texto.text = "¡Bienvenido a Separá!";
		Marcar(texto);
		log.Append("- Texto de instrucciones: '" + RutaDe(texto.transform) + "' (auto size " + TextoFontSizeMin + "-" + TextoFontSizeMax + ", wrap).\n");

		// ---- 8. Flecha de ayuda ----
		TMP_FontAsset fuente = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FuentePath);
		if (fuente == null)
		{
			Debug.LogWarning("TutorialIntroBuilder: no se encontró la fuente " + FuentePath + "; se usa la fuente por defecto de TMP.");
		}
		TextMeshPro flecha = CrearTexto3D("Flecha ayuda", FlechaTexto, FlechaFontSize, TutorialIntro.ColorAyuda, fuente, raiz.transform);
		flecha.rectTransform.sizeDelta = new Vector2(0.3f, 0.3f);
		flecha.transform.position = apoyo + Vector3.up * 0.5f;
		flecha.gameObject.SetActive(false);

		// ---- 9. Audio y TutorialIntro ----
		AudioSource audio = raiz.AddComponent<AudioSource>();
		audio.playOnAwake = false;
		audio.loop = false;
		audio.spatialBlend = 0f;

		TutorialIntro tutorial = raiz.AddComponent<TutorialIntro>();
		tutorial.botella = agua.GetComponent<CustomGrabInteractable>();
		tutorial.panial = panial.GetComponent<CustomGrabInteractable>();
		tutorial.naranja = naranja.GetComponent<Container>();
		tutorial.verde = verde.GetComponent<Container>();
		tutorial.texto = texto;
		tutorial.logo = logo;
		tutorial.flecha = flecha.transform;
		tutorial.botonSaltar = botonSaltar;
		tutorial.gameStart = gameStart;
		tutorial.audioSource = audio;
		tutorial.sonidoPaso = CargarClip(ClipPasoPath);
		tutorial.sonidoFinal = CargarClip(ClipFinalPath);
		tutorial.sonidoError = CargarClip(ClipErrorPath);
		Marcar(tutorial);
		log.Append("- TutorialIntro y AudioSource en '" + RaizNombre + "'; sonidos: " +
			Nombre(tutorial.sonidoPaso) + ", " + Nombre(tutorial.sonidoFinal) + ", " + Nombre(tutorial.sonidoError) + ".\n");
		if (tutorial.naranja.IDcontainer != Container.Type.RECYCLE || tutorial.verde.IDcontainer != Container.Type.GARBAGE)
		{
			Debug.LogWarning("TutorialIntroBuilder: los tipos de contenedor no son los esperados (naranja = RECYCLE, verde = GARBAGE).");
		}

		// ---- 10. Guardar ----
		EditorSceneManager.MarkSceneDirty(intro);
		bool guardada = EditorSceneManager.SaveScene(intro);
		log.Append(guardada ? "- Escena guardada.\n" : "- ¡No se pudo guardar la escena! Guardala a mano.\n");
		if (guardada)
		{
			Debug.Log(log.ToString());
		}
		else
		{
			Debug.LogError(log.ToString());
		}
		Selection.activeGameObject = raiz;
	}

	// ---------------------------------------------------------------- copias

	// Copia un objeto de NuevoJuego a la Intro bajo la raíz, con la misma pose y escala en el mundo.
	static GameObject Copiar(Transform original, Transform raiz, Scene intro, Scene juego, XRInteractionManager managerIntro, StringBuilder log)
	{
		Vector3 posicion = original.position;
		Quaternion rotacion = original.rotation;
		Vector3 escala = original.lossyScale;

		GameObject copia = Object.Instantiate(original.gameObject);
		copia.name = original.name;
		if (copia.scene != intro)
		{
			SceneManager.MoveGameObjectToScene(copia, intro);
		}
		copia.transform.SetParent(raiz, false);
		copia.transform.SetPositionAndRotation(posicion, rotacion);
		copia.transform.localScale = escala;
		LimpiarReferenciasExternas(copia, juego, managerIntro, log);
		return copia;
	}

	// Las referencias a objetos de NuevoJuego no sobreviven al cerrarla: el interaction manager se cambia por el de la
	// Intro y el resto se anula (y se avisa).
	static void LimpiarReferenciasExternas(GameObject copia, Scene juego, XRInteractionManager managerIntro, StringBuilder log)
	{
		foreach (Component componente in copia.GetComponentsInChildren<Component>(true))
		{
			if (componente == null)
			{
				continue;   // script faltante
			}
			SerializedObject serializado = new SerializedObject(componente);
			SerializedProperty propiedad = serializado.GetIterator();
			bool cambio = false;
			while (propiedad.Next(true))
			{
				if (propiedad.propertyType != SerializedPropertyType.ObjectReference || propiedad.objectReferenceValue == null)
				{
					continue;
				}
				Object valor = propiedad.objectReferenceValue;
				GameObject destino = valor as GameObject;
				if (destino == null && valor is Component)
				{
					destino = ((Component)valor).gameObject;
				}
				if (destino == null || destino.scene != juego)
				{
					continue;
				}
				if (propiedad.name == "m_InteractionManager")
				{
					propiedad.objectReferenceValue = managerIntro;
				}
				else
				{
					propiedad.objectReferenceValue = null;
					log.Append("  (aviso) " + RutaDe(componente.transform) + " " + componente.GetType().Name + "." + propiedad.propertyPath +
						" apuntaba a '" + destino.name + "' de NuevoJuego; quedó vacío.\n");
				}
				cambio = true;
			}
			if (cambio)
			{
				serializado.ApplyModifiedPropertiesWithoutUndo();
			}
		}
	}

	static void CerrarJuego(Scene juego, bool juegoYaAbierta)
	{
		if (!juegoYaAbierta && juego.IsValid())
		{
			EditorSceneManager.CloseScene(juego, true);
		}
	}

	// ---------------------------------------------------------------- ubicación

	static void UbicarContenedor(GameObject contenedor, Vector3 rig, Vector3 adelante, float angulo, Vector3 frenteJuego)
	{
		Vector3 direccion = Quaternion.AngleAxis(angulo, Vector3.up) * adelante;
		Vector3 posicion = rig + direccion * DistanciaContenedores;
		posicion.y = AlturaContenedores;
		// Gira en Y lo necesario para que el frente que tenía en el juego mire al jugador de la Intro.
		Vector3 frenteNuevo = Horizontal(rig - posicion, -direccion);
		float giro = Vector3.SignedAngle(frenteJuego, frenteNuevo, Vector3.up);
		Quaternion rotacion = Quaternion.AngleAxis(giro, Vector3.up) * contenedor.transform.rotation;
		contenedor.transform.SetPositionAndRotation(posicion, rotacion);
	}

	// Apoya el objeto (su caja de renderers) centrado sobre el punto dado, conservando su rotación y escala.
	static void ApoyarEn(GameObject objeto, Vector3 apoyo)
	{
		objeto.transform.position = apoyo;
		Bounds bounds = BoundsDe(objeto);
		Vector3 correccion = new Vector3(apoyo.x - bounds.center.x, apoyo.y - bounds.min.y, apoyo.z - bounds.center.z);
		objeto.transform.position = apoyo + correccion;
	}

	static void ArmarBotonSaltar(GameObject pushButton, GameObject pedestal, Bounds pedestalBounds, Transform raiz,
		Vector3 rig, Vector3 adelante, Vector3 derecha, StringBuilder log)
	{
		float piso = pedestalBounds.min.y;
		Vector3 baseBoton = rig - derecha * SaltarIzquierda + adelante * SaltarAdelante;
		float tope = piso + SaltarAlturaSoporte;

		// Soporte: copia chica del pedestal.
		GameObject soporte = Object.Instantiate(pedestal);
		soporte.name = "Soporte Saltar";
		soporte.transform.SetParent(raiz, false);
		soporte.transform.rotation = Quaternion.identity;
		soporte.transform.localScale = Vector3.one;
		Bounds unitario = BoundsDe(soporte);
		Vector3 tamano = unitario.size;
		soporte.transform.localScale = new Vector3(
			SaltarAnchoSoporte / Mathf.Max(tamano.x, 1e-4f),
			SaltarAlturaSoporte / Mathf.Max(tamano.y, 1e-4f),
			SaltarAnchoSoporte / Mathf.Max(tamano.z, 1e-4f));
		soporte.transform.position = new Vector3(baseBoton.x, piso, baseBoton.z);
		Bounds soporteBounds = BoundsDe(soporte);
		soporte.transform.position += new Vector3(baseBoton.x - soporteBounds.center.x, piso - soporteBounds.min.y, baseBoton.z - soporteBounds.center.z);

		// Botón: misma altura sobre su apoyo que tenía sobre el pedestal, escalada. Se conserva su rotación.
		pushButton.transform.localScale = Vector3.one * SaltarEscala;
		pushButton.transform.position = new Vector3(baseBoton.x, tope + BotonSobreApoyo * SaltarEscala, baseBoton.z);
		Marcar(pushButton.transform);

		// Etiqueta "Saltar" arriba del botón, mirando al jugador.
		TMP_FontAsset fuente = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FuentePath);
		TextMeshPro etiqueta = CrearTexto3D("Etiqueta Saltar", "Saltar", SaltarEtiquetaFontSize, Color.white, fuente, raiz);
		etiqueta.rectTransform.sizeDelta = new Vector2(0.4f, 0.12f);
		Vector3 posicionEtiqueta = pushButton.transform.position + Vector3.up * SaltarEtiquetaAltura;
		Vector3 cabeza = new Vector3(rig.x, piso + AlturaCabeza, rig.z);
		etiqueta.transform.SetPositionAndRotation(posicionEtiqueta,
			Quaternion.LookRotation(Horizontal(posicionEtiqueta - cabeza, adelante), Vector3.up));

		log.Append("- Push Button movido a " + pushButton.transform.position.ToString("F2") + " (escala " + SaltarEscala + ") con soporte y etiqueta 'Saltar'.\n");
	}

	// ---------------------------------------------------------------- utilidades

	static TextMeshPro CrearTexto3D(string nombre, string contenido, float fontSize, Color color, TMP_FontAsset fuente, Transform padre)
	{
		GameObject objeto = new GameObject(nombre, typeof(RectTransform));
		objeto.transform.SetParent(padre, false);
		TextMeshPro tmp = objeto.AddComponent<TextMeshPro>();
		if (fuente != null)
		{
			tmp.font = fuente;
		}
		tmp.text = contenido;
		tmp.fontSize = fontSize;
		tmp.enableAutoSizing = false;
		tmp.color = color;
		tmp.alignment = TextAlignmentOptions.Center;
		tmp.textWrappingMode = TextWrappingModes.NoWrap;
		return tmp;
	}

	static GameObject BuscarRaiz(Scene escena, string nombre)
	{
		foreach (GameObject raiz in escena.GetRootGameObjects())
		{
			if (raiz.name == nombre)
			{
				return raiz;
			}
		}
		return null;
	}

	static T BuscarComponente<T>(Scene escena) where T : Component
	{
		foreach (GameObject raiz in escena.GetRootGameObjects())
		{
			T componente = raiz.GetComponentInChildren<T>(true);
			if (componente != null)
			{
				return componente;
			}
		}
		return null;
	}

	static Bounds BoundsDe(GameObject objeto)
	{
		bool hay = false;
		Bounds bounds = new Bounds(objeto.transform.position, Vector3.zero);
		foreach (Renderer renderer in objeto.GetComponentsInChildren<Renderer>(true))
		{
			if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
			{
				continue;
			}
			if (!hay)
			{
				bounds = renderer.bounds;
				hay = true;
			}
			else
			{
				bounds.Encapsulate(renderer.bounds);
			}
		}
		if (!hay)
		{
			foreach (Collider collider in objeto.GetComponentsInChildren<Collider>(true))
			{
				if (!hay)
				{
					bounds = collider.bounds;
					hay = true;
				}
				else
				{
					bounds.Encapsulate(collider.bounds);
				}
			}
		}
		return bounds;
	}

	static Vector3 Horizontal(Vector3 v, Vector3 siEsCero)
	{
		v.y = 0f;
		if (v.sqrMagnitude < 1e-6f)
		{
			return siEsCero;
		}
		return v.normalized;
	}

	static AudioClip CargarClip(string path)
	{
		AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
		if (clip == null)
		{
			Debug.LogError("TutorialIntroBuilder: no se encontró el sonido " + path);
		}
		return clip;
	}

	static string Nombre(Object objeto)
	{
		return objeto != null ? objeto.name : "(falta)";
	}

	static string RutaDe(Transform t)
	{
		string ruta = t.name;
		while (t.parent != null)
		{
			t = t.parent;
			ruta = t.name + "/" + ruta;
		}
		return ruta;
	}

	static void Marcar(Object objeto)
	{
		EditorUtility.SetDirty(objeto);
		if (PrefabUtility.IsPartOfPrefabInstance(objeto))
		{
			PrefabUtility.RecordPrefabInstancePropertyModifications(objeto);
		}
	}

	static void Reportar(List<string> errores)
	{
		foreach (string error in errores)
		{
			Debug.LogError("TutorialIntroBuilder: " + error);
		}
		Debug.LogError("TutorialIntroBuilder: no se construyó el tutorial (" + errores.Count + " error(es)). La escena no se modificó.");
	}
}
