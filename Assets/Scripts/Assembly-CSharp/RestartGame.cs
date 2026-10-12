using UnityEngine;
using UnityEngine.SceneManagement;

public class RestartGame : MonoBehaviour
{
	public float waitingTime = 10f;

	private void Start()
	{
		Invoke("Restart", waitingTime);
	}

	public void Restart()
	{
		SceneManager.LoadScene(0);
	}
}
