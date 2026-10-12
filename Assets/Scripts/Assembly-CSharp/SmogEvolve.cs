using System.Collections;
using UnityEngine;

public class SmogEvolve : IEvolve
{
	public Material[] SmogMaterials;

	public Material MySmog;

	private int current;

	public override void Evolve()
	{
		current++;
		if (current < SmogMaterials.Length)
		{
			StartCoroutine(MakeShinner(SmogMaterials[current - 1], SmogMaterials[current]));
		}
	}

	private IEnumerator MakeShinner(Material oldMaterial, Material newMaterial)
	{
		float elapsed = 0f;
		while (elapsed < 1f)
		{
			elapsed += Time.deltaTime * 0.5f; // RECONSTRUCCION-DUDA: could have been written "Time.deltaTime / 2f" (same machine code)
			MySmog.Lerp(oldMaterial, newMaterial, elapsed);
			yield return null;
		}
		yield return null;
	}
}
