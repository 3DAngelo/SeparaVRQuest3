using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

// En modo Floor, hacia dónde mira el casco respecto al XR Origin depende del guardián de Quest, así que el jugador
// podía empezar mirando para cualquier lado. Esto hace lo mismo que la intro (TutorialIntro) en el resto de escenas
// (juego y final), sin tocar los .unity: al cargar cada escena que tenga un XR Origin, y que no sea la intro,
// añade este componente al origen, que gira el origen (solo yaw, alrededor de la cámara) para que la cámara mire
// hacia donde está diseñado el origen en la escena, y lo traslada en horizontal para que la cámara quede donde
// estaba diseñado el origen. No toca la Y. Se repite cuando el sistema recentra el espacio de tracking.
[DisallowMultipleComponent]
public class AlinearJugador : MonoBehaviour
{
	public const float EsperaPoseMaxima = 2f;   // segundos máximos esperando una pose válida de la cámara
	public const int FramesEsperaPose = 3;      // frames mínimos de espera antes de alinear

	private XROrigin origen;
	private Vector3 origenPosicionInicio;
	private Vector3 origenForwardInicio;        // hacia dónde mira el origen tal como está diseñado en la escena (en horizontal)
	private bool alineando;
	private bool iniciado;
	private readonly List<XRInputSubsystem> subsistemasInput = new List<XRInputSubsystem>();

	// ---------------------------------------------------------------- arranque

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Bootstrap()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
		// La primera escena ya está cargada cuando se ejecuta esto, así que se procesa aquí.
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Instalar(SceneManager.GetSceneAt(i));
		}
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		Instalar(scene);
	}

	// Añade el alineador al XR Origin de la escena, salvo en la intro (TutorialIntro se alinea sola).
	private static void Instalar(Scene scene)
	{
		if (!scene.IsValid() || !scene.isLoaded)
		{
			return;
		}
		GameObject[] raices = scene.GetRootGameObjects();
		foreach (GameObject raiz in raices)
		{
			if (raiz.GetComponentsInChildren<TutorialIntro>(true).Length > 0)
			{
				return;
			}
		}
		foreach (GameObject raiz in raices)
		{
			foreach (XROrigin xrOrigin in raiz.GetComponentsInChildren<XROrigin>(true))
			{
				// Si ya lo tiene (escena procesada dos veces) no se duplica.
				if (xrOrigin.GetComponent<AlinearJugador>() == null)
				{
					xrOrigin.gameObject.AddComponent<AlinearJugador>().Iniciar(xrOrigin);
				}
				return;
			}
		}
	}

	// ---------------------------------------------------------------- alineación del jugador

	// Guarda dónde está diseñado el XR Origin, se suscribe al recentrado del sistema y alinea una primera vez.
	private void Iniciar(XROrigin xrOrigin)
	{
		if (iniciado || xrOrigin == null || xrOrigin.Camera == null || !isActiveAndEnabled)
		{
			return;
		}
		iniciado = true;
		origen = xrOrigin;
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

	private void OnDestroy()
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
		if (!alineando && isActiveAndEnabled)
		{
			StartCoroutine(Alinear());
		}
	}

	// Espera una pose válida de la cámara, gira el origen alrededor de la cámara (solo yaw) para que mire hacia donde
	// está diseñado y lo traslada en horizontal para que la cámara quede donde estaba diseñado el origen. No toca la Y.
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
		Vector3 actual = Vector3.ProjectOnPlane(camara.forward, Vector3.up);
		if (deseada.sqrMagnitude < 1e-4f)
		{
			deseada = Vector3.forward;
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
}
