import taichi as ti
import random
import math


ti.init(arch=ti.vulkan)

# aux-------------------------------------------------------------
vec2 = ti.types.vector(2, ti.f32)
vec3 = ti.types.vector(3, ti.f32)
mat33 = ti.types.matrix(3, 3, ti.f32)

STATE_STATIC = 0
STATE_KINIMATIC = 1
STATE_DYNAMIC = 2

mat33_identity = mat33([[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]])
rad = math.pi / 6
mat33_xy_30 = mat33(
    [[ti.cos(rad), -ti.sin(rad), 0.0], [ti.sin(rad), ti.cos(rad), 0.0], [0.0, 0.0, 1.0]]
)

inf = 1e30
eps = 1e-6
# aux-------------------------------------------------------------


# global variable-------------------------------------------------
object_num = 0
particle_num = ti.field(ti.i32, shape=())
max_particle_num = 20000
max_object_num = 500
particle_radius = 0.05
particle_diameter = particle_radius * 2.0
particle_object_id = ti.field(ti.i32, max_particle_num)

x = ti.Vector.field(3, ti.f32, max_particle_num)
delta_x = ti.Vector.field(3, ti.f32, max_particle_num)
x_old = ti.Vector.field(3, ti.f32, max_particle_num)
x0 = ti.Vector.field(3, ti.f32, max_particle_num)
v = ti.Vector.field(3, ti.f32, max_particle_num)

# inv_m = ti.field(ti.f32, max_particle_num)
particle_sdf = ti.field(ti.f32, max_particle_num)
particle_sdf_grad = ti.Vector.field(3, ti.f32, max_particle_num)
object_rest_Cm = ti.Vector.field(3, ti.f32, max_object_num)
object_Cm = ti.Vector.field(3, ti.f32, max_object_num)
object_rot = ti.Matrix.field(3, 3, ti.f32, max_object_num)
object_rest_rot = ti.Matrix.field(3, 3, ti.f32, max_object_num)
object_A = ti.Matrix.field(3, 3, ti.f32, max_object_num)
object_state = ti.field(ti.i32, max_object_num)
object_friction_factor = ti.Vector.field(
    2, ti.f32, max_object_num
)  # [mu_s, mu_k]
object_restitution = ti.field(ti.f32, max_object_num)
object_begin = ti.field(ti.i32, max_object_num)
object_size = ti.field(ti.i32, max_object_num)
object_mass = ti.field(ti.f32, max_object_num)
# global variable-------------------------------------------------

# mock unity------------------------------------------------------
boundary_box = ti.Vector.ndarray(3, ti.f32, 2)
object_cm_to_host = ti.Vector.ndarray(3, ti.f32, max_object_num)
object_rot_to_host = ti.Matrix.ndarray(3, 3, ti.f32, max_object_num)
# mock unity------------------------------------------------------

# all symbols-----------------------------------------------------
sym_dt = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "dt", ti.f32)
sym_corr_rate = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "corr_rate", ti.f32)
sym_object_id = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "object_id", ti.i32)
sym_boundary_box = ti.graph.Arg(ti.graph.ArgKind.NDARRAY, 'boundary_box', ti.f32, field_dim=1, element_shape=(3,))
sym_center_x = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "center_x", ti.f32)
sym_center_y = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "center_y", ti.f32)
sym_center_z = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "center_z", ti.f32)
sym_half_extent = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "half_extent", ti.f32)
sym_state = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "state", ti.i32)
sym_total_objects = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "total_objects", ti.i32)
sym_object_cm = ti.graph.Arg(ti.graph.ArgKind.NDARRAY, 'object_cm', ti.f32, field_dim=1, element_shape=(3,))
sym_object_rot = ti.graph.Arg(ti.graph.ArgKind.NDARRAY, 'object_rot', ti.f32, field_dim=1, element_shape=(3,3))
sym_v_x = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "v_x", ti.f32)
sym_v_y = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "v_y", ti.f32)
sym_v_z = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "v_z", ti.f32)
sym_damp = ti.graph.Arg(ti.graph.ArgKind.SCALAR, "damp", ti.f32)
# all symbols-----------------------------------------------------

# init graph------------------------------------------------------------
# avoid these particles to be rendered
@ti.kernel
def init():
    particle_num[None] = 0
    for I in ti.grouped(x):
        x[I] = ti.Vector([1e20, 1e20, 1e20])

init_GraphBuilder = ti.graph.GraphBuilder()
init_GraphBuilder.dispatch(init)
init_graph = init_GraphBuilder.compile()

