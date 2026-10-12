using System.Collections.Generic;
using UnityEngine;

public class MeshEvolve : IEvolve
{
	public List<GameObject> meshes;

	private int current;

	private void Start()
	{
		foreach (GameObject mesh in meshes)
		{
			mesh.SetActive(false);
		}
		meshes[0].SetActive(true);
		current = 0;
	}

	public override void Evolve()
	{
		if (current == meshes.Count - 1)
		{
			return;
		}
		meshes[current].SetActive(false);
		current++;
		meshes[current].SetActive(true);
	}
}
