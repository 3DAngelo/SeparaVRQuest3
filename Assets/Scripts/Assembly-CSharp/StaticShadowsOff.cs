using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// En runtime, los objetos con lightmap (estáticos horneados) dejan de proyectar sombras en tiempo real:
// su sombra ya está en el lightmap. En el editor y al bakear no se toca nada.
// Las sombras de los objetos dinámicos (residuos, manos) no cambian.
public static class StaticShadowsOff
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		foreach (GameObject root in scene.GetRootGameObjects())
		{
			foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				if (r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE)
				{
					r.shadowCastingMode = ShadowCastingMode.Off;
				}
			}
		}
	}
}
