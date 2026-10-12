using System.Collections;
using UnityEngine;

public class Crosshair : MonoBehaviour
{
	public LayerMask raycastMask;

	[HideInInspector]
	public Transform anchor;

	private Vector3 originalScale;

	public float scaleFactor = 1f;

	private void Start()
	{
		GameObject mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
		if (!mainCamera)
		{
			Debug.LogError("ponete las pilas y taguea la MainCamera locoo");
		}
		anchor = mainCamera.transform;
		originalScale = transform.localScale;
	}

	private void Update()
	{
		RaycastHit hit;
		float distance;
		transform.rotation = anchor.rotation;
		if (Physics.Raycast(new Ray(anchor.position, anchor.forward), out hit, 1000f, raycastMask))
		{
			distance = hit.distance;
			transform.position = hit.point;
		}
		else
		{
			distance = 950f;
			transform.position = anchor.position + anchor.forward * distance;
		}
		transform.localScale = originalScale * distance * scaleFactor;
	}

	public void Hide()
	{
		StartCoroutine(SetScaleFactor(0f));
	}

	public void Show()
	{
		StartCoroutine(SetScaleFactor(1f));
	}

	private IEnumerator SetScaleFactor(float to)
	{
		float elapsed = 0f;
		float from = scaleFactor;
		while (elapsed < 1f)
		{
			// RECONSTRUCCION-DUDA: el binario multiplica por 4f; pudo estar escrito como Time.deltaTime / 0.25f
			elapsed = Mathf.Clamp01(elapsed + Time.deltaTime * 4f);
			scaleFactor = Mathf.Lerp(from, to, elapsed);
			yield return null;
		}
	}
}
