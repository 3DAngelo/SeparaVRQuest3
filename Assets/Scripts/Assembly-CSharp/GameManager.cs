using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
	private static GameManager _instance;

	public GameState gameState;

	public float pickTime = 1f;

	public float dumpTime = 3f;

    public AnimationCurve curvita;

	public Transform enviroment;

	private Residuo residuoLevantado;

	public AudioSource Aplausos;

	private int score;

	private int totalResiduos;

	public Slider progressBar;

	public int[] evolveScores = new int[3] { 2, 4, 6 };

	public Text descResiduo;

	public string msjLanzarResiduo = "Elegí el contenedor correcto para separarlo.";

	public string msjSeleccionarResiduo = "Separá los residuos enviándolos al contenedor correcto.\n\nMirá un objeto para levantarlo.";

	public string msjWin = "Felicitaciones !!! Separaste correctamente los residuos. Tu ciudad está más limpia.";

	public static GameManager Instance
	{
		get
		{
			if (_instance == null)
			{
				Debug.LogError("Cuidado, intentas usar la instancia del GameManager sin haber sido previamente seteada");
			}
			return _instance;
		}
	}

	// true si hay un GameManager vivo en escena. A diferencia de Instance, no loguea error cuando no hay
	// (por ejemplo en la escena Intro / tutorial, que no tiene GameManager).
	public static bool HasInstance
	{
		get
		{
			return _instance != null;
		}
	}

	private void Start()
	{
		if (_instance != null)
		{
			Debug.LogError("Cuidado, hay mas de un GameManager en escena, se usara el ultimo creado");
		}
		_instance = this;
		if (!progressBar)
		{
			progressBar = GameObject.Find("Slider").GetComponent<Slider>();
		}
		descResiduo.text = msjSeleccionarResiduo;
		InitializeResiduos();
	}

	private void InitializeResiduos()
	{
		GameObject[] residuos = GameObject.FindGameObjectsWithTag("residuo");
		List<GameObject> spots = new List<GameObject>(GameObject.FindGameObjectsWithTag("spot"));
		foreach (GameObject residuo in residuos)
		{
			if ((bool)residuo)
			{
				// RECONSTRUCCION-DUDA: el binario sale del bucle si no quedan spots o si el spot elegido es nulo; la forma original de esas condiciones no es recuperable
				if (spots.Count == 0)
				{
					break;
				}
				GameObject spot = spots[Random.Range(0, spots.Count)];
				if (!spot)
				{
					break;
				}
				residuo.transform.position = spot.transform.position;
				spots.Remove(spot);
				Destroy(spot);
			}
			else
			{
				descResiduo.text = "ojo! no encontro los residuos";
				break;
			}
		}
		foreach (GameObject sobrante in spots)
		{
			if ((bool)sobrante)
			{
				Destroy(sobrante);
			}
		}
		totalResiduos = residuos.Length;
		progressBar.maxValue = totalResiduos;
	}

	public IEnumerator LevantarResiduos(Transform residuo)
	{
		gameState.SetDumpState();
		residuoLevantado = residuo.GetComponent<Residuo>();
		descResiduo.text = residuoLevantado.descripcion + "\n\n" + msjLanzarResiduo;
		float elapsed = 0f;
		while (elapsed < pickTime)
		{
			elapsed += Time.fixedDeltaTime;
			elapsed = Mathf.Clamp(elapsed, 0f, pickTime);
			yield return new WaitForFixedUpdate();
		}
		StartCoroutine(SostenerResiduo());
	}

	public IEnumerator SostenerResiduo()
	{
		while (residuoLevantado != null)
		{
			yield return new WaitForFixedUpdate();
		}
	}

	public void IniciarArrojarResiduos(Container container, Residuo residuo)
	{
		StartCoroutine(ArrojarResiduos(container, residuo));
	}

	public IEnumerator ArrojarResiduos(Container container, Residuo _residuo)
	{
		gameState.SetCollectState();
		residuoLevantado = _residuo;
		score++;
		// RECONSTRUCCION-DUDA: llamada virtual a Slider con un float (slot 47 de la vtable); se asume el setter de value
		progressBar.value = score;
		Transform residuo = residuoLevantado.transform;
		residuo.SetParent(null, true);
		residuoLevantado = null;
		// The flight is driven by hand: no physics, so neither the release (throw) velocity nor the container's colliders
		// can push it again once it arrives.
		Rigidbody body = residuo.GetComponent<Rigidbody>();
		body.isKinematic = true;
		foreach (Collider residuoCollider in residuo.GetComponentsInChildren<Collider>())
		{
			residuoCollider.enabled = false;
		}
		// The lid (if the container has one) opens while the object flies in, and closes once it has disappeared.
		AbreContainer lid = container.GetComponent<AbreContainer>();
		if (lid != null)
		{
			lid.Open();
		}
		Vector3 startPosition = residuo.position;
		Vector3 endPosition = container.RandomArrojarPoint().position;
		float elapsed = 0f;
		while (elapsed < dumpTime)
		{
			elapsed += Time.fixedDeltaTime;
			elapsed = Mathf.Clamp(elapsed, 0f, dumpTime);
			residuo.position = Vector3.Lerp(startPosition, endPosition, elapsed / dumpTime);
			residuo.position += new Vector3(0f, curvita.Evaluate(elapsed / dumpTime) * 0.7f, 0f);
			residuo.Rotate(Time.fixedDeltaTime * 200f, Time.fixedDeltaTime * 400f, Time.fixedDeltaTime * 100f);
			yield return new WaitForFixedUpdate();
		}
		// Right container (only right releases get here): celebrate with fireworks in its colour as the object lands.
		FeedbackFX.PlayFireworks(container);
		CheckEvolve();
		// Then it just drops into the container (straight down, no collisions) and disappears.
		body.isKinematic = false;
		body.linearVelocity = Vector3.zero;
		body.angularVelocity = Vector3.zero;
		yield return new WaitForSeconds(0.5f);
		Destroy(residuo.gameObject);
		if (lid != null)
		{
			lid.Close();
		}
		if (score == totalResiduos)
		{
			descResiduo.text = msjWin;
			PlayApplause();
			gameState.SetWinState();
		}
	}

	// The scene's Aplausos field points to the green container's lid AudioSource (ContainerAbre clip), so the applause
	// clip is loaded from Assets/Resources/Aplausos and played on its own 2D source, kept across the load of the Win scene.
	private void PlayApplause()
	{
		AudioClip clip = Resources.Load<AudioClip>("Aplausos");
		if (clip == null)
		{
			#if UNITY_EDITOR
			Debug.LogWarning("GameManager: no applause clip at Assets/Resources/Aplausos; the win applause is not played.");
			#endif
			return;
		}
		GameObject go = new GameObject("Aplausos");
		DontDestroyOnLoad(go);
		AudioSource source = go.AddComponent<AudioSource>();
		source.clip = clip;
		source.spatialBlend = 0f;
		source.Play();
		Destroy(go, clip.length + 0.1f);
	}

	private void CheckEvolve()
	{
		foreach (int evolveScore in evolveScores)
		{
			if (score == evolveScore)
			{
				IEvolve[] evolves = enviroment.GetComponentsInChildren<IEvolve>();
				foreach (IEvolve evolve in evolves)
				{
					evolve.Evolve();
				}
			}
		}
	}

	public void OnObjectGrabbed(GameObject grabbedObject)
	{
		#if UNITY_EDITOR
		Debug.Log("Se agarró el objeto: " + grabbedObject.name);
		#endif
		gameState.SetDumpState();
		if (grabbedObject.GetComponent<Residuo>() != null)
		{
			StartCoroutine(LevantarResiduos(grabbedObject.transform));
		}
	}

	public void OnContainerHovered(Container container)
	{
		#if UNITY_EDITOR
		Debug.Log("Contenedor detectado: " + container.descripcion);
		#endif
	}
}
