using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class LongTouchRestart : MonoBehaviour
{
	private bool touching;

	private float elapsed;

	// Debug only: disabled outside the editor.
	private void Awake()
	{
		if (!Application.isEditor)
		{
			enabled = false;
		}
	}

	private void Update()
	{
		if (Pointer.current != null && Pointer.current.press.isPressed)
		{
			if (!touching)
			{
				touching = true;
				elapsed = 0f;
			}
			else
			{
				elapsed += Time.deltaTime;
				if (elapsed >= 1.5f)
				{
					// Loads the intro directly so this no longer needs a RestartGame (whose Start restarts the game on a timer).
					SceneManager.LoadScene(0);
				}
			}
		}
		else
		{
			touching = false;
		}
	}
}