def init_mock():
    init_graph.run({})
# init graph------------------------------------------------------------



# add_box graph---------------------------------------------------------
@ti.func
def add_box_func(
    center,
    half_extent, # real length
    rotation,
    state,
    fric_factor,
    restitution,
    particle_num,
    object_id,
) -> ti.i32:
    n3 = ti.ceil(half_extent * 2 / particle_diameter)

    # Padding extent to a multiple of particle diameter
    # TODO: Is this reasonable from a user's view
    half_extent = n3 * particle_diameter / 2

    low_corner = center - half_extent
    high_corner = center + half_extent

    # !!!
    n3_with_border = int(n3 + vec3(1, 1, 1))
    new_particle_num = n3_with_border.x * n3_with_border.y * n3_with_border.z
    object_Cm[object_id] = vec3(0.0)

    for i, j, k in ti.ndrange(n3_with_border.x, n3_with_border.y, n3_with_border.z):
        local_id = i * n3_with_border.z * n3_with_border.y + j * n3_with_border.z + k
        global_id = local_id + particle_num
        pos = low_corner + ti.Vector([i, j, k]) * particle_diameter
        x0[global_id] = pos
        x[global_id] = x0[global_id]
        x_old[global_id] = x0[global_id]
        # inv_m[global_id] = 1.0 / particle_mass
        # inv_m[global_id] = 1.0 / particle_mass
        particle_object_id[global_id] = object_id


        # sdf------------------------------------------------------------
        # TODO: we need a better method to calculate sdf and sdf_grad
        dir = vec3(0.0)
        sign = vec3(0.0)
        min_dis = inf
        for d in ti.static(range(3)):
            signed_dis = 0.0
            if pos[d] - low_corner[d] > high_corner[d] - pos[d]:
                signed_dis = high_corner[d] - pos[d]
                sign[d] = 1
            else:
                signed_dis = low_corner[d] - pos[d]
                sign[d] = -1
            dir[d] = signed_dis
            min_dis = ti.min(min_dis, ti.abs(signed_dis))

        if min_dis < eps:
            # boundary particles
            particle_sdf[global_id] = 0.0
            for d in ti.static(range(3)):
                if ti.abs(dir[d]) > eps:
                    dir[d] = 0
                else:
                    dir[d] = 1.0 * sign[d]
        else:
            # if ti.abs(dir[0] - dir[1]) >= eps or ti.abs(dir[0] - dir[2]) >= eps or ti.abs(dir[1] - dir[2]) >= eps:
            # remain a dim if several signed distance equal the minimum component
            remain_dim = 0
            for d in ti.static(range(3)):
                if ti.abs(ti.abs(dir[d]) - min_dis) < eps:
                    remain_dim = d
                    # break # BUG: we must remove this break, otherwise we get a unexpected result
            for d in ti.static(range(3)):
                if d == remain_dim:
                    pass
                    # continue # BUG: we muse use pass instead of continue, otherwise we get a unexpected result
                else:
                    dir[d] = 0.0
            particle_sdf[global_id] = -dir.norm()

        particle_sdf_grad[global_id] = dir.normalized()
        # sdf------------------------------------------------------------


        x0[global_id] = rotation @ (x0[global_id] - center) + center
        x[global_id] = x0[global_id]
        x_old[global_id] = x0[global_id]

        particle_sdf_grad[global_id] = (
            rotation.inverse().transpose() @ particle_sdf_grad[global_id]
        )

        ti.atomic_add(
            # object_rest_Cm[object_num], particle_mass * x[global_id]
            object_rest_Cm[object_id], 1.0 * x[global_id]
        )
    object_rest_rot[object_id] = rotation
    object_state[object_id] = state
    object_friction_factor[object_id] = fric_factor
    object_restitution[object_id] = restitution
    object_begin[object_id] = particle_num
    object_size[object_id] = new_particle_num
    # object_mass[object_id] = particle_mass * new_particle_num
    object_mass[object_id] = 1.0 * new_particle_num
    object_rest_Cm[object_id] /= object_mass[object_id]

    return new_particle_num

@ti.kernel
def add_box_kernel(center_x: ti.f32, center_y: ti.f32, center_z: ti.f32, half_extent: ti.f32, state: ti.i32, object_num: ti.i32):
    new_particle_num = add_box_func(
        vec3(center_x, center_y, center_z),
        vec3(half_extent, half_extent, half_extent),
        mat33_identity,
        state,
        vec2(1, 1),
        1.0,
        particle_num[None],
        object_num,
    )
    particle_num[None] += new_particle_num


