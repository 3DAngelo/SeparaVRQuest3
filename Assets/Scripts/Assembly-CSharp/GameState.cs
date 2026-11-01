using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameState : MonoBehaviour
{
	private bool collecting = true;

	public VRScreenFade screenFade;

	private void Start()
	{
		collecting = true;
	}

	private void Update()
	{
	}

	public void SetCollectState()
	{
		collecting = true;
	}

	public void SetDumpState()
	{
		collecting = false;
	}

	public void SetWinState()
	{
		StartCoroutine(ThxForPlaying());
	}

	private IEnumerator ThxForPlaying()
	{
		// The Win scene has no city, so the fireworks show happens here, before the fade.
		WinFireworks.Play();
		yield return new WaitForSeconds(WinFireworks.ShowSeconds);
		screenFade.BeginFadeOut();
		yield return new WaitForSeconds(3f);
		SceneManager.LoadScene(2);
	}
}
