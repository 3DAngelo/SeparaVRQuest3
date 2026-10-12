using UnityEngine;
using UnityEngine.UI;

public class SightSelection : MonoBehaviour
{
	public enum Action
	{
		OVER = 0,
		OUT = 1,
		SELECTED = 2
	}

	public Image timeFillImage;

	public float raycastDistance = 1f;

	public float TiempoSeleccion;

	private bool selected;

	private GameObject spoted;

	private float timeOver;

	public string selectionLayer = "Selectable";

	private void Start()
	{
		if (timeFillImage != null)
		{
			timeFillImage.fillAmount = 0f;
		}
	}

	private void Update()
	{
		RaycastHit hit;
		Ray ray = new Ray(transform.position, transform.forward);
		if (Physics.Raycast(ray, out hit, raycastDistance, 1 << LayerMask.NameToLayer(selectionLayer)))
		{
			if (spoted == null || spoted != hit.collider.gameObject)
			{
				if (spoted != null)
				{
					if (timeFillImage != null)
					{
						timeFillImage.fillAmount = 0f;
					}
					spoted.SendMessage("OnOut", SendMessageOptions.DontRequireReceiver);
				}
				spoted = hit.collider.gameObject;
				timeOver = 0f;
				selected = false;
				spoted.SendMessage("OnOver", SendMessageOptions.DontRequireReceiver);
			}
			else if (spoted == hit.collider.gameObject)
			{
				timeOver += Time.deltaTime;
				if (timeFillImage != null)
				{
					timeFillImage.fillAmount = timeOver / TiempoSeleccion;
				}
				if (timeOver >= TiempoSeleccion && !selected)
				{
					if (timeFillImage != null)
					{
						timeFillImage.fillAmount = 0f;
					}
					spoted.SendMessage("OnSelected", SendMessageOptions.DontRequireReceiver);
					selected = true;
				}
			}
		}
		else
		{
			selected = false;
			if (spoted != null)
			{
				if (timeFillImage != null)
				{
					timeFillImage.fillAmount = 0f;
				}
				spoted.SendMessage("OnOut", SendMessageOptions.DontRequireReceiver);
				spoted = null;
			}
		}
	}
}
