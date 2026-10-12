using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TestPerformance : MonoBehaviour
{
	[Serializable]
	public class Objetos
	{
		public GameObject gObject;

		public KeyCode shortcut;
	}

	public List<Objetos> objetos;

	public Text messageField;

	private void Start()
	{
	}

	private void Update()
	{
		if (objetos.Count > 0)
		{
			foreach (Objetos objeto in objetos)
			{
				if (Input.GetKeyDown(objeto.shortcut))
				{
					bool wasActive = objeto.gObject.activeSelf;
					objeto.gObject.SetActive(!wasActive);
					StartCoroutine(ShowMessage(objeto.gObject.name + (wasActive ? " DESACTIVADO" : " ACTIVADO")));
				}
			}
		}
	}

	private IEnumerator ShowMessage(string message)
	{
		messageField.text = message;
		yield return new WaitForSeconds(3f);
		messageField.text = "";
	}
}