# TODO: Reduce optimization
@ti.func
def calc_single_object_cm_A_rot(object_id: ti.i32):

    object_Cm[object_id] = vec3(0.0)
    object_A[object_id] = mat33(0.0)

    object_begin_t = object_begin[object_id]
    object_end = object_begin[object_id] + object_size[object_id]

    for i in range(object_begin_t, object_end):
        # ti.atomic_add(object_Cm[object_id], 1.0 / inv_m[i] * x[i])
        ti.atomic_add(object_Cm[object_id], 1.0 * x[i])

    object_Cm[object_id] /= object_mass[object_id]
    for i in range(object_begin_t, object_end):
        # A
        q = x0[i] - object_rest_Cm[object_id]
        p = x[i] - object_Cm[object_id]
        ti.atomic_add(
            # object_A[object_id], 1.0 / inv_m[i] * p @ q.transpose()
            object_A[object_id], 1.0 * p.outer_product(q)
        )

    # rot
    object_rot[object_id], S = ti.polar_decompose(object_A[object_id])
    if all(abs(object_rot[object_id]) < eps):
        object_rot[object_id] = ti.Matrix.identity(ti.f32, 3)

@ti.kernel
def calc_single_object_cm_A_rot_kernel(object_id: ti.i32):
    calc_single_object_cm_A_rot(object_id)

add_box_GraphBuilder = ti.graph.GraphBuilder()

add_box_GraphBuilder.dispatch(add_box_kernel, sym_center_x, sym_center_y, sym_center_z, sym_half_extent, sym_state, sym_object_id)
add_box_GraphBuilder.dispatch(calc_single_object_cm_A_rot_kernel, sym_object_id)
add_box_graph = add_box_GraphBuilder.compile()


def add_box_mock(center_x: ti.f32, center_y: ti.f32, center_z: ti.f32, 
    half_extent: ti.f32, state: ti.i32, object_id: ti.i32):
    add_box_graph.run({
        'center_x' : center_x,
        'center_y' : center_y,
        'center_z' : center_z,
        'half_extent' : half_extent,
        'state' : state,
        'object_id' : object_id
    })
    return object_id + 1
# add_box graph---------------------------------------------------------


# update graph----------------------------------------------------------
@ti.func
def calc_all_object_cm_A_rot(object_num):
    part_num = particle_num[None]
    for i in range(object_num):
        object_Cm[i] = vec3(0.0)
        object_A[i] = mat33(0.0)

    for i in range(part_num):
        object_id = particle_object_id[i]
        # ti.atomic_add(object_Cm[object_id], 1.0 / inv_m[i] * x[i])
        ti.atomic_add(object_Cm[object_id], 1.0 * x[i])

    for i in range(object_num):
        object_Cm[i] /= object_mass[i]

    for i in range(part_num):
        # A
        object_id = particle_object_id[i]
        q = x0[i] - object_rest_Cm[object_id]
        p = x[i] - object_Cm[object_id]
        ti.atomic_add(
            # object_A[object_id], 1.0 / inv_m[i] * p @ q.transpose()
            object_A[object_id], 1.0 *  p.outer_product(q)
        )

    for i in range(object_num):
        # rot
        object_rot[i], S = ti.polar_decompose(object_A[i])
        if all(abs(object_rot[i]) < eps):
            object_rot[i] = ti.Matrix.identity(ti.f32, 3)

@ti.kernel
def semi_euler(h: ti.f32):
    gravity = ti.Vector([0.0, -9.8, 0.0])
    for i in range(particle_num[None]):
        if object_state[particle_object_id[i]] == STATE_STATIC:
            continue
        v[i] += h * gravity
        x_old[i] = x[i]
        x[i] += h * v[i]


