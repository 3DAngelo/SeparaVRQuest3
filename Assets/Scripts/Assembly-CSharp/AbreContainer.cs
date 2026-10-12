using UnityEngine;

public class AbreContainer : MonoBehaviour
{
	private bool isQueued;

	public SightSelection.Action opensIn;

	public AudioClip SonidoAbre;

	public AudioClip SonidoCierra;

	public bool isOpen;

	private void Start()
	{
	}

	private void Update()
	{
	}

	private void OnSelected()
	{
		if (opensIn == SightSelection.Action.SELECTED)
		{
			Open();
		}
	}

	private void OnOver()
	{
		if (opensIn == SightSelection.Action.OVER)
		{
			Open();
		}
	}

	private void OnOut()
	{
		if (isOpen)
		{
			if (GetComponent<Animation>().isPlaying && !isQueued)
			{
				isQueued = true;
				GetComponent<Animation>().PlayQueued("Cerrar");
			}
			else
			{
				isQueued = false;
				GetComponent<Animation>().Play("Cerrar");
			}
			GetComponent<AudioSource>().clip = SonidoCierra;
			GetComponent<AudioSource>().Play();
			isOpen = false;
		}
	}

	private void Open()
	{
		if (!isOpen)
		{
			if (GetComponent<Animation>().isPlaying && !isQueued)
			{
				isQueued = true;
				GetComponent<Animation>().PlayQueued("Abrir");
			}
			else
			{
				isQueued = false;
				GetComponent<Animation>().Play("Abrir");
			}
			GetComponent<AudioSource>().clip = SonidoAbre;
			GetComponent<AudioSource>().Play();
			isOpen = true;
		}
	}
}
