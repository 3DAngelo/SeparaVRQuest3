using System.Collections;
using UnityEngine;

public class FogEvolve : IEvolve
{
	public float[] densities = new float[5] { 0.09f, 0.07f, 0.05f, 0.03f, 0f };

	private int current;

	private Light light;

	private void Start()
	{
		RenderSettings.fogDensity = densities[0];
	}

	public override void Evolve()
	{
		current++;
		if (current < densities.Length)
		{
			StartCoroutine(MakeClearer(densities[current]));
		}
	}

	private IEnumerator MakeClearer(float newDensity)
	{
		float elapsed = 0f;
		float oldDensity = RenderSettings.fogDensity;
		while (elapsed < 1f)
		{
			elapsed += Time.deltaTime * 0.5f; // RECONSTRUCCION-DUDA: could have been written "Time.deltaTime / 2f" (same machine code)
			RenderSettings.fogDensity = Mathf.Lerp(oldDensity, newDensity, elapsed);
			yield return null;
		}
	}
}
