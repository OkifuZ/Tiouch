using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Taichi;
using UnityEngine;
using static UnityEngine.Networking.UnityWebRequest;
using UnityEngine.UIElements;
using static UnityEditor.PlayerSettings;

public class RigidScene : MonoBehaviour
{
    private int STATE_STATIC = 0;
    private int STATE_KINIMATIC = 1;
    private int STATE_DYNAMIC = 2;

    private int max_obj = 500;

    private Mesh mesh;

    RigidCube[] rigidCubes;

    public AotModuleAsset rigidSceneModule;
    private ComputeGraph _Compute_Graph_g_init;
    private ComputeGraph _Compute_Graph_g_update;
    private ComputeGraph _Compute_Graph_g_add_box;
    private ComputeGraph _Compute_Graph_g_copy_to_nd;
    private ComputeGraph _Compute_Graph_g_set_ini_velocity;
    private ComputeGraph _Compute_Graph_g_reset_all;

    public float dt = 1.0f / 60f;
    public float corr_rate = 0.8f;
    public float damp = 0.95f;
    public Vector3 boundary_box_bot = new Vector3(-1.0f, 0.0f, -1.0f) * 1000;
    public Vector3 boundary_box_top = new Vector3(1.0f, 1.0f, 1.0f) * 1000;
    public Vector3 box_pos;
    public Quaternion box_rot;
    public Vector3 half_extent;
    public int state;

    public NdArray<float> object_cm_ndarray;
    public NdArray<float> object_rot_ndarray;
    public NdArray<float> boundary_box_ndarray;
    // TODO: 2box
    private int child_num;
    private float[] cm;
    private float[] rot;

    public void add_box_with_v(Vector3 pos, Quaternion rot, float extent, Vector3 v)
    {
        var new_cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        new_cube.transform.parent = gameObject.transform;
        new_cube.transform.position = pos;
        new_cube.transform.rotation = rot;
        new_cube.AddComponent<RigidCube>();
        new_cube.transform.localScale = new Vector3(extent, extent, extent) * 2.0f;

        var obj_id = child_num++;
        rigidCubes[obj_id] = new_cube.GetComponent<RigidCube>();

        if (_Compute_Graph_g_add_box != null)
            _Compute_Graph_g_add_box.LaunchAsync(new Dictionary<string, object>{
                { "center_x", pos.x },
                { "center_y", pos.y },
                { "center_z", pos.z },
                { "half_extent_x", extent },
                { "half_extent_y", extent },
                { "half_extent_z", extent },
                { "state", STATE_DYNAMIC },
                { "object_id", obj_id }
            });
        else Debug.LogError("Oh how could this be... _Compute_Graph_g_add_box missing!");

        if (_Compute_Graph_g_set_ini_velocity != null)
            _Compute_Graph_g_set_ini_velocity.LaunchAsync(new Dictionary<string, object>{
                { "object_id", obj_id },
                { "v_x", v.x },
                { "v_y", v.y },
                { "v_z", v.z },
            });
        else Debug.LogError("Oh how could this be... _Compute_Graph_g_set_ini_velocity missing!");
    }

    public void reset_scene()
    {
        if (_Compute_Graph_g_reset_all != null)
            _Compute_Graph_g_reset_all.LaunchAsync(new Dictionary<string, object>{
                { "total_objects", child_num }
            });
        else Debug.LogError("Oh how could this be... _Compute_Graph_g_reset_all missing!");
    }

