import taichi as ti
import numpy as np


ti.init(arch=ti.vulkan)
n = 11
n_particles = n ** 3
sub_iter = 10

# Following vars shall be copied from Unity.
# to MOCK Unity, we declare it here as global vars 
# ---------------------------------------------------
x = ti.Vector.ndarray(3, ti.f32, n_particles) # vertices AFTER model transformation!
x_old = ti.Vector.ndarray(3, ti.f32, n_particles)
x0 = ti.Vector.ndarray(3, ti.f32, n_particles)
v = ti.Vector.ndarray(3, ti.f32, n_particles)
# inv_m = ti.ndarray(ti.f32, n_particles) # hack this for now
rest_center = ti.Vector.ndarray(3, ti.f32, ())
dt:ti.f32 = 0.01
corr_rate:ti.f32 = 0.1
ground_y:ti.f32 = -1.0
damp:ti.f32 = 0.995

# ---------------------------------------------------


# This is no longer needed, Unity shall do this
# @ti.func
# def init_pos(vertices: ti.types.ndarray(field_dim=1), vertices_old: ti.types.ndarray(field_dim=1), vertices0: ti.types.ndarray(field_dim=1)):
#     for i in vertices:
#         # x[i] = vertices[i]
#         vertices0[i] = x[i]
#         vertices_old[i] = x[i]
#         # inv_m[i] = 1.0
#     # for i, j, k in ti.ndrange(n, n, n):
#     #     idx = i * n ** 2 + j * n + k
#     #     x0[idx] = ti.Vector([i, j, k]) * 0.1
#     #     x[idx] = x0[idx]
#     #     x_old[idx] = x0[idx]
#     #     inv_m[idx] = 1.0


# TODO: Reduce optimization
@ti.func
def compute_cos(vertices: ti.types.ndarray(field_dim=1)):
    sum_m = 0.0
    cm = ti.Vector([0.0, 0.0, 0.0])
    for i in vertices:
        # mass = 1.0 / inv_m[i]
        # cm += mass * vertices[i]
        cm += 1.0 * vertices[i] # hack mass
        sum_m += 1.0
    cm /= sum_m
    return cm


# we need to dispatch this kernel
@ti.kernel
def init_ti(vertices: ti.types.ndarray(field_dim=1), vertices0: ti.types.ndarray(field_dim=1), vertices_old: ti.types.ndarray(field_dim=1), rest_center: ti.types.ndarray()):
    # just copy
    for i in vertices:
        pos = vertices[i] + rest_center[None]
        vertices[i] = pos
        vertices0[i] = pos
        vertices_old[i] = pos
        # vertices0[i] = vertices[i]
        # vertices_old[i] = vertices[i]
    
    # init_pos(vertices) # this step done in unity
    # restCm[None] = compute_cos(vertices)


# we need to dispatch this kernel
@ti.kernel
def semi_euler(dt: ti.f32, vertices: ti.types.ndarray(field_dim=1), vertices_old: ti.types.ndarray(field_dim=1), velocity: ti.types.ndarray(field_dim=1)):
    gravity = ti.Vector([0.0, -9.8, 0.0])
    for i in vertices:
        velocity[i] += dt * gravity
        vertices_old[i] = vertices[i]
        vertices[i] += dt * velocity[i]
    # gravity = ti.Vector([0.0, -9.8, 0.0])
    # for i in ti.ndrange(n, n, n):
    #     idx = i * n ** 2 + j * n + k
    #     v[idx] += h * gravity
    #     x_old[idx] = x[idx]
    #     x[idx] += h * v[idx]


# we need to dispatch this kernel
@ti.kernel
def solve_constraints(vertices: ti.types.ndarray(field_dim=1), vertices0: ti.types.ndarray(field_dim=1), rest_center: ti.types.ndarray(), corr_rate:ti.f32):
    center = compute_cos(vertices=vertices)
    A = ti.Matrix([[0.0, 0.0, 0.0], [0.0, 0.0, 0.0], [0.0, 0.0, 0.0]])
    for i in vertices:
        q = vertices0[i] - rest_center[None]
        p = vertices[i] - center
        # A += 1.0 * p @ q.transpose()
        A += 1.0 * p.outer_product(q)
        # A += 1.0 / inv_m[i] * p @ q.transpose()
    R, S = ti.polar_decompose(A)
    for i in vertices:
        goal = center + R @ (vertices0[i] - rest_center[None])
        corr = (goal - vertices[i]) * corr_rate
        vertices[i] += corr
    # # compute center of mass
    # center = compute_cos()
    # # A
    # A = ti.Matrix([[0.0, 0.0, 0.0], [0.0, 0.0, 0.0], [0.0, 0.0, 0.0]])
    # for i, j, k in ti.ndrange(n, n, n):
    #     idx = i * n ** 2 + j * n + k
    #     q = x0[idx] - restCm[None]
    #     p = x[idx] - cm
    #     A += 1.0 / inv_m[idx] * p @ q.transpose()
    # R, S = ti.polar_decompose(A)
    # for i, j, k in ti.ndrange(n, n, n):
    #     idx = i * n ** 2 + j * n + k
    #     goal = cm + R @ (x0[idx] - restCm[None])
    #     corr = (goal - x[idx]) * 0.1
    #     x[idx] += corr


# we need to dispatch this kernel
@ti.kernel
def collision_response(vertices: ti.types.ndarray(field_dim=1), ground_y: ti.f32):
    for i in vertices:
        if vertices[i][1] < ground_y:
            vertices[i][1] = ground_y
    # for i, j, k in ti.ndrange(n, n, n):
    #     idx = i * n ** 2 + j * n + k
    #     for e in ti.static(range(3)):
    #         if x[idx][e] < -1.0:
    #             x[idx][e] = -1.0


