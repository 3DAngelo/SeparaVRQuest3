using System.Collections;
using UnityEngine;

public class VRScreenFade : MonoBehaviour
{
	public float waitTime = 1f;

	public float fadeTime = 2f;

	public Color fadeColor = new Color(0.01f, 0.01f, 0.01f, 1f);

	public Shader fadeShader;

	private Material fadeMaterial;

	private bool isFading;

	private void Awake()
	{
		fadeMaterial = (fadeShader != null) ? new Material(fadeShader) : new Material(Shader.Find("Transparent/Diffuse"));
	}

	private void OnEnable()
	{
		StartCoroutine(FadeIn());
	}

	private void OnDisable()
	{
	}

	private void OnLevelWasLoaded(int level)
	{
		StartCoroutine(FadeIn());
	}

	private void OnDestroy()
	{
		if (fadeMaterial != null)
		{
			Destroy(fadeMaterial);
		}
	}

	public void BeginFadeIn()
	{
		StartCoroutine(FadeIn());
	}

	public void BeginFadeOut()
	{
		StartCoroutine(FadeOut());
	}

	private IEnumerator FadeIn()
	{
		float elapsedTime = 0f;
		fadeColor.a = 1f;
		fadeMaterial.color = fadeColor;
		Color color = fadeColor;
		isFading = true;
		yield return new WaitForSeconds(waitTime);
		// RECONSTRUCCION-DUDA: forma exacta del bucle; el binario incrementa y aplica el color antes de comprobar elapsedTime > fadeTime
		while (true)
		{
			elapsedTime += Time.deltaTime;
			color.a = 1f - Mathf.Clamp01(elapsedTime / fadeTime);
			fadeMaterial.color = color;
			if (elapsedTime > fadeTime)
			{
				break;
			}
			yield return new WaitForEndOfFrame();
		}
		isFading = false;
	}

	private IEnumerator FadeOut()
	{
		float elapsedTime = 0f;
		fadeMaterial.color = fadeColor;
		Color color = fadeColor;
		isFading = true;
		while (elapsedTime < fadeTime)
		{
			elapsedTime += Time.deltaTime;
			color.a = Mathf.Clamp01(elapsedTime / fadeTime);
			fadeMaterial.color = color;
			yield return new WaitForEndOfFrame();
		}
		yield return new WaitForSeconds(waitTime);
		isFading = false;
	}

	private void OnPostRender()
	{
		if (isFading)
		{
			fadeMaterial.SetPass(0);
			GL.PushMatrix();
			GL.LoadOrtho();
			GL.Color(fadeMaterial.color);
			GL.Begin(GL.QUADS);
			GL.Vertex3(0f, 0f, -12f);
			GL.Vertex3(0f, 1f, -12f);
			GL.Vertex3(1f, 1f, -12f);
			GL.Vertex3(1f, 0f, -12f);
			GL.End();
			GL.PopMatrix();
		}
	}
}
