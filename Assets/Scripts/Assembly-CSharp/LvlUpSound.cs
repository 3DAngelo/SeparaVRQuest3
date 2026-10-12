using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class LvlUpSound : IEvolve
{
	public override void Evolve()
	{
		GetComponent<AudioSource>().Play();
	}
}
