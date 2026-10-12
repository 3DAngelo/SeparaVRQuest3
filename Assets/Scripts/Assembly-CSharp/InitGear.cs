using UnityEngine;
using UnityEngine.SceneManagement;

public class InitGear : MonoBehaviour
{
	private void Start()
	{
		SceneManager.LoadScene(1);
	}
}
