using UnityEngine;
using UnityEngine.InputSystem;

public class TestVictory : MonoBehaviour
{
	public GameState gameState;

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
		if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
		{
			gameState.SetWinState();
		}
	}
}
