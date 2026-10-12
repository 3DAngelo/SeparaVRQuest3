using System.Collections;
using UnityEngine;

public class Autorestart : MonoBehaviour
{
	public float waitingTime = 10f;

	private void Start()
	{
		StartCoroutine(ToIntro());
	}

	private IEnumerator ToIntro()
	{
		yield return new WaitForSeconds(waitingTime);
		GetComponent<RestartGame>().Restart();
	}
}
