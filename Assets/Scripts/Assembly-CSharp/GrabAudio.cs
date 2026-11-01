using UnityEngine;

/// <summary>
/// The two sounds of the grab: a soft confirmation when an object is taken, and a very soft cue when a hand closes on
/// nothing. Clips from Assets/Resources, loaded once; one shared 2D AudioSource. Nothing is allocated when they play.
/// </summary>
public static class GrabAudio
{
	public const string GrabClipName = "GrabConfirm";   // Assets/Resources/GrabConfirm.wav (copy of the XRI sample's Button Pop.wav: 0.18 s, peak 1.0)
	public const string MissClipName = "GrabMiss";      // Assets/Resources/GrabMiss.ogg (copy of AudioClip/Button_14_hover.ogg: 12 ms tick, peak 0.10)
	public const float GrabVolume = 0.4f;               // PlayOneShot scale of the confirmation (final peak about 0.40). Set from the measured level, not by ear: check on the Quest
	public const float MissVolume = 1.0f;               // PlayOneShot scale of the "nothing there" cue. The clip is already very quiet (final peak about 0.10): lower it and it is gone

	private static bool prepared;
	private static AudioClip grabClip;
	private static AudioClip missClip;
	private static AudioSource source;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		prepared = false;
		grabClip = null;
		missClip = null;
		source = null;
	}

	/// <summary>Loads the clips now, so the first grab does not hitch. Safe to call many times.</summary>
	public static void Prepare()
	{
		if (prepared)
		{
			return;
		}
		prepared = true;
		grabClip = Resources.Load<AudioClip>(GrabClipName);
		missClip = Resources.Load<AudioClip>(MissClipName);
		#if UNITY_EDITOR
		if (grabClip == null || missClip == null)
		{
			Debug.LogWarning("GrabAudio: missing clip in Assets/Resources (" + GrabClipName + " / " + MissClipName + ").");
		}
		#endif
	}

	public static void PlayGrab()
	{
		Prepare();
		Play(grabClip, GrabVolume);
	}

	public static void PlayMiss()
	{
		Prepare();
		Play(missClip, MissVolume);
	}

	private static void Play(AudioClip clip, float volume)
	{
		if (clip == null)
		{
			return;
		}
		if (source == null)
		{
			GameObject go = new GameObject("GrabAudio");
			Object.DontDestroyOnLoad(go);
			source = go.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 0f;
		}
		source.PlayOneShot(clip, volume);
	}
}
