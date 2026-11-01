using UnityEngine;

/// <summary>
/// The victory show: large fireworks of all colours launch from the street behind the plaza, rise leaving a trail and
/// burst high above the city. Play() once; the show lasts ShowSeconds and is destroyed with the scene.
/// </summary>
public class WinFireworks : MonoBehaviour
{
	// How long GameState waits before the screen fades (s).
	public const float ShowSeconds = 6f;
	// Time between launches (s) and how long a rocket takes to rise to its apex (s).
	public const float LaunchInterval = 0.8f;
	public const float RiseSeconds = 1.4f;
	// Apex height above the street (m) and its random spread around the launch point (m, +-).
	public const float ApexHeightMin = 35f;
	public const float ApexHeightMax = 48f;
	public const float ApexSpreadX = 3f;
	public const float ApexSpreadZ = 8f;
	// Rockets (shell + trail pairs) that can be in the air at once; a launch with none free is skipped.
	public const int ShellPool = 8;
	// From this time on (s) there are no more normal launches: one finale with a launch from every launch point.
	public const float FinaleTime = 4.2f;
	// How far the trail colour moves toward white, and every how many shells one uses two colours.
	public const float TrailTint = 0.3f;
	public const int MixEvery = 3;

	// World coordinates on the street behind the plaza in NuevoJuego (street column x 71..91, the player is around
	// x -8 looking toward +X, z 8), so they are specific to that scene.
	public static readonly Vector3[] LaunchPoints = { new Vector3(78f, 0.41f, 34f), new Vector3(78f, 0.41f, 16f), new Vector3(78f, 0.41f, 0f) };

	// Saturated colours: red, orange, yellow, green, cyan, blue, violet, pink.
	public static readonly Color[] Colors =
	{
		new Color(1f, 0.1f, 0.1f, 1f),
		new Color(1f, 0.5f, 0.05f, 1f),
		new Color(1f, 0.9f, 0.1f, 1f),
		new Color(0.15f, 1f, 0.2f, 1f),
		new Color(0.1f, 0.95f, 1f, 1f),
		new Color(0.15f, 0.3f, 1f, 1f),
		new Color(0.6f, 0.15f, 1f, 1f),
		new Color(1f, 0.25f, 0.7f, 1f)
	};

	private class Rocket
	{
		public ParticleSystem Shell;
		public ParticleSystem Trail;
		public bool Flying;
		public float Time;
		public Vector3 From;
		public Vector3 To;
		public Vector3 Position;
		public float PendingTrail;
	}

	private static WinFireworks instance;

	private Rocket[] rockets;
	private float time;
	private float nextLaunch;
	private bool finaleDone;
	private int launchCount;
	private int pointIndex;
	private int lastColor = -1;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		instance = null;
	}

	/// <summary>Starts the show (in the active scene, so it ends with the scene). A second call while it exists does nothing.</summary>
	public static void Play()
	{
		if (instance != null)
		{
			return;
		}
		GameObject go = new GameObject("Win Fireworks");
		instance = go.AddComponent<WinFireworks>();
	}

	private void Awake()
	{
		rockets = new Rocket[ShellPool];
		for (int i = 0; i < rockets.Length; i++)
		{
			Rocket rocket = new Rocket();
			rocket.Shell = FeedbackFX.CreateWinShell();
			rocket.Trail = FeedbackFX.CreateWinRocketTrail();
			if (rocket.Shell == null || rocket.Trail == null)
			{
				enabled = false;   // no material: no show
				Destroy(gameObject);
				return;
			}
			rocket.Shell.transform.SetParent(transform, false);
			rocket.Trail.transform.SetParent(transform, false);
			rockets[i] = rocket;
		}
	}

	private void Update()
	{
		time += Time.deltaTime;
		if (time < FinaleTime)
		{
			while (nextLaunch <= time)
			{
				Launch();
				nextLaunch += LaunchInterval;
			}
		}
		else if (!finaleDone)
		{
			finaleDone = true;
			for (int i = 0; i < LaunchPoints.Length; i++)
			{
				Launch();
			}
		}
		for (int i = 0; i < rockets.Length; i++)
		{
			if (rockets[i].Flying)
			{
				Fly(rockets[i]);
			}
		}
		if (time > ShowSeconds)
		{
			enabled = false;
		}
	}

	private void Launch()
	{
		Rocket rocket = null;
		for (int i = 0; i < rockets.Length; i++)
		{
			if (!rockets[i].Flying && !rockets[i].Shell.IsAlive(true))
			{
				rocket = rockets[i];
				break;
			}
		}
		Vector3 from = LaunchPoints[pointIndex];
		pointIndex = (pointIndex + 1) % LaunchPoints.Length;
		if (rocket == null)
		{
			return;
		}

		// Never the same colour twice in a row; every MixEvery-th shell mixes in a second colour.
		lastColor = (lastColor + 1 + Random.Range(0, Colors.Length - 1)) % Colors.Length;
		Color color = Colors[lastColor];
		launchCount++;
		if (launchCount % MixEvery == 0)
		{
			int other = (lastColor + 1 + Random.Range(0, Colors.Length - 1)) % Colors.Length;
			FeedbackFX.SetWinShellColor(rocket.Shell, color, Colors[other]);
		}
		else
		{
			FeedbackFX.SetWinShellColor(rocket.Shell, color);
		}
		ParticleSystem.MainModule trail = rocket.Trail.main;
		trail.startColor = Color.Lerp(color, Color.white, TrailTint);

		rocket.From = from;
		rocket.To = from + new Vector3(Random.Range(-ApexSpreadX, ApexSpreadX), Random.Range(ApexHeightMin, ApexHeightMax), Random.Range(-ApexSpreadZ, ApexSpreadZ));
		rocket.Time = 0f;
		rocket.Flying = true;
		rocket.Position = from;
		rocket.PendingTrail = 0f;
		rocket.Trail.Play();
	}

	private void Fly(Rocket rocket)
	{
		rocket.Time += Time.deltaTime;
		float u = Mathf.Clamp01(rocket.Time / RiseSeconds);
		float eased = 1f - (1f - u) * (1f - u);   // ease-out: fast start, decelerating
		Vector3 position = Vector3.Lerp(rocket.From, rocket.To, eased);
		rocket.PendingTrail += Vector3.Distance(rocket.Position, position) * FeedbackFX.WinTrailRate;
		int count = (int)rocket.PendingTrail;
		rocket.PendingTrail -= count;
		ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
		for (int i = 0; i < count; i++)
		{
			emit.position = Vector3.Lerp(rocket.Position, position, (i + 1f) / count);
			rocket.Trail.Emit(emit, 1);
		}
		rocket.Position = position;
		if (u >= 1f)
		{
			rocket.Flying = false;
			rocket.Trail.Stop(false, ParticleSystemStopBehavior.StopEmitting);   // the particles die by themselves
			rocket.Shell.transform.position = rocket.To;
			rocket.Shell.Play(true);
		}
	}
}
