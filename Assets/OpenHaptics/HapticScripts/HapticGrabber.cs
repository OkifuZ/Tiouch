using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//using HapticPlugin;


//! This object can be applied to the stylus of a haptic device. 
//! It allows you to pick up simulated objects and feel the involved physics.
//! Optionally, it can also turn off physics interaction when nothing is being held.
public class HapticGrabber : MonoBehaviour 
{
	public int buttonID = 0;		//!< index of the button assigned to grabbing.  Defaults to the first button
	public int buttonID_reset = 1;		//!< index of the button assigned to grabbing.  Defaults to the first button
	public bool ButtonActsAsToggle = false; //!< Toggle button? as opposed to a press-and-hold setup?  Defaults to off.
    public enum PhysicsToggleStyle{ none, onTouch, onGrab };
	public PhysicsToggleStyle physicsToggleStyle = PhysicsToggleStyle.none;   //!< Should the grabber script toggle the physics forces on the stylus? 

	public bool DisableUnityCollisionsWithTouchableObjects = true;

	private  GameObject hapticDevice = null;   //!< Reference to the GameObject representing the Haptic Device
	private bool buttonStatus = false;			//!< Is the button currently pressed?
	private bool reset_buttonStatus = false;			//!< Is the button currently pressed?
	private GameObject touching = null;         //!< Reference to the object currently touched
    private GameObject grabbing = null;			//!< Reference to the object currently grabbed
	private FixedJoint joint = null;			//!< The Unity physics joint created between the stylus and the object being grabbed.


	//! Automatically called for initialization
	void Start () 
	{
		if (hapticDevice == null)
		{

			HapticPlugin[] HPs = (HapticPlugin[])Object.FindObjectsOfType(typeof(HapticPlugin));
			foreach (HapticPlugin HP in HPs)
			{
				if (HP.hapticManipulator == this.gameObject)
				{
					hapticDevice = HP.gameObject;
				}
			}

		}

		if ( physicsToggleStyle != PhysicsToggleStyle.none)
			hapticDevice.GetComponent<HapticPlugin>().PhysicsManipulationEnabled = false;

		if (DisableUnityCollisionsWithTouchableObjects)
			disableUnityCollisions();
	}

	void disableUnityCollisions()
	{
		GameObject[] touchableObjects;
		touchableObjects =  GameObject.FindGameObjectsWithTag("Touchable") as GameObject[];  //FIXME  Does this fail gracefully?

		// Ignore my collider
		Collider myC = gameObject.GetComponent<Collider>();
		if (myC != null)
			foreach (GameObject T in touchableObjects)
			{
				Collider CT = T.GetComponent<Collider>();
				if (CT != null)
					Physics.IgnoreCollision(myC, CT);
			}
		
		// Ignore colliders in children.
		Collider[] colliders = gameObject.GetComponentsInChildren<Collider>();
		foreach (Collider C in colliders)
			foreach (GameObject T in touchableObjects)
			{
				Collider CT = T.GetComponent<Collider>();
				if (CT != null)
					Physics.IgnoreCollision(C, CT);
			}

	}

