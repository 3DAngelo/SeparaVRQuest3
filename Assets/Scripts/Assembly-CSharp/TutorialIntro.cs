using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Mini tutorial de la escena Intro: enseña a apuntar, agarrar y tirar un residuo en el contenedor correcto,
// y después carga el juego (escena 1) con el mismo fundido que el botón de inicio.
// No depende del GameManager (que solo existe en NuevoJuego). Lo arma el menú "SeparaVR/Construir tutorial Intro".
public class TutorialIntro : MonoBehaviour
{
	// ---- pasos ----
	public const int PasoBienvenida = 0;
	public const int PasoApuntar = 1;
	public const int PasoAgarrar = 2;
	public const int PasoSoltar = 3;
	public const int PasoPanial = 4;
	public const int PasoListo = 5;

	// ---- tiempos (segundos) ----
	public const float DuracionBienvenida = 3f;
	public const float DuracionListo = 3f;
	public const float EsperaVolverIncorrecto = 1.5f;
	public const float EsperaVolverSoltado = 2f;
	public const float DuracionMensajeError = 2.5f;
	public const float TiempoSinProgresoParaAyuda = 15f;

	// ---- vuelta al punto de inicio ----
	// Si cae por debajo de esta altura o se aleja más que esto (horizontal) del inicio, vuelve enseguida.
	public const float AlturaMinima = -1f;
	public const float DistanciaMaxima = 4f;
	// Más lejos que esto del inicio y sin agarrar: se lo considera "caído" y vuelve después de EsperaVolverSoltado.
	public const float DistanciaFueraDeLugar = 0.3f;

	// ---- animación de arrojar (como GameManager.ArrojarResiduos: dumpTime 1, curva en lomo de altura 0.7) ----
	public const float DuracionArrojar = 1f;
	public const float AlturaArco = 0.7f;
	public const float EsperaCerrarTapa = 0.4f;
	private static readonly Vector3 GiroArrojar = new Vector3(200f, 400f, 100f);

	// ---- ayuda (flecha + resaltado) ----
	public const float FlechaAltura = 0.2f;
	public const float FlechaRebote = 0.08f;
	public const float FlechaReboteHz = 1.5f;
	public static readonly Color ColorAyuda = new Color(1f, 0.85f, 0.1f, 1f);

	// ---- logo: más chico después de la bienvenida ----
	public const float EscalaLogoReducido = 0.6f;
	public const float VelocidadLogo = 4f;

	// ---- alineación del jugador al inicio (en modo Floor el yaw del casco depende del guardián de Quest) ----
	public const float EsperaPoseMaxima = 2f;
	public const int FramesEsperaPose = 3;

	[Header("Objetos de práctica")]
	public CustomGrabInteractable botella;
	public CustomGrabInteractable panial;

	[Header("Contenedores")]
	public Container naranja;
	public Container verde;

	[Header("Interfaz")]
	public TMP_Text texto;
	public Transform logo;
	public Transform flecha;

	[Header("Saltar tutorial")]
	public XRBaseInteractable botonSaltar;
	public GameStart gameStart;

	[Header("Sonidos")]
	public AudioSource audioSource;
	public AudioClip sonidoPaso;
	public AudioClip sonidoFinal;
	public AudioClip sonidoError;

	// ---- estado ----
	private int paso = PasoBienvenida;
	private int pasoPendiente = PasoBienvenida;
	private bool terminado;
	private bool cargando;
	private bool usandoControl;
	private float ultimoProgreso;

	private string mensajeError;
	private float mensajeErrorHasta;
	private string textoMostrado;

	private Vector3 logoEscalaOriginal;
	private Vector3 logoPosicionOriginal;
	private Vector3 logoEscalaObjetivo;
	private Vector3 logoPosicionObjetivo;

	private HighlightPulse pulsoAyuda;
	private GameObject objetivoAyuda;
	private MeshRenderer[] renderersAyuda;

	// Estado de cada objeto de práctica (botella y pañal).
	private class Practica
	{
		public CustomGrabInteractable objeto;
		public Rigidbody body;
		public Vector3 inicioPosicion;
		public Quaternion inicioRotacion;
		public float volverEn = -1f;
		public bool arrojado;
	}

	private readonly List<Practica> practicas = new List<Practica>();

