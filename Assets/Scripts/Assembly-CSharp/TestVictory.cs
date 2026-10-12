using UnityEngine;

public class TestVictory : MonoBehaviour
{
	public GameState gameState;

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.M))
		{
			gameState.SetWinState();
		}
	}
}
