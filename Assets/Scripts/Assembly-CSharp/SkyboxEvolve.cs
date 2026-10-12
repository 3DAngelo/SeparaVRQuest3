using System.Collections;
using UnityEngine;

public class SkyboxEvolve : IEvolve
{
	public Material skybox;

	public float[] shininess;

	public float rotationSpeed = 0.3f;

	private int current;

	private float rotation;

	private void Start()
	{
		skybox.SetFloat("_Exposure", shininess[0]);
		skybox.SetFloat("_Rotation", rotation);
	}

	private void Update()
	{
		rotation += Time.deltaTime * rotationSpeed;
		if (rotation > 360f)
		{
			rotation -= 360f;
		}
		skybox.SetFloat("_Rotation", rotation);
	}

	public override void Evolve()
	{
		current++;
		if (current < shininess.Length)
		{
			StartCoroutine(MakeShinner(shininess[current]));
		}
	}

	private IEnumerator MakeShinner(float newShine)
	{
		float elapsed = 0f;
		float oldShininess = skybox.GetFloat("_Exposure");
		while (elapsed < 1f)
		{
			elapsed += Time.deltaTime * 0.5f; // RECONSTRUCCION-DUDA: could have been written "Time.deltaTime / 2f" (same machine code)
			skybox.SetFloat("_Exposure", Mathf.Lerp(oldShininess, newShine, elapsed));
			yield return null;
		}
	}
}