    private Quaternion rot2quat(float[] rot)
    {
        // 3*3 => quaternion
        Matrix4x4 m = Matrix4x4.zero;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                m[i, j] = rot[3 * i + j];
            }
        }
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
        child_num = this.gameObject.transform.childCount;
        rigidCubes = new RigidCube[max_obj];
        for (int i = 0; i < child_num; i++)
        {
            rigidCubes[i] = this.gameObject.transform.GetChild(i).gameObject.GetComponent<RigidCube>();
            if (rigidCubes[i] == null) Debug.LogError("null child object reference in start()");
        }

        var cgraphs_list = rigidSceneModule.GetAllComputeGrpahs();
        var cgraphs = cgraphs_list.ToDictionary(x => x.Name);
        if (cgraphs.Count > 0)
        {
            _Compute_Graph_g_init = cgraphs["init"];
            _Compute_Graph_g_update = cgraphs["update"];
            _Compute_Graph_g_add_box = cgraphs["add_box"];
            _Compute_Graph_g_copy_to_nd = cgraphs["copy_to_nd"];
            _Compute_Graph_g_set_ini_velocity = cgraphs["set_ini_velocity"];
            _Compute_Graph_g_reset_all = cgraphs["reset_all"];
        }
        else Debug.LogError("Oh how could this be... compute graphs missing!");


        object_cm_ndarray = new NdArrayBuilder<float>().Shape(max_obj).ElemShape(3).HostRead().Build();
        object_rot_ndarray = new NdArrayBuilder<float>().Shape(max_obj).ElemShape(3, 3).HostRead().Build();
        boundary_box_ndarray = new NdArrayBuilder<float>().Shape(2).ElemShape(3).HostWrite().Build();

        // get all data from children
        var boundary_box_host = new float[6];
        boundary_box_host[0] = boundary_box_bot.x;
        boundary_box_host[1] = boundary_box_bot.y;
        boundary_box_host[2] = boundary_box_bot.z;
        boundary_box_host[3] = boundary_box_top.x;
        boundary_box_host[4] = boundary_box_top.y;
        boundary_box_host[5] = boundary_box_top.z;
        boundary_box_ndarray.CopyFromArray(boundary_box_host);

        cm = new float[3 * max_obj];
        rot = new float[9 * max_obj];

        if (_Compute_Graph_g_init != null)
            _Compute_Graph_g_init.LaunchAsync(new Dictionary<string, object>{});
        else Debug.LogError("Oh how could this be... _Compute_Graph_g_init missing!");

        for (int i = 0; i < child_num; i++)
        {
            var cube = rigidCubes[i];
            box_pos = cube.GetComponent<RigidCube>().transform.position;
            box_rot = cube.GetComponent<RigidCube>().transform.rotation;
            state = cube.GetComponent<RigidCube>().state;
            half_extent = cube.GetComponent<RigidCube>().transform.localScale / 2.0f;
            if (_Compute_Graph_g_add_box != null)
                _Compute_Graph_g_add_box.LaunchAsync(new Dictionary<string, object>{
                { "center_x", box_pos.x },
                { "center_y", box_pos.y },
                { "center_z", box_pos.z },
                { "half_extent_x", half_extent.x },
                { "half_extent_y", half_extent.y },
                { "half_extent_z", half_extent.z },
                { "state", state },
                { "object_id", i }
            });
            else Debug.LogError("Oh how could this be... _Compute_Graph_g_add_box missing!");
        }

        Debug.Log("successfully start!");
    }


    private bool added = false;
    // Update is called once per frame
    void Update()
    {
        /*transform.position = box_pos;
        transform.rotation = box_rot;*/

        if (Input.GetKeyDown(KeyCode.R))
        {
            if (!added) reset_scene();
        }
        if (Input.GetKeyDown(KeyCode.A))
        {
            add_box_with_v(new Vector3(0, 6.0f, 0), Quaternion.identity, 0.2f, new Vector3(2.0f, 0, 0));
            added = true;
        }
        

        if (_Compute_Graph_g_update != null)
            _Compute_Graph_g_update.LaunchAsync(new Dictionary<string, object>{
                { "dt", dt },
                { "total_objects", child_num },
                { "boundary_box", boundary_box_ndarray },
                { "corr_rate", corr_rate },
                { "damp", damp}
            });
        else Debug.LogError("Oh how could this be... _Compute_Graph_g_update missing!");

        if (_Compute_Graph_g_copy_to_nd != null)
            _Compute_Graph_g_copy_to_nd.LaunchAsync(new Dictionary<string, object>{
                { "total_objects", child_num },
                { "object_cm", object_cm_ndarray },
                { "object_rot", object_rot_ndarray }
            });
        else Debug.LogError("Oh how could this be... _Compute_Graph_g_copy_to_nd missing!");

        object_cm_ndarray.CopyToArray(cm);
        object_rot_ndarray.CopyToArray(rot);

        var sub_rot = new float[9];
        for (int i = 0; i < child_num; i++)
        {
            Array.Copy(rot, i*9, sub_rot, 0, 9);
            rigidCubes[i].box_rot = rot2quat(sub_rot);
            rigidCubes[i].box_pos = new Vector3(cm[i * 3 + 0], cm[i * 3 + 1], cm[i * 3 + 2]);
        }

        Runtime.Submit();
    }
}
