using UnityEngine;
using UnityEngine.SceneManagement;

public class GameStart : MonoBehaviour
{
	public float waitingTime = 2f;

	public GameObject mainCamera;

	private VRScreenFade screenFade;

	private void Start()
	{
		screenFade = mainCamera.GetComponent<VRScreenFade>();
		screenFade.BeginFadeIn();
	}

	public void StartGame()
	{
		screenFade.BeginFadeOut();
		Invoke("LoadScene", waitingTime);
	}

	public void LoadScene()
	{
		Debug.Log("Loading Scene 1");
		SceneManager.LoadScene(1);
	}
}
