using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


//! This MonoBehavior can be applied to any GameObject with a MeshCollider or MeshFilter.
//! It allows for the easy customization of the haptic properties of a "touchable" object.
public class HapticSurface : MonoBehaviour {


	public enum HLTOUCH_MODEL { HL_CONTACT, HL_CONSTRAINT };
	public HLTOUCH_MODEL hlTouchModel = HLTOUCH_MODEL.HL_CONTACT;  //!< HL_CONTACT is a normal object, HL_CONSTRAINT will force the stylus to stick to the surface of the mesh.

	public enum HLFACING { HL_FRONT, HL_BACK, HL_FRONT_AND_BACK };
	public HLFACING hlTouchable = HLFACING.HL_FRONT; //!< Which surface will be touchable? Front, Back, or Both?

	private bool Flip_Normals = false;  //!< There isn't areally a reason to use this.  

	[Range(0.0f, 1.0f)] public float hlStiffness = 0.7f;	//!< Higher values are less 'rubbery'.
	[Range(0.0f, 1.0f)] public float hlDamping = 0.1f;		//!< Higher values are less 'bouncy'.
	[Range(0.0f, 1.0f)] public float hlStaticFriction = 0.2f;	//!< Higher values have more 'sticky' surface friction.
	[Range(0.0f, 1.0f)] public float hlDynamicFriction = 0.3f;	//!< Higher values have more 'sliding' surface friction.
	[Range(0.0f, 1.0f)] public float hlPopThrough = 0.0f;	//!< If non-zero : How much force is required to "pop" through to the inside of the mesh.

	public float snapDistance = 1.0f; //!< When in HL_CONTRAINT mode, the maximum distance the stylus will "snap" to the surface.

	private bool oldFlipNormals = false;
	private float oldStiffness = -1;
	private float oldDamping = -1;
	private float oldStaticFriction = -1;
	private float oldDynamicFriction = -1;
	private float oldSnapDistance = -1;
	private float oldPopThrough = -1;
	private HLTOUCH_MODEL oldTouchModel = HLTOUCH_MODEL.HL_CONTACT;
	private HLFACING oldFacing = HLFACING.HL_FRONT;



	//! Used automatically for initialization
	void Start () {
		if (GetComponent<MeshCollider>() == null && GetComponent<MeshFilter>() == null)
		{
			Debug.LogError("HapticSurface has been assigned to object without mesh.");
		}

		if( gameObject.tag == "Untagged" )
			gameObject.tag = "Touchable";
		
	}
	
	//! Update is called once per frame and updates OpenHaptics with the current suface materials. 
	void Update () 
	{
		bool needUpdate = false;

		if (hlStiffness != oldStiffness) needUpdate = true;
		if (hlDamping != oldDamping) needUpdate = true;
		if (hlStaticFriction != oldStaticFriction) needUpdate = true;
		if (hlDynamicFriction != oldDynamicFriction) needUpdate = true;
		if (hlPopThrough != oldPopThrough) needUpdate = true;
		if (snapDistance != oldSnapDistance) needUpdate = true;
		if (hlTouchModel != oldTouchModel) needUpdate = true;
		if (Flip_Normals != oldFlipNormals)	needUpdate = true;
		if (hlTouchable != oldFacing) needUpdate = true;

		if (needUpdate)
		{
			HapticPlugin.shape_settings(gameObject.GetInstanceID(), hlStiffness, hlDamping, hlStaticFriction, hlDynamicFriction, hlPopThrough);

			int M = 0;
			if (hlTouchModel == HLTOUCH_MODEL.HL_CONSTRAINT)
				M = 1;
			
			HapticPlugin.shape_constraintSettings(gameObject.GetInstanceID(), M, snapDistance);
			HapticPlugin.shape_flipNormals(gameObject.GetInstanceID(), Flip_Normals);

			int T = 1;
			if (hlTouchable == HLFACING.HL_BACK) T = 2;
			if (hlTouchable == HLFACING.HL_FRONT_AND_BACK) T = 3;
			HapticPlugin.shape_facing(gameObject.GetInstanceID(), T);

			oldStiffness = hlStiffness;
			oldDamping = hlDamping;
			oldStaticFriction = hlStaticFriction;
			oldDynamicFriction = hlDynamicFriction;
			oldTouchModel = hlTouchModel;
			oldSnapDistance = snapDistance;
			oldPopThrough = hlPopThrough;
			oldFlipNormals = Flip_Normals;
			oldFacing = hlTouchable;

		}
		
	}

	void OnDestroy()
	{
		HapticPlugin.shape_remove(gameObject.GetInstanceID());
	}



}		
	

#if UNITY_EDITOR


[CustomEditor(typeof(HapticSurface))]
public class HapticSurfaceEditor : Editor
{
	override public void OnInspectorGUI()
	{
		HapticSurface HS = (HapticSurface)target;

		if (HS.gameObject.GetComponent<MeshCollider>() == null && HS.gameObject.GetComponent<MeshFilter>() == null)
		{
			EditorGUILayout.LabelField("*********************************************************");
			EditorGUILayout.LabelField("   Haptic Surface must be assigned to an object with a mesh.");
			EditorGUILayout.LabelField("*********************************************************");

		} else
		{
			HS.hlTouchModel = (HapticSurface.HLTOUCH_MODEL)EditorGUILayout.EnumPopup("HL_Touch_Model", HS.hlTouchModel);
			HS.hlTouchable = (HapticSurface.HLFACING)EditorGUILayout.EnumPopup("HL_Facing", HS.hlTouchable);

			switch (HS.hlTouchModel)
			{
			case HapticSurface.HLTOUCH_MODEL.HL_CONTACT:
				HS.hlStiffness = EditorGUILayout.Slider("Stiffness", HS.hlStiffness, 0.0f, 1.0f);
				HS.hlDamping = EditorGUILayout.Slider("Damping", HS.hlDamping, 0.0f, 1.0f);
				HS.hlStaticFriction = EditorGUILayout.Slider("Static Friction", HS.hlStaticFriction, 0.0f, 1.0f);
				HS.hlDynamicFriction = EditorGUILayout.Slider("Dynamic Friction", HS.hlDynamicFriction, 0.0f, 1.0f);
				HS.hlPopThrough = EditorGUILayout.Slider("Pop-through", HS.hlPopThrough, 0.0f, 1.0f);
				break;
			case HapticSurface.HLTOUCH_MODEL.HL_CONSTRAINT:
				HS.snapDistance = EditorGUILayout.FloatField("Snap Distance", HS.snapDistance);
				break;
			}
		}

	}
}
#endif
