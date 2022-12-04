using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HapticPluginSafetyScript : MonoBehaviour {

	public bool SafeOnMovedReferenceFrame = true;
	public float SafeOnFrameratesBelow = 15.0f;

	private	HapticPlugin Haptic = null;


	private float DOWNTIME = 0.5f; // Seconds before you turn the thing back on

	private float timeUntilRestart = 0;

	// Use this for initialization
	void Start () 
	{
		Haptic = gameObject.GetComponent(typeof(HapticPlugin)) as HapticPlugin;
		if (Haptic == null)
			Debug.LogError("HapticPluginSafetyScript must be attached to the same object as the HapticPlugin script.");
	}
	
	// Update is called once per frame
	void Update () 
	{
		if (Haptic == null)
			return;

		timeUntilRestart = Mathf.Min(0, (float)(timeUntilRestart + Time.unscaledDeltaTime));

		if (SafeOnMovedReferenceFrame && didMove())
		{
			Haptic.startSafetyMode();
			timeUntilRestart = -DOWNTIME;
			return;
		}

		double fps = 1.0 / Time.unscaledDeltaTime;
		if (fps < SafeOnFrameratesBelow)
		{
			Haptic.startSafetyMode();
			timeUntilRestart = -DOWNTIME;
			return;
		}

		if( timeUntilRestart >= 0 )
			Haptic.endSafetyMode();
	}


	private Matrix4x4 oldMatrix = Matrix4x4.zero;
	bool didMove()
	{
		bool output = false;
		if (gameObject.transform.localToWorldMatrix != oldMatrix && oldMatrix != Matrix4x4.zero )
			output = true;
		oldMatrix = gameObject.transform.localToWorldMatrix;
		return output;
	}
}
