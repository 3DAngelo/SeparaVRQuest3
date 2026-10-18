using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Full-screen fade to / from black that works in URP and XR (single pass instanced), and scene changes behind it.
///
/// Self-creating singleton: the first use of <see cref="Instance"/> creates a "ScreenFader" GameObject that survives scene
/// loads (DontDestroyOnLoad), so a fade started in one scene can finish in the next one. While <see cref="Alpha"/> &gt; 0 it
/// draws, every frame from LateUpdate, a quad already in clip space with Assets/Resources/ScreenFade.shader through
/// Graphics.RenderMesh (VRScreenFade draws from OnPostRender, which URP never calls, so it is never visible).
///
/// <see cref="FadeOutAndLoad"/> keeps the view black through the whole load: on Quest the compositor keeps showing (and
/// reprojecting) the last submitted frame while the game hitches, so that frame must already be black, and the new scene
/// is only revealed once its first, heavy frames are over. If the shader is missing the timing (and so the load) still
/// works, just without anything visible.
/// </summary>
public class ScreenFader : MonoBehaviour
{
	// Default durations of the fades, in unscaled seconds.
	public const float DefaultFadeOutSeconds = 1f;
	public const float DefaultFadeInSeconds = 1f;
	// Frames rendered fully black before the load starts, so the frame the compositor holds during the hitch is black.
	// A few rather than one: the XR render thread and the compositor run a frame or two behind the main thread.
	public const int BlackFramesBeforeLoad = 3;
	// Once the new scene is loaded, it stays black for at least this many frames and seconds: Start and the first frames
	// (shader warm-up, physics, GC) are the heavy ones and must happen while nothing can be seen.
	public const int BlackFramesAfterLoad = 5;
	public const float MinBlackSecondsAfterLoad = 0.25f;
	// Largest time step a fade advances in one frame: after a hitch the next frame would otherwise skip the whole fade.
	public const float MaxFadeStep = 0.05f;
	// Size of the bounds given to RenderMesh, centred on the camera, so the overlay is never frustum-culled.
	public const float CullingBoundsSize = 100000f;
	// Assets/Resources/ScreenFade.shader
	public const string ShaderResource = "ScreenFade";

	private static readonly int ColorId = Shader.PropertyToID("_Color");

	private static ScreenFader instance;

	/// <summary>Colour of the overlay; its alpha is multiplied by <see cref="Alpha"/>.</summary>
	public Color FadeColor = Color.black;

