using UnityEngine;

[RequireComponent(typeof(Crosshair))]
public class IntroCroshair : MonoBehaviour
{
	private Crosshair crosshair;

	private bool visible;

	public LayerMask mask;

	private void Start()
	{
		crosshair = GetComponent<Crosshair>();
		crosshair.Hide();
	}

	private void Update()
	{
		bool hit = Physics.Raycast(crosshair.anchor.position, crosshair.anchor.forward, Mathf.Infinity, mask);
		if (hit && !visible)
		{
			crosshair.Show();
			visible = true;
		}
		else if (visible && !hit)
		{
			crosshair.Hide();
			visible = false;
		}
	}
}
