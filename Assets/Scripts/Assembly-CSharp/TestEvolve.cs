using UnityEngine;

public class TestEvolve : MonoBehaviour
{
	public GameObject entorno;

	private void Start()
	{
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.KeypadPlus))
		{
			entorno.BroadcastMessage("Evolve", SendMessageOptions.DontRequireReceiver);
		}
	}
}
