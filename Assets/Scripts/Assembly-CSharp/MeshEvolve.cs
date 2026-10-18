using System.Collections.Generic;
using UnityEngine;

public class MeshEvolve : IEvolve
{
	public List<GameObject> meshes;

	private int current;

	// Awake (no Start) para que un Evolve() que llegue antes de Start no se pierda.
	private void Awake()
	{
		current = 0;
		Show();
	}

	public override void Evolve()
	{
		if (current >= meshes.Count - 1)
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
		HashSet<GameObject> visibles = new HashSet<GameObject>();
		for (int i = 0; i <= current && i < meshes.Count; i++)
		{
			if (meshes[i] != null)
			{
				visibles.Add(meshes[i]);
			}
		}
		foreach (GameObject mesh in meshes)
		{
			if (mesh != null)
			{
				mesh.SetActive(visibles.Contains(mesh));
			}
		}
	}
}