	private int cnt = 0;
	//! Update is called once per frame
	void FixedUpdate () 
	{
		if (cnt > 1000) cnt = 0;
		bool newButtonStatus = hapticDevice.GetComponent<HapticPlugin>().Buttons [buttonID] == 1;
		bool oldButtonStatus = buttonStatus;
		buttonStatus = newButtonStatus;
		bool newreset_botton = hapticDevice.GetComponent<HapticPlugin>().Buttons[buttonID_reset] == 1;
		bool old_reset_botton_stat = reset_buttonStatus;
		reset_buttonStatus = newreset_botton;

		if (old_reset_botton_stat == false && newreset_botton == true)
		{
			grabbed = false;
			var throwable = GameObject.Find("throw");
			if (throwable != null)
			{
				throwable.transform.position = new Vector3(-0.469999999f, 0.444000006f + 1.0f, -1.27999997f);
				throwable.GetComponent<Renderer>().enabled = true;
				throwable.GetComponent<Rigidbody>().velocity = new Vector3(0, 0, 0);

                int shapeID = throwable.GetInstanceID();
                string name = throwable.name;

                GameObject go = throwable;
                Mesh mesh = null;

                // If the object has a collision mesh, use that.
                MeshCollider collider = go.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    mesh = collider.sharedMesh;
                }
                if (mesh == null)
                {
                    MeshFilter filter = go.GetComponent<MeshFilter>();
                    if (filter != null)
                        mesh = filter.mesh;
                }

				// Vectors need to be converted to array of primatives. 
				// Triangles already are an array of ints.
				if (mesh != null)
				{
					double[] vertices = HapticPlugin.Vector3ArrayToDoubleArray(mesh.vertices);
					int[] triangles = mesh.triangles;

					HapticPlugin.shape_define(shapeID, name, vertices, triangles, vertices.Length, triangles.Length);
				}
            }
		}
		if (grabbed)
			cnt += 1;
        if (oldButtonStatus == false && newButtonStatus == true)
		{
			if (ButtonActsAsToggle)
			{
				if (grabbing)
				{
                    release();
                    Debug.Log("-1");
                }
                else
				{
                    grab();
                    Debug.Log("0");

                }
            } else
			{
				Debug.Log("1");
				grab();
            }
		}
		if (oldButtonStatus == true && newButtonStatus == false)
		{
			if (ButtonActsAsToggle)
			{
				Debug.Log("2");
                //Do Nothing
            }
            else
			{
				Debug.Log("3");
				release();
            }
		}

		// Make sure haptics is ON if we're grabbing
		if( grabbing && physicsToggleStyle != PhysicsToggleStyle.none)
			hapticDevice.GetComponent<HapticPlugin>().PhysicsManipulationEnabled = true;
		if (!grabbing && physicsToggleStyle == PhysicsToggleStyle.onGrab)
			hapticDevice.GetComponent<HapticPlugin>().PhysicsManipulationEnabled = false;