	private XROrigin origen;
	private Vector3 origenPosicionInicio;
	private Vector3 origenForwardInicio;
	private bool alineando;
	private readonly List<XRInputSubsystem> subsistemasInput = new List<XRInputSubsystem>();

	private void Awake()
	{
		pulsoAyuda = new HighlightPulse("Tutorial ayuda", ColorAyuda);
		if (logo != null)
		{
			logoEscalaOriginal = logo.localScale;
			logoPosicionOriginal = logo.localPosition;
			logoEscalaObjetivo = logoEscalaOriginal;
			logoPosicionObjetivo = logoPosicionOriginal;
		}
		RegistrarPractica(botella);
		RegistrarPractica(panial);
	}

	private void RegistrarPractica(CustomGrabInteractable objeto)
	{
		if (objeto == null)
		{
			Debug.LogError("TutorialIntro: falta asignar un objeto de práctica (botella / pañal).");
			return;
		}
		Practica practica = new Practica();
		practica.objeto = objeto;
		practica.body = objeto.GetComponent<Rigidbody>();
		practica.inicioPosicion = objeto.transform.position;
		practica.inicioRotacion = objeto.transform.rotation;
		practicas.Add(practica);
	}

	private void OnEnable()
	{
		CustomGrabInteractable.Soltado += OnSoltado;
		if (botella != null)
		{
			botella.hoverEntered.AddListener(OnBotellaApuntada);
			botella.selectEntered.AddListener(OnAgarrado);
		}
		if (panial != null)
		{
			panial.selectEntered.AddListener(OnAgarrado);
		}
		if (botonSaltar != null)
		{
			botonSaltar.selectEntered.AddListener(OnSaltar);
		}
	}

	private void OnDisable()
	{
		CustomGrabInteractable.Soltado -= OnSoltado;
		if (botella != null)
		{
			botella.hoverEntered.RemoveListener(OnBotellaApuntada);
			botella.selectEntered.RemoveListener(OnAgarrado);
		}
		if (panial != null)
		{
			panial.selectEntered.RemoveListener(OnAgarrado);
		}
		if (botonSaltar != null)
		{
			botonSaltar.selectEntered.RemoveListener(OnSaltar);
		}
	}

	private void Start()
	{
		if (panial != null)
		{
			panial.gameObject.SetActive(false);
		}
		if (flecha != null)
		{
			flecha.gameObject.SetActive(false);
		}
		ultimoProgreso = Time.time;
		ActualizarModo();
		ActualizarTexto();
		StartCoroutine(Bienvenida());
		IniciarAlineacion();
	}

	private void OnDestroy()
	{
		DesuscribirRecentrado();
		if (pulsoAyuda != null)
		{
			pulsoAyuda.Dispose();
			pulsoAyuda = null;
		}
	}

	// ---------------------------------------------------------------- alineación del jugador

	// Guarda dónde está diseñado el XR Origin, se suscribe al recentrado del sistema y alinea una primera vez.
	private void IniciarAlineacion()
	{
		origen = FindFirstObjectByType<XROrigin>();
		if (origen == null || origen.Camera == null)
		{
			return;
		}
		origenPosicionInicio = origen.transform.position;
		origenForwardInicio = Vector3.ProjectOnPlane(origen.transform.forward, Vector3.up);
		SubsystemManager.GetSubsystems(subsistemasInput);
		foreach (XRInputSubsystem subsistema in subsistemasInput)
		{
			subsistema.trackingOriginUpdated -= OnOrigenRecentrado;
			subsistema.trackingOriginUpdated += OnOrigenRecentrado;
		}
		StartCoroutine(Alinear());
	}

	private void DesuscribirRecentrado()
	{
		foreach (XRInputSubsystem subsistema in subsistemasInput)
		{
			if (subsistema != null)
			{
				subsistema.trackingOriginUpdated -= OnOrigenRecentrado;
			}
		}
		subsistemasInput.Clear();
	}

	private void OnOrigenRecentrado(XRInputSubsystem subsistema)
	{
		if (!terminado && !alineando)
		{
			StartCoroutine(Alinear());
		}
	}

