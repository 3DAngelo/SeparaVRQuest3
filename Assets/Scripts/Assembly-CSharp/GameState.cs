using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(SightSelection))]
public class GameState : MonoBehaviour
{
	private SightSelection selector;

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
		screenFade.BeginFadeOut();
		yield return new WaitForSeconds(3f);
		SceneManager.LoadScene(2);
	}

	private IEnumerator ChangeLayer(string newLayer)
	{
		selector.selectionLayer = "";
		yield return new WaitForSeconds(1f);
		selector.selectionLayer = newLayer;
	}
}