		/*
		if (grabbing)
			hapticDevice.GetComponent<HapticPlugin>().shapesEnabled = false;
		else
			hapticDevice.GetComponent<HapticPlugin>().shapesEnabled = true;
			*/
			
	}

	private void hapticTouchEvent( bool isTouch )
	{
		if (physicsToggleStyle == PhysicsToggleStyle.onGrab)
		{
			if (isTouch)
				hapticDevice.GetComponent<HapticPlugin>().PhysicsManipulationEnabled = true;
			else			
				return; // Don't release haptics while we're holding something.
		}
			
		if( physicsToggleStyle == PhysicsToggleStyle.onTouch )
		{
			hapticDevice.GetComponent<HapticPlugin>().PhysicsManipulationEnabled = isTouch;
			GetComponentInParent<Rigidbody>().velocity = Vector3.zero;
			GetComponentInParent<Rigidbody>().angularVelocity = Vector3.zero;

		}
	}

	void OnCollisionEnter(Collision collisionInfo)
	{
		Collider other = collisionInfo.collider;
		//Debug.unityLogger.Log("OnCollisionEnter : " + other.name);
		GameObject that = other.gameObject;
		Rigidbody thatBody = that.GetComponent<Rigidbody>();

		// If this doesn't have a rigidbody, walk up the tree. 
		// It may be PART of a larger physics object.
		while (thatBody == null)
		{
			//Debug.logger.Log("Touching : " + that.name + " Has no body. Finding Parent. ");
			if (that.transform == null || that.transform.parent == null)
				break;
			GameObject parent = that.transform.parent.gameObject;
			if (parent == null)
				break;
			that = parent;
			thatBody = that.GetComponent<Rigidbody>();
		}

		if( collisionInfo.rigidbody != null )
			hapticTouchEvent(true);

		if (thatBody == null)
			return;

		if (thatBody.isKinematic)
			return;
	
		touching = that;
	}
	void OnCollisionExit(Collision collisionInfo)
	{
		Collider other = collisionInfo.collider;
		//Debug.unityLogger.Log("onCollisionrExit : " + other.name);

		if( collisionInfo.rigidbody != null )
			hapticTouchEvent( false );

		if (touching == null)
			return; // Do nothing

		if (other == null ||
		    other.gameObject == null || other.gameObject.transform == null)
			return; // Other has no transform? Then we couldn't have grabbed it.

		if( touching == other.gameObject || other.gameObject.transform.IsChildOf(touching.transform))
		{
			touching = null;
		}
	}

	private bool grabbed = false;
		
	//! Begin grabbing an object. (Like closing a claw.) Normally called when the button is pressed. 
	void grab()
	{
		GameObject touchedObject = touching;
		if (touchedObject == null) // No Unity Collision? 
		{
			// Maybe there's a Haptic Collision
			touchedObject = hapticDevice.GetComponent<HapticPlugin>().touching;
		}

		if (grabbing != null) // Already grabbing
			return;
		if (touchedObject == null) // Nothing to grab
			return;

		// Grabbing a grabber is bad news.
		if (touchedObject.tag =="Gripper")
			return;

		Debug.Log( " Object : " + touchedObject.name + "  Tag : " + touchedObject.tag );

		grabbing = touchedObject;

		//Debug.logger.Log("Grabbing Object : " + grabbing.name);
		Rigidbody body = grabbing.GetComponent<Rigidbody>();

		// If this doesn't have a rigidbody, walk up the tree. 
		// It may be PART of a larger physics object.
		while (body == null)
		{
			//Debug.logger.Log("Grabbing : " + grabbing.name + " Has no body. Finding Parent. ");
			if (grabbing.transform.parent == null)
			{
				grabbing = null;
				return;
			}
			GameObject parent = grabbing.transform.parent.gameObject;
			if (parent == null)
			{
				grabbing = null;
				return;
			}
			grabbing = parent;
			body = grabbing.GetComponent<Rigidbody>();
		}

		joint = (FixedJoint)gameObject.AddComponent(typeof(FixedJoint));
		joint.connectedBody = body;

		grabbed = true;
		// joint.connectedBody.gameObject.GetComponent<Renderer>().enabled = true;
    }
	//! changes the layer of an object, and every child of that object.
	static void SetLayerRecursively(GameObject go, int layerNumber )
	{
		if( go == null ) return;
		foreach(Transform trans in go.GetComponentsInChildren<Transform>(true))
			trans.gameObject.layer = layerNumber;
	}

	//! Stop grabbing an obhject. (Like opening a claw.) Normally called when the button is released. 
	void release()
	{
		if( grabbing == null ) //Nothing to release
			return;


		Debug.Assert(joint != null);
        Debug.Log("release");
        if (joint.connectedBody != null)
        {
			if (cnt > 100)
			{
                HapticPlugin.shape_remove(joint.connectedBody.gameObject.GetInstanceID());
                //HapticPlugin.shape_disableShapeRendering();
                var throwable = joint.connectedBody.gameObject;
                throwable.GetComponent<Renderer>().enabled = false;
                var rb_throwable = throwable.GetComponent<Rigidbody>();
                // Destroy(joint.connectedBody.gameObject);
                /*joint.connectedBody.gameObject.GetComponent<Renderer>().enabled = false;
                joint.connectedBody.gameObject.transform.position = new Vector3(1000f, 1000f, 1000f);
                Destroy(joint.connectedBody.gameObject);*/
                var rigid_scene = GameObject.Find("rigid_scene").GetComponent<RigidScene>();
                if (rigid_scene != null)
                {
                    var pos = throwable.transform.position;
                    var velo = rb_throwable.velocity;
                    rigid_scene.add_box_with_v(pos, throwable.transform.rotation, throwable.transform.localScale[0] / 2.0f, velo * 3);
                }
                else Debug.LogError("error: rigid scene is nullptr in release");
            }
        }
        joint.connectedBody = null;
		grabbed = false;

        Destroy(joint);

        cnt = 0;


        grabbing = null;

		if (physicsToggleStyle != PhysicsToggleStyle.none)
			hapticDevice.GetComponent<HapticPlugin>().PhysicsManipulationEnabled = false;
			
	}

	//! Returns true if there is a current object. 
	public bool isGrabbing()
	{
		return (grabbing != null);
	}
}