	// Espera una pose válida de la cámara, gira el origen alrededor de la cámara (solo yaw) para que mire hacia los
	// contenedores y lo traslada en horizontal para que la cámara quede donde estaba diseñado el origen. No toca la Y.
	private IEnumerator Alinear()
	{
		alineando = true;
		Transform camara = origen.Camera.transform;
		float limite = Time.unscaledTime + EsperaPoseMaxima;
		for (int frames = 0; frames < FramesEsperaPose || (camara.localPosition == Vector3.zero && camara.localRotation == Quaternion.identity && Time.unscaledTime < limite); frames++)
		{
			yield return null;
		}

		Vector3 deseada = origenForwardInicio;
		if (naranja != null && verde != null)
		{
			deseada = (naranja.transform.position + verde.transform.position) * 0.5f - origenPosicionInicio;
		}
		else if (naranja != null || verde != null)
		{
			deseada = (naranja != null ? naranja : verde).transform.position - origenPosicionInicio;
		}
		deseada = Vector3.ProjectOnPlane(deseada, Vector3.up);
		Vector3 actual = Vector3.ProjectOnPlane(camara.forward, Vector3.up);
		if (deseada.sqrMagnitude < 1e-4f)
		{
			deseada = origenForwardInicio.sqrMagnitude > 1e-4f ? origenForwardInicio : Vector3.forward;
		}
		if (actual.sqrMagnitude > 1e-4f)
		{
			// Gira solo sobre el eje Y alrededor de la cámara (la cámara no se mueve).
			origen.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(actual, deseada, Vector3.up));
		}
		Vector3 desplazamiento = origenPosicionInicio - camara.position;
		desplazamiento.y = 0f;
		origen.transform.position += desplazamiento;
		alineando = false;
	}

	// ---------------------------------------------------------------- pasos

	private IEnumerator Bienvenida()
	{
		yield return new WaitForSeconds(DuracionBienvenida);
		// Si durante la bienvenida ya apuntó / agarró / tiró la botella, se salta directo a ese paso.
		int siguiente = Mathf.Max(PasoApuntar, pasoPendiente);
		if (siguiente == PasoApuntar && botella != null && botella.isHovered)
		{
			siguiente = PasoAgarrar;
		}
		if (siguiente <= PasoAgarrar && botella != null && botella.isSelected)
		{
			siguiente = PasoSoltar;
		}
		paso = PasoApuntar;   // sale de la bienvenida (AvanzarA solo avanza)
		ultimoProgreso = Time.time;
		ActualizarTexto();
		AvanzarA(siguiente, siguiente > PasoApuntar);
		ReducirLogo();
	}

	// Avanza al paso indicado (nunca retrocede). Durante la bienvenida solo lo recuerda para después.
	private void AvanzarA(int nuevoPaso, bool conSonido)
	{
		if (terminado)
		{
			return;
		}
		if (paso == PasoBienvenida)
		{
			pasoPendiente = Mathf.Max(pasoPendiente, nuevoPaso);
			return;
		}
		if (nuevoPaso <= paso)
		{
			return;
		}
		paso = nuevoPaso;
		ultimoProgreso = Time.time;
		mensajeError = null;
		OcultarAyuda();
		if (conSonido)
		{
			Sonar(sonidoPaso);
		}
		if (paso == PasoPanial)
		{
			MostrarPanial();
		}
		if (paso == PasoListo)
		{
			StartCoroutine(Final());
		}
		ActualizarTexto();
		#if UNITY_EDITOR
		Debug.Log("TutorialIntro: paso " + paso);
		#endif
	}

	private void MostrarPanial()
	{
		Practica practica = BuscarPractica(panial);
		if (practica == null)
		{
			return;
		}
		panial.gameObject.SetActive(true);
		Reposicionar(practica);
	}

	private IEnumerator Final()
	{
		Sonar(sonidoFinal);
		yield return new WaitForSeconds(DuracionListo);
		Cargar();
	}

	private void OnSaltar(SelectEnterEventArgs args)
	{
		if (cargando)
		{
			return;
		}
		#if UNITY_EDITOR
		Debug.Log("TutorialIntro: tutorial salteado");
		#endif
		Cargar();
	}

	// Carga el juego con el fundido de ScreenFader. Nunca carga dos veces: flag propio + GameStart.started +
	// ScreenFader.IsLoading (además ScreenFader ignora pedidos mientras ya está cargando).
	private void Cargar()
	{
		if (cargando)
		{
			return;
		}
		cargando = true;
		terminado = true;
		OcultarAyuda();
		StopAllCoroutines();
		if (ScreenFader.Instance.IsLoading)
		{
			return;   // el botón ya llamó a GameStart.StartGame (llamada persistente que corre antes que esta)
		}
		if (gameStart != null)
		{
			gameStart.LoadScene();
		}
		else
		{
			ScreenFader.Instance.FadeOutAndLoad(GameStart.GameSceneBuildIndex, ScreenFader.DefaultFadeOutSeconds, ScreenFader.DefaultFadeInSeconds);
		}
	}

	// ---------------------------------------------------------------- eventos de interacción

	private void OnBotellaApuntada(HoverEnterEventArgs args)
	{
		if (paso <= PasoApuntar)
		{
			AvanzarA(PasoAgarrar, true);
		}
	}

	private void OnAgarrado(SelectEnterEventArgs args)
	{
		CustomGrabInteractable objeto = args.interactableObject as CustomGrabInteractable;
		Practica practica = BuscarPractica(objeto);
		if (practica != null)
		{
			practica.volverEn = -1f;
		}
		if (objeto == botella && paso <= PasoAgarrar)
		{
			AvanzarA(PasoSoltar, true);
		}
	}

	private void OnSoltado(CustomGrabInteractable objeto, Container contenedor, bool correcto)
	{
		Practica practica = BuscarPractica(objeto);
		if (practica == null || terminado)
		{
			return;
		}
		if (contenedor == null)
		{
			// Soltado sin apuntar a ningún contenedor: vuelve al pedestal en un rato.
			practica.volverEn = Time.time + EsperaVolverSoltado;
			return;
		}
		if (!correcto)
		{
			Sonar(sonidoError);
			mensajeError = objeto == panial
				? "El pañal no se recicla: va al verde"
				: "Ese no es: las botellas van al naranja (reciclables)";
			mensajeErrorHasta = Time.time + DuracionMensajeError;
			ActualizarTexto();
			practica.volverEn = Time.time + EsperaVolverIncorrecto;
			return;
		}
		practica.arrojado = true;
		practica.volverEn = -1f;
		StartCoroutine(Arrojar(practica, contenedor));
		if (objeto == botella)
		{
			AvanzarA(PasoPanial, true);
		}
		else if (objeto == panial)
		{
			AvanzarA(PasoListo, true);
		}
	}

	// Igual que GameManager.ArrojarResiduos: abre la tapa, el objeto vuela en arco hasta arrojarAqui, desaparece y
	// la tapa se cierra.
	private IEnumerator Arrojar(Practica practica, Container contenedor)
	{
		// Un frame: XRI termina de soltar el objeto (Detach aplica la velocidad de lanzamiento en la fase Late);
		// si ya fuera cinemático avisaría "Cannot throw a kinematic Rigidbody".
		yield return null;
		CustomGrabInteractable objeto = practica.objeto;
		if (objeto == null)
		{
			yield break;
		}
		if (practica.body != null)
		{
			practica.body.isKinematic = true;
		}
		foreach (Collider objetoCollider in objeto.GetComponentsInChildren<Collider>())
		{
			objetoCollider.enabled = false;
		}
		AbreContainer tapa = contenedor.GetComponent<AbreContainer>();
		if (tapa != null)
		{
			tapa.Open();
		}
		Transform destino = contenedor.arrojarAqui != null ? contenedor.arrojarAqui : contenedor.transform;
		Vector3 inicio = objeto.transform.position;
		float t = 0f;
		while (t < 1f)
		{
			t = Mathf.Min(1f, t + Time.deltaTime / DuracionArrojar);
			objeto.transform.position = Vector3.Lerp(inicio, destino.position, t) + Vector3.up * (Mathf.Sin(Mathf.PI * t) * AlturaArco);
			objeto.transform.Rotate(GiroArrojar * Time.deltaTime);
			yield return null;
		}
		// Solo se llega aquí con el contenedor correcto: fuegos artificiales de su color al caer el objeto.
		FeedbackFX.PlayFireworks(contenedor);
		objeto.gameObject.SetActive(false);
		yield return new WaitForSeconds(EsperaCerrarTapa);
		if (tapa != null)
		{
			tapa.Close();
		}
	}

	// ---------------------------------------------------------------- cada frame

	private void Update()
	{
		if (terminado)
		{
			return;
		}
		ActualizarModo();
		if (paso == PasoApuntar && botella != null && botella.isHovered)
		{
			AvanzarA(PasoAgarrar, true);
		}
		if (mensajeError != null && Time.time >= mensajeErrorHasta)
		{
			mensajeError = null;
		}
		ActualizarTexto();
		foreach (Practica practica in practicas)
		{
			ControlarPosicion(practica);
		}
		ActualizarAyuda();
		ActualizarLogo();
	}

	private void LateUpdate()
	{
		// El contorno de HighlightPulse hay que dibujarlo cada frame.
		if (pulsoAyuda != null && pulsoAyuda.IsActive && objetivoAyuda != null && objetivoAyuda.activeInHierarchy)
		{
			pulsoAyuda.DrawOutline(renderersAyuda, objetivoAyuda.layer);
		}
	}

	// Mano o control, según XRInputModalityManager. None (por ejemplo mientras se busca el control) mantiene el último.
	private void ActualizarModo()
	{
		XRInputModalityManager.InputMode modo = XRInputModalityManager.currentInputMode.Value;
		if (modo == XRInputModalityManager.InputMode.MotionController)
		{
			usandoControl = true;
		}
		else if (modo == XRInputModalityManager.InputMode.TrackedHand)
		{
			usandoControl = false;
		}
	}

	private string TextoDelPaso()
	{
		switch (paso)
		{
			case PasoBienvenida:
				return "¡Bienvenido a Separá!";
			case PasoApuntar:
				return usandoControl ? "Apuntá a la botella con el control" : "Apuntá a la botella con la mano";
			case PasoAgarrar:
				return usandoControl ? "Apretá el grip para agarrarla" : "Cerrá la mano para agarrarla";
			case PasoSoltar:
				return usandoControl
					? "Girá la mano hacia el contenedor naranja y soltá el grip"
					: "Girá la mano hacia el contenedor naranja y abrila para soltar";
			case PasoPanial:
				return "Este no se recicla: va al verde. Agarralo y tiralo en el contenedor verde.";
			default:
				return "¡Listo! Separá todos los residuos para limpiar la ciudad";
		}
	}

	private void ActualizarTexto()
	{
		if (texto == null)
		{
			return;
		}
		string nuevo = mensajeError != null ? "<color=#FF7060>" + mensajeError + "</color>" : TextoDelPaso();
		if (nuevo != textoMostrado)
		{
			textoMostrado = nuevo;
			texto.text = nuevo;
		}
	}

	private void Sonar(AudioClip clip)
	{
		if (audioSource != null && clip != null)
		{
			audioSource.PlayOneShot(clip);
		}
	}

	// ---------------------------------------------------------------- vuelta al punto de inicio

	private Practica BuscarPractica(CustomGrabInteractable objeto)
	{
		if (objeto == null)
		{
			return null;
		}
		foreach (Practica practica in practicas)
		{
			if (practica.objeto == objeto)
			{
				return practica;
			}
		}
		return null;
	}

	private void ControlarPosicion(Practica practica)
	{
		CustomGrabInteractable objeto = practica.objeto;
		if (objeto == null || practica.arrojado || !objeto.gameObject.activeInHierarchy)
		{
			return;
		}
		if (objeto.isSelected)
		{
			practica.volverEn = -1f;
			return;
		}
		Vector3 posicion = objeto.transform.position;
		Vector3 desplazamiento = posicion - practica.inicioPosicion;
		desplazamiento.y = 0f;
		if (posicion.y < AlturaMinima || desplazamiento.magnitude > DistanciaMaxima)
		{
			Reposicionar(practica);
			return;
		}
		if (practica.volverEn < 0f && Vector3.Distance(posicion, practica.inicioPosicion) > DistanciaFueraDeLugar)
		{
			// Se cayó del pedestal sin que nadie lo soltara (empujado con la mano, por ejemplo).
			practica.volverEn = Time.time + EsperaVolverSoltado;
		}
		if (practica.volverEn >= 0f && Time.time >= practica.volverEn)
		{
			Reposicionar(practica);
		}
	}

	private void Reposicionar(Practica practica)
	{
		practica.volverEn = -1f;
		if (practica.objeto.isSelected)
		{
			return;
		}
		Rigidbody body = practica.body;
		if (body != null)
		{
			body.isKinematic = false;
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			body.position = practica.inicioPosicion;
			body.rotation = practica.inicioRotacion;
		}
		practica.objeto.transform.SetPositionAndRotation(practica.inicioPosicion, practica.inicioRotacion);
	}

	// ---------------------------------------------------------------- ayuda si se traba

	private void ActualizarAyuda()
	{
		GameObject objetivo = null;
		if (paso >= PasoApuntar && paso <= PasoPanial && Time.time - ultimoProgreso >= TiempoSinProgresoParaAyuda)
		{
			CustomGrabInteractable objeto = paso == PasoPanial ? panial : botella;
			Container contenedor = paso == PasoPanial ? verde : naranja;
			if (objeto != null && objeto.gameObject.activeInHierarchy && !BuscarPracticaArrojada(objeto))
			{
				if (objeto.isSelected)
				{
					objetivo = contenedor != null ? contenedor.gameObject : null;
				}
				else
				{
					objetivo = objeto.gameObject;
				}
			}
		}
		if (objetivo != objetivoAyuda)
		{
			objetivoAyuda = objetivo;
			if (objetivo == null)
			{
				OcultarAyuda();
				return;
			}
			renderersAyuda = objetivo.GetComponentsInChildren<MeshRenderer>();
			pulsoAyuda.Begin();
			if (flecha != null)
			{
				flecha.gameObject.SetActive(true);
			}
		}
		if (objetivo != null)
		{
			MoverFlecha(objetivo);
		}
	}

	private bool BuscarPracticaArrojada(CustomGrabInteractable objeto)
	{
		Practica practica = BuscarPractica(objeto);
		return practica != null && practica.arrojado;
	}

	private void OcultarAyuda()
	{
		objetivoAyuda = null;
		renderersAyuda = null;
		if (pulsoAyuda != null)
		{
			pulsoAyuda.End();
		}
		if (flecha != null)
		{
			flecha.gameObject.SetActive(false);
		}
	}

	// Flecha rebotando arriba del objetivo, girada hacia la cámara (solo en horizontal).
	private void MoverFlecha(GameObject objetivo)
	{
		if (flecha == null)
		{
			return;
		}
		bool hayBounds = false;
		Bounds bounds = new Bounds(objetivo.transform.position, Vector3.zero);
		if (renderersAyuda != null)
		{
			foreach (MeshRenderer meshRenderer in renderersAyuda)
			{
				if (meshRenderer == null || !meshRenderer.enabled)
				{
					continue;
				}
				if (!hayBounds)
				{
					bounds = meshRenderer.bounds;
					hayBounds = true;
				}
				else
				{
					bounds.Encapsulate(meshRenderer.bounds);
				}
			}
		}
		float rebote = Mathf.Abs(Mathf.Sin(Time.time * FlechaReboteHz * Mathf.PI)) * FlechaRebote;
		flecha.position = new Vector3(bounds.center.x, bounds.max.y + FlechaAltura + rebote, bounds.center.z);
		Camera camara = Camera.main;
		if (camara != null)
		{
			Vector3 mirada = flecha.position - camara.transform.position;
			mirada.y = 0f;
			if (mirada.sqrMagnitude > 1e-4f)
			{
				flecha.rotation = Quaternion.LookRotation(mirada, Vector3.up);
			}
		}
	}

	// ---------------------------------------------------------------- logo

	// Después de la bienvenida el logo se achica, manteniendo su borde de arriba en el mismo lugar.
	private void ReducirLogo()
	{
		if (logo == null)
		{
			return;
		}
		logoEscalaObjetivo = logoEscalaOriginal * EscalaLogoReducido;
		logoPosicionObjetivo = logoPosicionOriginal;
		SpriteRenderer sprite = logo.GetComponent<SpriteRenderer>();
		if (sprite != null && sprite.sprite != null)
		{
			float mitadAlto = sprite.sprite.bounds.extents.y * logoEscalaOriginal.y;
			logoPosicionObjetivo.y += mitadAlto * (1f - EscalaLogoReducido);
		}
	}

	private void ActualizarLogo()
	{
		if (logo == null)
		{
			return;
		}
		float k = 1f - Mathf.Exp(-VelocidadLogo * Time.deltaTime);
		logo.localScale = Vector3.Lerp(logo.localScale, logoEscalaObjetivo, k);
		logo.localPosition = Vector3.Lerp(logo.localPosition, logoPosicionObjetivo, k);
	}
}
