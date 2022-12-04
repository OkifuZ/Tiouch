using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RigidCube : MonoBehaviour
{
    private int STATE_STATIC = 0;
    private int STATE_KINIMATIC = 1;
    private int STATE_DYNAMIC = 2;

    public Vector3 box_pos;
    public Quaternion box_rot;
    // public float half_extent; // yeah~we don't need this any more, just assigning scale/2
    public int state;

    private Quaternion rot2quat(float[] rot)
    {
        // 3*3 => quaternion
        Matrix4x4 m = Matrix4x4.zero;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                m[i, j] = rot[3 * i + j];
        m[3, 3] = 1;
        Quaternion q = new Quaternion();
        q.w = Mathf.Sqrt(Mathf.Max(0, 1 + m[0, 0] + m[1, 1] + m[2, 2])) / 2;
        q.x = Mathf.Sqrt(Mathf.Max(0, 1 + m[0, 0] - m[1, 1] - m[2, 2])) / 2;
        q.y = Mathf.Sqrt(Mathf.Max(0, 1 - m[0, 0] + m[1, 1] - m[2, 2])) / 2;
        q.z = Mathf.Sqrt(Mathf.Max(0, 1 - m[0, 0] - m[1, 1] + m[2, 2])) / 2;
        q.x *= Mathf.Sign(q.x * (m[2, 1] - m[1, 2]));
        q.y *= Mathf.Sign(q.y * (m[0, 2] - m[2, 0]));
        q.z *= Mathf.Sign(q.z * (m[1, 0] - m[0, 1]));
        return q;
        // return Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
    }


    // Start is called before the first frame update
    void Start()
    {
        // now we need doing nothing...
    }

    // Update is called once per frame
    void Update()
    {
       
        transform.position = box_pos;
        transform.rotation = box_rot;
    }
}