@ti.kernel
def solve_constraints( object_num: ti.i32, corr_rate: ti.f32):
    part_num = particle_num[None]

    for i in range(part_num):
        delta_x[i] = vec3(0.0)

    for i in range(part_num):

        if object_state[particle_object_id[i]] == STATE_STATIC:
            continue

        for j in range(part_num):
            if particle_object_id[i] != particle_object_id[j]:
                pij = x[i] - x[j]
                pij_ = pij.norm()

                # if pij_ < particle_diameter or pij_ < ti.abs(
                if pij_ < particle_radius or pij_ < ti.abs(particle_sdf[i]) + ti.abs(particle_sdf[j]):
                    # if pij_ < particle_diameter or pij_ < ti.max(ti.abs(particle_sdf[i]), ti.abs(particle_sdf[j])):

                    nij = vec3(0.0)
                    d = 0.0

                    nij = pij
                    d = pij_ - (
                        ti.abs(particle_sdf[i]) + ti.abs(particle_sdf[j])
                    )

                    new_nij = nij
                    # boundary particles ???
                    if -particle_sdf[i] < eps and -particle_sdf[j] < eps:
                        if pij.dot(nij) < 0.0:
                            new_nij = pij - 2 * pij.dot(nij) * nij
                        else:
                            new_nij = pij
                        d = pij_ - particle_diameter

                    delta_x[i] += -0.5 * d * new_nij / (new_nij.norm() + 0.1)

                    if (
                        -particle_sdf[i] < eps
                        and -particle_sdf[j] < eps
                        and pij.dot(nij) >= 0.0
                    ):
                        p = delta_x[i]
                        vel = p
                        n = -pij
                        vn = n.dot(vel) * n
                        vt = vel - vn
                        stress = d * 1.0
                        mu_s_i = object_friction_factor[
                            particle_object_id[i]
                        ][0]
                        mu_k_i = object_friction_factor[
                            particle_object_id[i]
                        ][1]
                        mu_s_j = object_friction_factor[
                            particle_object_id[j]
                        ][0]
                        mu_k_j = object_friction_factor[
                            particle_object_id[j]
                        ][1]
                        if vt.norm() < stress * mu_s_i * mu_s_j:
                            p -= vt
                        else:
                            delta = vt * ti.min(
                                stress * mu_k_i * mu_k_j / vt.norm(), 1.0
                            )
                            p -= delta
                        delta_x[i] = p

    for i in range(part_num):
        x[i] += delta_x[i]

    # TODO: error here!!!
    calc_all_object_cm_A_rot(object_num)

    for i in range(part_num):
        obj_id = particle_object_id[i]
        goal = object_Cm[obj_id] + object_rot[obj_id] @ (
            x0[i] - object_rest_Cm[obj_id]
        )
        # corr = (goal - x[i]) * 0.8
        corr = (goal - x[i]) * corr_rate
        x[i] += corr


@ti.kernel
def collision_response( boundary_box: ti.types.ndarray()):
    for i in range(particle_num[None]):
        if object_state[particle_object_id[i]] == STATE_STATIC:
            continue
        p = x[i]
        dir = vec3(0.0)
        collision_normal = ti.Vector([0.0, 0.0, 0.0])
        for j in ti.static(range(3)):
            if p[j] < boundary_box[0][j]:
                p[j] = boundary_box[0][j]
                collision_normal[j] += -1.0
                dir[j] += boundary_box[0][j] - x[i][j]
        for j in ti.static(range(3)):
            if p[j] > boundary_box[1][j] and j != 1:
                p[j] = boundary_box[1][j]
                collision_normal[j] += 1.0
                dir[j] += boundary_box[1][j] - x[i][j]

        # velocity
        collision_normal_length = collision_normal.norm()
        if collision_normal_length > eps:
            collision_normal /= collision_normal_length
            vel = p - x_old[i]
            vn = collision_normal.dot(vel) * collision_normal
            vt = vel - vn
            # use dir.norm() as stress
            stress = dir.norm() * 1.0
            mu_s = object_friction_factor[particle_object_id[i]][0]
            mu_k = object_friction_factor[particle_object_id[i]][1]
            if vt.norm() < stress * mu_s:
                p -= vt
            else:
                delta = vt * ti.min(stress * mu_k / vt.norm(), 1.0)
                p -= delta
            v[i] -= (
                1.0 + object_restitution[particle_object_id[i]]
            ) * vn
            x[i] = p


@ti.kernel
def update_velocities( h: ti.f32, damp:ti.f32):
    for i in range(particle_num[None]):
        dx = x[i] - x_old[i]
        if dx.norm() < eps:
            v[i] = vec3(0.0)
            x[i] = x_old[i]
        else:
            v[i] = dx / h
        v[i] *= damp
        

update_GraphBuilder = ti.graph.GraphBuilder()

max_iter = 30
update_GraphBuilder.dispatch(semi_euler, sym_dt)
for i in range(30):
    update_GraphBuilder.dispatch(solve_constraints, sym_total_objects, sym_corr_rate)
    update_GraphBuilder.dispatch(collision_response, sym_boundary_box)
