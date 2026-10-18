using System;
using System.Collections.Generic;
using UnityEngine;

// Los árboles viven en la instancia del FBX (/EntornoFix/Entorno/Arboles), fuera de 'Entorno'.
// Este componente va bajo Entorno para que GameManager.CheckEvolve lo encuentre con GetComponentsInChildren<IEvolve>().
public class ArbolesEvolve : IEvolve
{
	public string rutaArboles = "/EntornoFix/Entorno/Arboles";
	public string[] secuenciaArbol = { "Arbol0", "Arbol1", "Arbol2", "Arbol2", "Arbol3" };
	public string[] secuenciaWillow = { "Willow_2_C", "Willow_2_B", "Willow_2_B", "Willow_2_A", "Willow_2_A" };

	private readonly List<List<GameObject>> grupos = new List<List<GameObject>>();
	private int current;

	// Awake (no Start) para que un Evolve() que llegue antes de Start no se pierda.
	private void Awake()
	{
		GameObject raiz = GameObject.Find(rutaArboles);
		if (raiz == null)
		{
#if UNITY_EDITOR
			Debug.LogWarning("ArbolesEvolve: no se encuentra " + rutaArboles);
#endif
			return;
		}
		foreach (Transform grupo in raiz.transform)
		{
			List<GameObject> niveles = Buscar(grupo, secuenciaArbol);
			if (niveles == null)
			{
				niveles = Buscar(grupo, secuenciaWillow);
			}
			if (niveles != null)
			{
				grupos.Add(niveles);
			}
#if UNITY_EDITOR
			else
			{
				Debug.LogWarning("ArbolesEvolve: grupo sin secuencia conocida: " + grupo.name);
			}
#endif
		}
		current = 0;
		Show();
	}

	// Devuelve los niveles del grupo en orden de secuencia, o null si falta algún prefijo.
	private static List<GameObject> Buscar(Transform grupo, string[] secuencia)
	{
		if (secuencia == null || secuencia.Length == 0)
		{
			return null;
		}
		List<GameObject> niveles = new List<GameObject>();
		foreach (string prefijo in secuencia)
		{
			GameObject nivel = null;
			foreach (Transform hijo in grupo)
			{
				if (hijo.name.StartsWith(prefijo, StringComparison.Ordinal))
				{
					nivel = hijo.gameObject;
					break;
				}
			}
			if (nivel == null)
			{
				return null;
			}
			niveles.Add(nivel);
		}
		return niveles;
	}

	public override void Evolve()
	{
		int max = 0;
		foreach (List<GameObject> niveles in grupos)
		{
			max = Mathf.Max(max, niveles.Count);
		}
		if (current >= max - 1)
		{
			return;
		}
		current++;
		Show();
	}

	// Las mallas se suman: quedan encendidas todas hasta el nivel actual, las siguientes apagadas.
	// Tolera la misma malla repetida en la lista.
	private void Show()
	{
		foreach (List<GameObject> niveles in grupos)
		{
			HashSet<GameObject> visibles = new HashSet<GameObject>();
			for (int i = 0; i <= current && i < niveles.Count; i++)
			{
				visibles.Add(niveles[i]);
			}
			foreach (GameObject mesh in niveles)
			{
				mesh.SetActive(visibles.Contains(mesh));
			}
		}
	}
}
