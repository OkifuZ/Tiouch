import numpy as np
import taichi as ti

ti.init(debug=True)

rest_center = ti.Vector.ndarray(3, ti.f32, ())
rest_pose_np = np.array([1.0,0,0])
rest_center.from_numpy(rest_pose_np)


@ti.kernel
def pt(x:ti.types.ndarray()):
    print(x[None])

pt(rest_center)