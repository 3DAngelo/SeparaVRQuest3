using UnityEngine;
using UnityEngine.InputSystem;

public class TestEvolve : MonoBehaviour
{
	public GameObject entorno;

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
		if (Keyboard.current != null && Keyboard.current.numpadPlusKey.wasPressedThisFrame)
		{
			entorno.BroadcastMessage("Evolve", SendMessageOptions.DontRequireReceiver);
		}
	}
}
