using System.Collections.Generic;
using UnityEngine.Audio;

public class AudioEvolve : IEvolve
{
	public List<AudioMixerSnapshot> snapshots;

	private int current;

	private void Start()
	{
		current = 0;
	}

	public override void Evolve()
	{
		if (current == snapshots.Count - 1)
		{
			return;
		}
		current++;
		snapshots[current].TransitionTo(0.2f);
	}
}