# we need to dispatch this kernel
@ti.kernel
def update_velocities(dt: ti.f32, vertices: ti.types.ndarray(field_dim=1), vertices_old: ti.types.ndarray(field_dim=1), velocity: ti.types.ndarray(field_dim=1), damp:ti.f32):
    for i in vertices:
        velocity[i] = (vertices[i] - vertices_old[i]) / dt
        velocity[i] *= damp
    # for i, j, k in ti.ndrange(n, n, n):
    #     idx = i * n ** 2 + j * n + k
    #     v[idx] = (x[idx] - x_old[idx]) / h


@ti.kernel
def convert_to_field(x: ti.types.ndarray(field_dim=1), y: ti.template()):
    # this function convert ti.ndarray to ti.field
    # rendering part need this, as scene.particles() only accept ti.field
    for I in ti.grouped(x):
        y[I] = x[I]


# to MOCK the behavior that Unity load vertices of model to x
x_np = np.ndarray((n_particles, 3))
offset = (n-1) / 2
for i in range(0, n):
    for j in range(0, n):
        for k in range(0, n):
            idx = i * n ** 2 + j * n + k
            x_np[idx] = (np.array([i, j, k]) - offset) * 0.1 
x.from_numpy(x_np)
# x0.from_numpy(x_np)
# x_old.from_numpy(x_np)

rest_pose_np = np.array([0,0,0])
rest_center.from_numpy(rest_pose_np)


# create graph arguments
# name is of no importance, but duplicating them makes it easier to use
sym_vertices = ti.graph.Arg(ti.graph.ArgKind.NDARRAY, 'vertices', ti.f32, field_dim=1, element_shape=(3,))
sym_vertices0 = ti.graph.Arg(ti.graph.ArgKind.NDARRAY, 'vertices0', ti.f32, field_dim=1, element_shape=(3,))
sym_vertices_old = ti.graph.Arg(ti.graph.ArgKind.NDARRAY, 'vertices_old', ti.f32, field_dim=1, element_shape=(3,))
sym_velocity = ti.graph.Arg(ti.graph.ArgKind.NDARRAY, 'velocity', ti.f32, field_dim=1, element_shape=(3,))
sym_rest_center = ti.graph.Arg(ti.graph.ArgKind.NDARRAY, 'rest_center', ti.f32, field_dim=0, element_shape=(3,))
sym_dt = ti.graph.Arg(ti.graph.ArgKind.SCALAR, 'dt', ti.f32)
sym_corr_rate = ti.graph.Arg(ti.graph.ArgKind.SCALAR, 'corr_rate', ti.f32)
sym_damp = ti.graph.Arg(ti.graph.ArgKind.SCALAR, 'damp', ti.f32)
sym_ground_y = ti.graph.Arg(ti.graph.ArgKind.SCALAR, 'ground_y', ti.f32)

# create compute graphs
# the order of args must be aligned with function defination's
g_init_builder = ti.graph.GraphBuilder()
g_init_builder.dispatch(init_ti, sym_vertices, sym_vertices0, sym_vertices_old, sym_rest_center)
g_init = g_init_builder.compile()

g_update_builder = ti.graph.GraphBuilder()
g_update_builder.dispatch(semi_euler, sym_dt, sym_vertices, sym_vertices_old, sym_velocity)
for _ in range(sub_iter):
    g_update_builder.dispatch(solve_constraints, sym_vertices, sym_vertices0, sym_rest_center, sym_corr_rate)
    g_update_builder.dispatch(collision_response, sym_vertices, sym_ground_y)
g_update_builder.dispatch(update_velocities, sym_dt, sym_vertices, sym_vertices_old, sym_velocity, sym_damp)
g_update = g_update_builder.compile()


def save_aot_foo():
    # save aot artefact
    # Notion: calling this makes 'running cgraphs at python runtime' generates error
    mod = ti.aot.Module(ti.vulkan)
    mod.add_graph('init', g_init)
    mod.add_graph('update', g_update)
    mod.archive("Assets/Resources/TaichiModules/rigid.cgraph.tcm")
    print('AOT done')


def init():
    g_init.run({
        'vertices': x,
        'vertices0': x0,
        'vertices_old': x_old,
        'rest_center' : rest_center
    })

def update():
    g_update.run({
        'dt': dt,
        'vertices': x,
        'vertices_old': x_old,
        'vertices0': x0,
        'velocity': v,
        'rest_center' : rest_center,
        'corr_rate': corr_rate,

        'damp': damp,
        'ground_y': ground_y
    })


save_aot = False

if __name__ == '__main__':
    if save_aot:
        save_aot_foo()
    else:
        window = ti.ui.Window("Shape matching - Rigid", (800, 800))
        scene = ti.ui.Scene()
        camera = ti.ui.make_camera()
        camera.position(2.0, 2.0, 2.0)
        camera.lookat(0, 0, 0)
        camera.up(0.0, 1.0, 0.0)
        camera.fov(80)
        scene.set_camera(camera)
        canvas = window.get_canvas()

        x_field = ti.Vector.field(3, dtype=ti.f32, shape=n_particles)

        init()
        while window.running:
            update()
            convert_to_field(x, x_field)

            scene.point_light((1.0, 1.0, 1.0), color=(1.0, 1.0, 1.0))
            scene.particles(x_field, radius=0.05, color=(1.0, 0.0, 0.0))
            canvas.scene(scene)
            window.show()
