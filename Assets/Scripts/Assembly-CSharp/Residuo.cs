using UnityEngine;

public class Residuo : MonoBehaviour
{
	public Container.Type IDcontenedor;

	public string descripcion;

	public Material defaultMaterial;

	public Material hilightMaterial;

	private MeshRenderer myRenderer;

	private Component[] childrenRenderers;

	private void Start()
	{
		childrenRenderers = GetComponentsInChildren<MeshRenderer>();
		myRenderer = GetComponent<MeshRenderer>();
		if (myRenderer)
		{
			myRenderer.material = defaultMaterial;
		}
		foreach (MeshRenderer childRenderer in childrenRenderers)
		{
			childRenderer.material = defaultMaterial;
		}
	}

	private void Update()
	{
	}

	private void OnSelected()
	{
		// RECONSTRUCCION-DUDA: este bloque es identico al cuerpo de OnOut(); el original pudo llamar a OnOut() y quedar inlineado
		if (myRenderer)
		{
			myRenderer.material = defaultMaterial;
		}
		foreach (MeshRenderer childRenderer in childrenRenderers)
		{
			childRenderer.material = defaultMaterial;
		}
		StartCoroutine(GameManager.Instance.LevantarResiduos(transform));
	}

	public void OnOut()
	{
		if (myRenderer)
		{
			myRenderer.material = defaultMaterial;
		}
		foreach (MeshRenderer childRenderer in childrenRenderers)
		{
			childRenderer.material = defaultMaterial;
		}
	}

	public void OnOver()
	{
		if (myRenderer)
		{
			myRenderer.material = hilightMaterial;
		}
		foreach (MeshRenderer childRenderer in childrenRenderers)
		{
			childRenderer.material = hilightMaterial;
		}
	}
}