update_GraphBuilder.dispatch(update_velocities, sym_dt, sym_damp)

update_graph = update_GraphBuilder.compile()

def update_mock(object_num: ti.i32, dt:ti.f32, corr_rate:ti.f32, damp:ti.f32):
    update_graph.run({
        'total_objects': object_num,
        'boundary_box': boundary_box,
        'dt': dt,
        'corr_rate' : corr_rate,
        'damp': damp
    })
# update graph----------------------------------------------------------

# set initial velocity -------------------------------------------------
@ti.kernel
def set_ini_velocity(object_id:ti.i32, v_x:ti.f32, v_y:ti.f32, v_z:ti.f32):
    object_begin_t = object_begin[object_id]
    object_end = object_begin[object_id] + object_size[object_id]

    v_new = ti.Vector([v_x, v_y, v_z])
    for i in range(object_begin_t, object_end):
        v[i] = v_new

set_ini_velocity_GraphBuilder = ti.graph.GraphBuilder()
set_ini_velocity_GraphBuilder.dispatch(set_ini_velocity, sym_object_id, sym_v_x, sym_v_y, sym_v_z)
set_ini_velocity_graph = set_ini_velocity_GraphBuilder.compile()

def set_ini_velocity_mock(object_id:ti.i32, v_x, v_y, v_z):
    set_ini_velocity_graph.run({
        'object_id':object_id,
        'v_x':v_x,
        'v_y':v_y,
        'v_z':v_z,
    })

# set initial velocity -------------------------------------------------


# copy_to_ndarray graph-------------------------------------------------
@ti.kernel
def copy_to_nd(total_objects: ti.i32, 
    object_cm_to_host: ti.types.ndarray(), 
    object_rot_to_host: ti.types.ndarray()):
    for i in range(total_objects):
        object_cm_to_host[i] = object_Cm[i]
        object_rot_to_host[i] = object_rot[i]

copy_to_nd_GraphBuilder = ti.graph.GraphBuilder()
copy_to_nd_GraphBuilder.dispatch(copy_to_nd, sym_total_objects, sym_object_cm, sym_object_rot)
copy_to_end_graph = copy_to_nd_GraphBuilder.compile()

def copy_to_nd_mock(total_objects: ti.i32):
    copy_to_end_graph.run({
        'total_objects': total_objects,
        'object_cm' : object_cm_to_host,
        'object_rot' : object_rot_to_host,
    })
# copy_to_ndarray graph-------------------------------------------------

# reset graph-----------------------------------------------------------

@ti.kernel
def reset_all(total_objects: ti.i32):
    part_num = particle_num[None]
    for i in range(part_num):
        v[i] = ti.Vector([0,0,0])
        x[i] = x0[i]
        x_old[i] = x0[i]   

    for i in range(total_objects):
        object_Cm[i] = object_rest_Cm[i]     
        object_rot[i] = object_rest_rot[i]

reset_all_GraphBuilder = ti.graph.GraphBuilder()
reset_all_GraphBuilder.dispatch(reset_all, sym_total_objects)
reset_all_graph = reset_all_GraphBuilder.compile()

def reset_all_mock(total_objects: ti.i32):
    reset_all_graph.run({
        'total_objects': total_objects
    })
# reset graph-----------------------------------------------------------




# aux---------------------------------------------------------------------
@ti.kernel
def convert_to_field(x: ti.types.ndarray(field_dim=1), y: ti.template()):
    # this function convert ti.ndarray to ti.field
    # rendering part need this, as scene.particles() only accept ti.field
    for I in ti.grouped(x):
        y[I] = x[I]

def save_aot_foo():
    # save aot artefact
    # Notion: calling this makes 'running cgraphs at python runtime' generates error
    mod = ti.aot.Module(ti.vulkan)
    mod.add_graph('init', init_graph)
    mod.add_graph('add_box', add_box_graph)
    mod.add_graph('update', update_graph)
    mod.add_graph('copy_to_nd', copy_to_end_graph)
    mod.add_graph('set_ini_velocity', set_ini_velocity_graph)
    mod.add_graph('reset_all', reset_all_graph)
    mod.archive("Assets/Resources/TaichiModules/rigid_scene.cgraph.tcm")
    print('AOT done')
# aux end------------------------------------------------------------------

# data preparing
import numpy as np
boundary_box_np = np.ndarray((2, 3))
boundary_box_np[0] = np.array([-1.0, 0.0, -1.0]) * 1000
boundary_box_np[1] = np.array([1.0, 1.0, 1.0]) * 1000
boundary_box.from_numpy(boundary_box_np)
# boundary_box_field = ti.Vector.field(3, ti.f32, 2)
# end preparing



