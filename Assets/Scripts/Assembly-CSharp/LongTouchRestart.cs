using UnityEngine;

public class LongTouchRestart : MonoBehaviour
{
	private bool touching;

	private float elapsed;

	private void Start()
	{
	}

	private void Update()
	{
		if (Input.GetMouseButton(0))
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
					GetComponent<RestartGame>().Restart();
				}
			}
		}
		else
		{
			touching = false;
		}
	}
}