	private float alpha;
	private Material material;
	private Mesh quad;
	private Coroutine fadeRoutine;
	private bool loading;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		instance = null;
	}

	/// <summary>The fader, created (and kept across scenes) on first use.</summary>
	public static ScreenFader Instance
	{
		get
		{
			if (instance == null)
			{
				GameObject go = new GameObject("ScreenFader");
				// Awake assigns instance and calls DontDestroyOnLoad.
				go.AddComponent<ScreenFader>();
			}
			return instance;
		}
	}

	/// <summary>Opacity of the overlay, 0 (invisible) .. 1 (fully black). A running fade overwrites it every frame.</summary>
	public float Alpha
	{
		get { return alpha; }
		set { alpha = Mathf.Clamp01(value); }
	}

	/// <summary>True from <see cref="FadeOutAndLoad"/> until the new scene has fully faded in.</summary>
	public bool IsLoading
	{
		get { return loading; }
	}

	private void Awake()
	{
		if (instance != null && instance != this)
		{
			#if UNITY_EDITOR
			Debug.LogWarning("ScreenFader: a second instance on '" + name + "' was ignored.");
			#endif
			Destroy(this);
			return;
		}
		instance = this;
		DontDestroyOnLoad(gameObject);

		// The shader lives in Assets/Resources so that it is always included in the build (Shader.Find alone would not do that).
		Shader shader = Resources.Load<Shader>(ShaderResource);
		if (shader != null && shader.isSupported)
		{
			material = new Material(shader);
			material.name = "Screen fade";
			material.enableInstancing = true;
			material.hideFlags = HideFlags.HideAndDontSave;
		}
		else
		{
			#if UNITY_EDITOR
			Debug.LogWarning("ScreenFader: shader 'Resources/" + ShaderResource + "' not found or not supported; fades will not be visible (scene loads still work).");
			#endif
		}

		// Vertices directly in clip space (the shader ignores the matrices): this quad covers the whole view.
		quad = new Mesh();
		quad.name = "Screen fade quad";
		quad.hideFlags = HideFlags.HideAndDontSave;
		quad.vertices = new Vector3[]
		{
			new Vector3(-1f, -1f, 0f),
			new Vector3(1f, -1f, 0f),
			new Vector3(1f, 1f, 0f),
			new Vector3(-1f, 1f, 0f)
		};
		quad.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
		quad.bounds = new Bounds(Vector3.zero, Vector3.one * CullingBoundsSize);
	}

	private void OnDestroy()
	{
		if (instance == this)
		{
			instance = null;
		}
		if (material != null)
		{
			Destroy(material);
		}
		if (quad != null)
		{
			Destroy(quad);
		}
	}

	/// <summary>Fades to black over <paramref name="seconds"/> (unscaled). Ignored while a scene load is in progress.</summary>
	public Coroutine FadeOut(float seconds)
	{
		return StartFade(1f, seconds);
	}

	/// <summary>Fades from black over <paramref name="seconds"/> (unscaled). Ignored while a scene load is in progress.</summary>
	public Coroutine FadeIn(float seconds)
	{
		return StartFade(0f, seconds);
	}

	/// <summary>
	/// Fades to black, loads the scene <paramref name="buildIndex"/> asynchronously while fully black, waits for its first
	/// frames and fades it in. Calls made while a load is already in progress are ignored.
	/// </summary>
	public void FadeOutAndLoad(int buildIndex, float fadeOutSeconds, float fadeInSeconds)
	{
		if (loading)
		{
			#if UNITY_EDITOR
			Debug.LogWarning("ScreenFader: a scene load is already in progress; request for scene " + buildIndex + " ignored.");
			#endif
			return;
		}
		if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
		{
			Debug.LogError("ScreenFader: scene build index " + buildIndex + " is not in the build settings.");
			return;
		}
		loading = true;
		StopFade();
		fadeRoutine = StartCoroutine(FadeOutAndLoadRoutine(buildIndex, fadeOutSeconds, fadeInSeconds));
	}

	private Coroutine StartFade(float target, float seconds)
	{
		if (loading)
		{
			// The load sequence owns the overlay until its fade in ends; a second fade would fight it.
			#if UNITY_EDITOR
			Debug.LogWarning("ScreenFader: fade request ignored, a scene load is in progress.");
			#endif
			return null;
		}
		StopFade();
		fadeRoutine = StartCoroutine(FadeRoutine(target, seconds));
		return fadeRoutine;
	}

	private void StopFade()
	{
		if (fadeRoutine != null)
		{
			StopCoroutine(fadeRoutine);
			fadeRoutine = null;
		}
	}

	private IEnumerator FadeRoutine(float target, float seconds)
	{
		yield return FadeTo(target, seconds);
		fadeRoutine = null;
	}

	// Moves alpha towards target at a constant rate (a full 0..1 fade takes 'seconds'), so a fade that starts half way
	// takes half the time. The alpha set in a frame is drawn by that same frame's LateUpdate.
	private IEnumerator FadeTo(float target, float seconds)
	{
		if (seconds <= 0f)
		{
			alpha = target;
			yield break;
		}
		while (alpha != target)
		{
			float step = Mathf.Min(Time.unscaledDeltaTime, MaxFadeStep) / seconds;
			alpha = Mathf.MoveTowards(alpha, target, step);
			yield return null;
		}
	}

	private IEnumerator FadeOutAndLoadRoutine(int buildIndex, float fadeOutSeconds, float fadeInSeconds)
	{
		yield return FadeTo(1f, fadeOutSeconds);
		alpha = 1f;

		for (int i = 0; i < BlackFramesBeforeLoad; i++)
		{
			yield return null;
		}

		// Nothing can be seen now, so let the loader use bigger time slices per frame (faster load, choppier frames).
		ThreadPriority previousPriority = Application.backgroundLoadingPriority;
		Application.backgroundLoadingPriority = ThreadPriority.High;
		AsyncOperation load = SceneManager.LoadSceneAsync(buildIndex);
		if (load == null)
		{
			Debug.LogError("ScreenFader: LoadSceneAsync(" + buildIndex + ") failed.");
			Application.backgroundLoadingPriority = previousPriority;
		}
		else
		{
			while (!load.isDone)
			{
				alpha = 1f;
				yield return null;
			}
			Application.backgroundLoadingPriority = previousPriority;
		}

		// The new scene is active: keep it black through its first frames.
		float loadedTime = Time.unscaledTime;
		int frames = 0;
		while (frames < BlackFramesAfterLoad || Time.unscaledTime - loadedTime < MinBlackSecondsAfterLoad)
		{
			alpha = 1f;
			frames++;
			yield return null;
		}

		yield return FadeTo(0f, fadeInSeconds);
		loading = false;
		fadeRoutine = null;
	}

	private void LateUpdate()
	{
		if (alpha <= 0f || material == null || quad == null)
		{
			return;
		}

		// Looked up every frame: each scene has its own XR rig and camera.
		Camera cam = Camera.main;
		Vector3 center = (cam != null) ? cam.transform.position : Vector3.zero;

		Color color = FadeColor;
		color.a *= alpha;
		material.SetColor(ColorId, color);

		RenderParams rp = new RenderParams(material);
		// Only the main (XR) camera; with no main camera it is drawn by every camera.
		rp.camera = cam;
		rp.layer = (cam != null) ? FirstLayerIn(cam.cullingMask) : 0;
		// Huge bounds around the camera: the clip-space quad has no meaningful world position and must never be culled.
		rp.worldBounds = new Bounds(center, Vector3.one * CullingBoundsSize);
		rp.shadowCastingMode = ShadowCastingMode.Off;
		rp.receiveShadows = false;
		rp.lightProbeUsage = LightProbeUsage.Off;
		rp.reflectionProbeUsage = ReflectionProbeUsage.Off;
		rp.motionVectorMode = MotionVectorGenerationMode.ForceNoMotion;
		Graphics.RenderMesh(rp, quad, 0, Matrix4x4.Translate(center));
	}

	// First layer the camera renders, so the overlay is not filtered out by its culling mask.
	private static int FirstLayerIn(int cullingMask)
	{
		for (int layer = 0; layer < 32; layer++)
		{
			if ((cullingMask & (1 << layer)) != 0)
			{
				return layer;
			}
		}
		return 0;
	}
}
