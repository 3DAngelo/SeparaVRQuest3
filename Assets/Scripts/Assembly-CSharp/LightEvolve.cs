using System.Collections;
using UnityEngine;

public class LightEvolve : IEvolve
{
	public float[] intensities;

	private int current;

	private Light myLight;

	private void Start()
	{
		myLight = GetComponent<Light>();
		myLight.intensity = intensities[0];
	}

	public override void Evolve()
	{
		current++;
		if (current < intensities.Length)
		{
			StartCoroutine(MakeShinner(intensities[current]));
		}
	}

	private IEnumerator MakeShinner(float newShine)
	{
		float elapsed = 0f;
		float oldShininess = myLight.intensity;
		while (elapsed < 1f)
		{
			elapsed += Time.deltaTime * 0.5f; // RECONSTRUCCION-DUDA: could have been written "Time.deltaTime / 2f" (same machine code)
			myLight.intensity = Mathf.Lerp(oldShininess, newShine, elapsed);
			yield return null;
		}
	}
}
