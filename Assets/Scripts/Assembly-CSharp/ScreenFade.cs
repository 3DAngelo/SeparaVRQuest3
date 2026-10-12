using System.Collections;
using UnityEngine;

public class ScreenFade : MonoBehaviour
{
	public float fadeTime = 1f;

	public float waitTime = 1f;

	private void Start()
	{
		StartCoroutine(BeginFadeIn());
	}

	public IEnumerator BeginFadeIn()
	{
		yield return new WaitForSeconds(fadeTime);
		GetComponent<MeshRenderer>().enabled = false;
		yield return new WaitForSeconds(waitTime);
	}

	public IEnumerator BeginFadeOut()
	{
		yield return new WaitForSeconds(fadeTime);
		GetComponent<MeshRenderer>().enabled = true;
		yield return new WaitForSeconds(waitTime);
	}
}