#--------------------------------------------------------------------------
#--------------------------------------------------------------------------
#--------------------------------------------------------------------------

center_obj = ti.Vector.field(3, ti.f32, max_object_num)


save_aot = True

if __name__ == '__main__':

    if save_aot: 
        save_aot_foo()
        exit(0)


    init_mock()
    object_id = 0

    # generate_friction_scene
    # object_id = add_box_mock(
    #     center_x=0.0, 
    #     center_y=2.2, 
    #     center_z=0.0,
    #     half_extent=0.3,
    #     state=STATE_DYNAMIC,
    #     object_id=object_id
    # )
    # object_id = add_box_mock(
    #     center_x=-0.3, 
    #     center_y=2.8, 
    #     center_z=0.0,
    #     half_extent=0.2,
    #     state=STATE_DYNAMIC,
    #     object_id=object_id
    # )
    # object_id = add_box_mock(
    #     center_x=-0.3, 
    #     center_y=3.4, 
    #     center_z=0.0,
    #     half_extent=0.2,
    #     state=STATE_DYNAMIC,
    #     object_id=object_id
    # )
    # object_id = add_box_mock(
    #     center_x=0.3, 
    #     center_y=2.8, 
    #     center_z=0.0,
    #     half_extent=0.2,
    #     state=STATE_DYNAMIC,
    #     object_id=object_id
    # )
    # object_id = add_box_mock(
    #     center_x=0.3, 
    #     center_y=3.4, 
    #     center_z=0.0,
    #     half_extent=0.2,
    #     state=STATE_DYNAMIC,
    #     object_id=object_id
    # )
    # object_id = add_box_mock(
    #     center_x=0.0, 
    #     center_y=0.7, 
    #     center_z=0.0,
    #     half_extent=0.5,
    #     state=STATE_STATIC,
    #     object_id=object_id
    # )

    window = ti.ui.Window("Shape matching - Rigid", (1200, 1200), vsync=True)
    scene = ti.ui.Scene()
    camera = ti.ui.make_camera()
    # camera.position(0.5, 1.0, 2.0)
    camera.position(-2.84373496, 3.19549618, 5.19180527)
    camera.lookat(0.0, 0.0, 0.0)
    camera.up(0.0, 1.0, 0.0)
    camera.fov(80)
    scene.set_camera(camera)
    canvas = window.get_canvas()
    movement_speed = 0.05

    pause = True
    frame = 0

    idx = 0
    while window.running:
        
        camera.track_user_inputs(window, movement_speed=movement_speed, hold_key=ti.ui.LMB)
        scene.set_camera(camera)
            
        if window.is_pressed(' '):
            pause = False

        if not pause and window.is_pressed("u"):
            set_ini_velocity_mock(0, 0, 5, 0)

        if not pause and window.is_pressed("r"):
            reset_all_mock(object_id)

        if not pause and window.is_pressed("t"):
            object_id = add_box_mock(
                center_x=0.0, 
                center_y=5.2, 
                center_z=0.0,
                half_extent=0.3,
                state=STATE_DYNAMIC,
                object_id=object_id
            )
            set_ini_velocity_mock(object_id=object_id-1, v_x=10, v_y=0, v_z=0)

        if not pause:
            update_mock(object_num=object_id, dt=1.0/60.0, corr_rate=1.0, damp=0.98)
            copy_to_nd_mock(object_id)

        if window.is_pressed("q"):
            pass

        scene.point_light((1.0, 1.0, 3.0), color=(1.0, 1.0, 1.0))
        scene.point_light((1.0, 1.0, 5.0), color=(1.0, 1.0, 1.0))
        scene.point_light((1.0, 1.0, 7.0), color=(1.0, 1.0, 1.0))
        scene.particles(
            x,
            radius=particle_radius,
            color=(0.5,0.2,0.3),
        )

        convert_to_field(object_cm_to_host, center_obj)
        # draw particle center to check if copy_to_nd works correctly
        scene.particles(
            center_obj,
            radius=particle_radius*3,
            color=(0.3,0.2,0.6),
        )
        # scene.particles(boundary_box_field, radius=particle_radius, color=(1.0, 0.0, 0.0))
        # scene.particles(x_debug, radius=particle_radius / 2, color=(0.0, 1.0, 0.0))
        canvas.scene(scene)
        window.show()
        frame += 1
        # break
