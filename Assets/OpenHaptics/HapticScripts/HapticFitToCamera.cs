using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HapticFitToCamera : MonoBehaviour 
{
	public Camera masterCamera;

	public enum BoxType{ usableWorkspace, maxWorkspace };
	public BoxType FitToWorkspace = BoxType.usableWorkspace;

	public enum ConstraintType{ uniform, nonuniform, nonuniformConstrainZ };
	public ConstraintType constraint;

	[Range(0.0f, 1.0f)]
	public float MinimumDepth = 0.25f;

	public float margin = 0.25f;

	public GameObject Greeting = null;
	public float greetingTime = 5.0f;


	// Use this for initialization
	void Start () 
	{
		// do nothing
	}
	
	// Update is called once per frame
	void Update () 
	{
		// Make sure camera and plugin exist.
		if (masterCamera == null)
			return;

		// Deal with the greeting 
		float T = Time.realtimeSinceStartup - greetingTime;
		if (T > 0 && Greeting != null)
		{
			float alpha = Mathf.Max( 1.0f - T, 0f );
			Greeting.GetComponent<CanvasGroup>().alpha = alpha;
		}


		HapticPlugin plugin = (HapticPlugin)gameObject.GetComponent(typeof(HapticPlugin));

		if (plugin == null)
		{
			Debug.LogError("HapticFitToCamera must be attached to a HapticDevice.");
			return;
		}

		// Extents are in array, these are the indexes
		const int minX = 0;
		const int minY = 1;
		const int minZ = 2;
		const int maxX = 3;
		const int maxY = 4;
		const int maxZ = 5;

		// dimensions and position of haptic volume (In raw, unscaled coordinates.)
		float hapticWidth;		
		float hapticHeight;		
		float hapticDepth;	
		Vector3 hapticCenter;

		if( FitToWorkspace == BoxType.usableWorkspace )
		{
			hapticWidth = (float)(plugin.usable_extents[maxX] - plugin.usable_extents [minX]);
			hapticHeight = (float)(plugin.usable_extents[maxY] - plugin.usable_extents [minY]);
			hapticDepth = (float)(plugin.usable_extents [maxZ] - plugin.usable_extents [minZ]);
			hapticCenter = new Vector3( 
				(float)(plugin.usable_extents[maxX] + plugin.usable_extents [minX]) / 2,
				(float)(plugin.usable_extents[maxY] + plugin.usable_extents [minY]) / 2,
				(float)(plugin.usable_extents[maxZ] + plugin.usable_extents [minZ]) / 2);
		}
		else
		{
			hapticWidth = (float)(plugin.max_extents[maxX] - plugin.max_extents [minX]);
			hapticHeight = (float)(plugin.max_extents[maxY] - plugin.max_extents [minY]);
			hapticDepth = (float)(plugin.max_extents [maxZ] - plugin.max_extents [minZ]);
			hapticCenter = new Vector3 (
				(float)(plugin.max_extents [maxX] + plugin.max_extents [minX]) / 2,
				(float)(plugin.max_extents [maxY] + plugin.max_extents [minY]) / 2,
				(float)(plugin.max_extents [maxZ] + plugin.max_extents [minZ]) / 2);
		}	

		// Add the margin to the dimensions of the box we're trying to place.
		hapticWidth *= (1.0f + margin);
		hapticHeight *= (1.0f + margin);
		hapticDepth *= (1.0f + margin);

		if (gameObject.transform.parent != null)
		{
			hapticWidth *= gameObject.transform.parent.localScale.x;
			hapticHeight *= gameObject.transform.parent.localScale.y;
			hapticDepth *= gameObject.transform.parent.localScale.z;
		}


		// How Close to the camera should the haptic box get.
		float camPlacementPlane = MinimumDepth; 

		// Dimantions of the box defined by camera-to-box distance defined above, and the camera frustrum
		float camDepth = masterCamera.nearClipPlane + (masterCamera.farClipPlane - masterCamera.nearClipPlane) * camPlacementPlane; // Halfway through the camera space.
		float camWidth = (masterCamera.ViewportToWorldPoint(new Vector3 (0, 0, camDepth)) - masterCamera.ViewportToWorldPoint(new Vector3 (1, 0, camDepth))).magnitude;
		float camHeight = (masterCamera.ViewportToWorldPoint(new Vector3 (0, 0, camDepth)) - masterCamera.ViewportToWorldPoint(new Vector3 (0, 1, camDepth))).magnitude;

		// Compare the haptic box with the camera box to determine caling.
		float ratioX = camWidth / hapticWidth;
		float ratioY = camHeight / hapticHeight;
		float ratioZ = (masterCamera.farClipPlane-camDepth) / (hapticDepth);
		if (constraint == ConstraintType.uniform)
		{
			// Uniform scaling must take the smallest dimension. (Otherwise there's an overhang.)
			float ratio = Mathf.Min(ratioZ, Mathf.Min(ratioX, ratioY));
			ratioX = ratio;
			ratioY = ratio;
			ratioZ = ratio;
		}
		else if( constraint == ConstraintType.nonuniformConstrainZ)
		{
			// Don't let the Z go out to the horizon. Find some reasonable value for it.
			float ratio = Mathf.Min(ratioZ, Mathf.Max(ratioX, ratioY));
			ratioZ = ratioX;
		}

		// Apply the rotation, scale and translation to the haptics objectt.
		gameObject.transform.rotation = masterCamera.transform.rotation;
		gameObject.transform.localScale = new Vector3 (ratioX, ratioY, ratioZ);
		gameObject.transform.position = masterCamera.ViewportToWorldPoint(new Vector3 (0.5f, 0.5f, camDepth + (ratioZ * hapticDepth)/2));

		/*
		// If the haptics volume was offset from zero, add that compensation back in.
		hapticCenter.Scale(gameObject.transform.localScale);
		gameObject.transform.position -= gameObject.transform.rotation *hapticCenter;*/
	}
		
#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		if (masterCamera == null)
			return;

		// No point drawing the lines if we're not doing anything.
		if (Application.isPlaying == false)
			return;

		// Draw some lines roughly indicating the camera frustrum
		Vector3 A = new Vector3 ();
		Vector3 B = new Vector3 ();
		Gizmos.color = new Color (Color.magenta.r, Color.magenta.g, Color.magenta.b, 0.5f); // Transparent Magenta
		for (int xx = 0; xx < 2; xx++)
			for (int yy = 0; yy < 2; yy++)
			{
				A.Set(xx, yy, masterCamera.nearClipPlane);		
				B.Set(xx, yy, masterCamera.farClipPlane);
				Gizmos.DrawLine(
					masterCamera.ViewportToWorldPoint(A),
					masterCamera.ViewportToWorldPoint(B));
			}
	}
#endif
	private static Quaternion QuaternionFromMatrix(Matrix4x4 m) 
	{ 
		return Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
	}
}
