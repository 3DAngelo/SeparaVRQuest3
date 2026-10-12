using UnityEngine;

public class MaterialEvolve : IEvolve
{
	public Material[] materials;

	private int current;

	private Renderer rend;

	private void Start()
	{
		rend = GetComponent<Renderer>();
		rend.material = materials[0];
	}

	public override void Evolve()
	{
		current++;
		if (current < materials.Length)
		{
			rend.material = materials[current];
		}
	}
}
