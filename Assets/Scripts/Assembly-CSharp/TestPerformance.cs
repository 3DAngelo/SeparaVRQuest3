using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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

	// Debug only: disabled outside the editor.
	private void Awake()
	{
		if (!Application.isEditor)
		{
			enabled = false;
		}
	}

	private void Update()
	{
		if (objetos.Count > 0)
		{
			foreach (Objetos objeto in objetos)
			{
				if (WasPressed(objeto.shortcut))
				{
					bool wasActive = objeto.gObject.activeSelf;
					objeto.gObject.SetActive(!wasActive);
					StartCoroutine(ShowMessage(objeto.gObject.name + (wasActive ? " DESACTIVADO" : " ACTIVADO")));
				}
			}
		}
	}

	// The shortcuts stay serialized as KeyCode; this maps them to the Input System's Key by name.
	private static bool WasPressed(KeyCode keyCode)
	{
		if (Keyboard.current == null || keyCode == KeyCode.None)
		{
			return false;
		}
		string name = keyCode.ToString()
			.Replace("Alpha", "Digit")
			.Replace("Keypad", "Numpad")
			.Replace("Control", "Ctrl")
			.Replace("Return", "Enter");
		Key key;
		if (!Enum.TryParse(name, out key) || key == Key.None)
		{
			return false;
		}
		return Keyboard.current[key].wasPressedThisFrame;
	}

	private IEnumerator ShowMessage(string message)
	{
		messageField.text = message;
		yield return new WaitForSeconds(3f);
		messageField.text = "";
	}
}
