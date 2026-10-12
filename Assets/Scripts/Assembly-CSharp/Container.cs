using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class Container : XRBaseInteractable
{
	public enum Type
	{
		GARBAGE = 0,
		RECYCLE = 1,
		BIG_GREENS = 2,
		ELECTRONICS = 3
	}

	public Type IDcontainer;

	public KeyCode shorcut = KeyCode.Alpha1;

	public string descripcion;

	public Transform arrojarAqui;

	private MeshRenderer _renderer;

	private Component[] childrenRenderers;

	public Material defaultMaterial;

	public Material hilightMaterial;

	private void Start()
	{
		childrenRenderers = GetComponentsInChildren<MeshRenderer>();
		_renderer = GetComponent<MeshRenderer>();
		SetMaterial(defaultMaterial);
	}

	private void SetMaterial(Material material)
	{
		if (_renderer)
		{
			_renderer.material = material;
		}
		foreach (MeshRenderer childRenderer in childrenRenderers)
		{
			childRenderer.material = material;
		}
	}

	protected override void OnHoverEntered(HoverEnterEventArgs args)
	{
		base.OnHoverEntered(args);
		SetMaterial(hilightMaterial);
		GameManager.Instance.OnContainerHovered(this);
	}

	protected override void OnHoverExited(HoverExitEventArgs args)
	{
		base.OnHoverExited(args);
		SetMaterial(defaultMaterial);
	}
}
