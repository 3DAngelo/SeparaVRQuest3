using UnityEngine;

public class Residuo : MonoBehaviour
{
	public Container.Type IDcontenedor;

	public string descripcion;

	public Material defaultMaterial;

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
}
