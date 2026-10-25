import numpy as np, trimesh, os
from geom import *
from geom import ORIENT
from tex import make_materials
M = make_materials()
OUT = 'models'; os.makedirs(OUT, exist_ok=True)
mm = 1e-3
SHAFT_L = 330*mm      # standard working length
HUB = SHAFT_L + 6*mm  # where shaft meets rotation knob

def cyl(r, y0, y1, mat, seg=24, bevel=0.0):
    b = bevel
    prof = [(0,y0),(0,y0),(r-b,y0),(r-b,y0),(r,y0+b),(r,y0+b),(r,y1-b),(r,y1-b),(r-b,y1),(r-b,y1),(0,y1)]
    return lathe(prof, mat, seg)

def shaft(r, insulated=True, length=SHAFT_L, y0=0.0):
    return cyl(r, y0, y0+length, M['insul'] if insulated else M['steel'], seg=24, bevel=0.25*mm)

# ---------------------------------------------------------------- ring-handle body
def handle_assembly(scene, parent, cautery=True, knob_r=9*mm, ratchet=True):
    """Rotation knob, housing, fixed (thumb) ring, movable (finger) ring as its own pivot node."""
    knob = lathe([(0,HUB),(0,HUB),(4*mm,HUB),(4*mm,HUB),(knob_r*0.8,HUB+3*mm),(knob_r,HUB+5*mm),
                  (knob_r,HUB+13*mm),(knob_r*0.85,HUB+16*mm),(0,HUB+16*mm)], M['handle'], seg=14)  # 14 flats = grip flutes
    scene.add_geometry(knob, node_name='rotation_knob', parent_node_name=parent)
    band = lathe([(knob_r+0.1*mm,HUB+8*mm),(knob_r+0.1*mm,HUB+10*mm)], M['accent'], seg=14)
    scene.add_geometry(band, node_name='knob_band', parent_node_name=parent)
    hy = HUB+16*mm
    housing = sweep_rect(arc_path([0,hy,0],[0,hy+42*mm,0],0,n=2), 16*mm, 14*mm, M['handle'], up=(0,0,1))
    scene.add_geometry(housing, node_name='handle_housing', parent_node_name=parent)
    # fixed thumb handle: from housing down-back to thumb ring
    fixed = sweep_rect(np.array([[-6*mm,hy+30*mm,0],[-40*mm,hy+44*mm,0],[-72*mm,hy+60*mm,0]]),
                       [12*mm,9*mm,8*mm], 9*mm, M['handle'])
    scene.add_geometry(fixed, node_name='fixed_handle', parent_node_name=parent)
    scene.add_geometry(tube(ring_path(-86*mm, hy+68*mm, 15*mm, 12*mm, n=20), 3.8*mm, M['handle'], seg=10, closed=True),
                       node_name='thumb_ring', parent_node_name=parent)
    # movable finger handle, pivot at housing so it can be animated (rotate about Z)
    piv = np.array([-4*mm, hy+10*mm, 0])
    mv = sweep_rect(np.array([[0,0,0],[-35*mm,-6*mm,0],[-66*mm,-10*mm,0]]), [11*mm,9*mm,8*mm], 9*mm, M['handle'])
    ring = tube(ring_path(-80*mm, -14*mm, 13*mm, 11*mm, n=20), 3.8*mm, M['handle'], seg=10, closed=True)
    scene.graph.update(frame_to='movable_handle_pivot', frame_from=parent, matrix=T(*piv))
    scene.add_geometry(trimesh.util.concatenate([mv, ring]), node_name='movable_handle', parent_node_name='movable_handle_pivot')
    if ratchet:
        r = sweep_rect(arc_path([-60*mm,hy+3*mm,0],[-58*mm,hy+50*mm,0],-6*mm,n=6), 3*mm, 2.5*mm, M['steel'])
        scene.add_geometry(r, node_name='ratchet_bar', parent_node_name=parent)
    if cautery:  # monopolar connector post on top of housing
        post = lathe([(0,0),(0,0),(2.4*mm,0),(2.4*mm,0),(2.4*mm,9*mm),(2.4*mm,9*mm),(1.2*mm,9*mm),(1.2*mm,9*mm),(1.2*mm,16*mm),(0,16.5*mm)], M['steel'], seg=12)
        scene.add_geometry(post, node_name='cautery_post', parent_node_name=parent, transform=T(7*mm,hy+30*mm,0)@rotZ(-60))

def new_scene(name):
    s = trimesh.Scene(); s.graph.update(frame_to=name, frame_from=s.graph.base_frame); return s

def jaw_pair(scene, parent, make_jaw, hinge_y):
    """Upper/lower jaws as separate nodes pivoting at the hinge (rotate about X to open)."""
    for side, sgn in (('upper',1),('lower',-1)):
        node = f'jaw_{side}_pivot'
        scene.graph.update(frame_to=node, frame_from=parent, matrix=T(0,hinge_y,0))
        scene.add_geometry(make_jaw(sgn), node_name=f'jaw_{side}', parent_node_name=node)
    clevis = cyl(2.5*mm, hinge_y-1*mm, hinge_y+4*mm, M['steel'], bevel=0.3*mm)
    scene.add_geometry(clevis, node_name='clevis', parent_node_name=parent)

JAW_L = 20*mm
def maryland_jaw(sgn):
    t = np.linspace(0,1,9)
    path = np.c_[2.6*mm*t**2, -JAW_L*t, sgn*1.05*mm*np.ones_like(t)]   # curved toward +X
    w = 4.6*mm*(1-t) + 1.6*mm*t
    h = 2.1*mm*(1-t) + 1.1*mm*t
    return sweep_rect(path, w, h, M['steel'], up=(0,0,1))

def fenestrated_jaw(sgn):
    z = sgn*1.1*mm; parts=[]
    parts.append(sweep_rect([[0,0,z],[0,-4*mm,z]], 4.6*mm, 2.2*mm, M['steel']))
    for x in (-1.75*mm, 1.75*mm):
        parts.append(sweep_rect([[x*1.0,-4*mm,z],[x*0.85,-17*mm,z]], 1.1*mm, 2.0*mm, M['steel']))
    tip = np.linspace(0,1,6)
    parts.append(sweep_rect(np.c_[np.zeros(6), -17*mm-3*mm*tip, z*np.ones(6)], 3.8*mm*(1-0.45*tip), 2.0*mm*(1-0.3*tip), M['steel']))
    return trimesh.util.concatenate(parts)

def scissor_blade(sgn):
    t = np.linspace(0,1,9)
    path = np.c_[2.2*mm*t**2, -16*mm*t, sgn*0.55*mm*np.ones_like(t)]
    return sweep_rect(path, 4.4*mm*(1-t)+0.5*mm*t, 1.0*mm, M['steel'])

def clip_jaw(sgn):
    t = np.linspace(0,1,6)
    path = np.c_[np.zeros(6), -22*mm*t, sgn*2.2*mm*np.ones(6)]
    a = sweep_rect(path, 7.5*mm*(1-0.35*t), 3.6*mm*(1-0.3*t), M['steel'])
    # clip channel lip
    b = sweep_rect([[0,-2*mm,sgn*0.25*mm],[0,-21*mm,sgn*0.25*mm]], 2.6*mm, 0.5*mm, M['steel'])
    return trimesh.util.concatenate([a,b])

def ring_instrument(name, jaw_fn, shaft_r=2.5*mm, cautery=True, hinge_y=JAW_L, knob_r=9*mm, ratchet=True):
    s = new_scene(name)
    s.add_geometry(shaft(shaft_r, insulated=True, length=HUB-hinge_y-3*mm, y0=hinge_y+3*mm), node_name='shaft', parent_node_name=name)
    jaw_pair(s, name, jaw_fn, hinge_y)
    handle_assembly(s, name, cautery=cautery, knob_r=knob_r, ratchet=ratchet)
    return s

def l_hook():
    name='l_hook_electrode_5mm'; s=new_scene(name)
    # exposed tip: wire 1.2mm going down then L-bend 5mm
    path = np.array([[0,22*mm,0],[0,6*mm,0],[0,1.6*mm,0],[0.4*mm,0.3*mm,0],[1.6*mm,0,0],[5.5*mm,0,0]])
    s.add_geometry(tube(path, 0.6*mm, M['steel'], seg=10), node_name='hook_tip', parent_node_name=name)
    taper = lathe([(0.6*mm,20*mm),(0.6*mm,20*mm),(1.5*mm,22*mm),(2.5*mm,26*mm),(2.5*mm,26*mm)], M['insul'], seg=20)
    s.add_geometry(taper, node_name='insulation_taper', parent_node_name=name)
    s.add_geometry(shaft(2.5*mm, True, length=SHAFT_L-26*mm, y0=26*mm), node_name='shaft', parent_node_name=name)
    y=SHAFT_L
    grip = lathe([(0,y),(0,y),(4*mm,y),(6*mm,y+8*mm),(7*mm,y+30*mm),(7.5*mm,y+70*mm),(6*mm,y+95*mm),(4*mm,y+100*mm),(4*mm,y+100*mm),(0,y+100*mm)], M['handle'], seg=16)
    s.add_geometry(grip, node_name='pencil_grip', parent_node_name=name)
    for i,(c,yy) in enumerate(((M['accent'], y+40*mm),(M['handle'], y+54*mm))):  # cut/coag buttons
        b = sweep_rect([[7.2*mm,yy,0],[7.2*mm,yy+9*mm,0]], 1.6*mm, 6*mm, M['accent'] if i==0 else M['insul'], up=(1,0,0))
        s.add_geometry(b, node_name=['cut_button','coag_button'][i], parent_node_name=name)
    pin = lathe([(0,y+100*mm),(1.2*mm,y+100*mm),(1.2*mm,y+115*mm),(0,y+115.5*mm)], M['steel'], seg=12)
    s.add_geometry(pin, node_name='connector_pin', parent_node_name=name)
    return s

def suction_irrigator():
    name='suction_irrigator_5mm'; s=new_scene(name)
    s.add_geometry(cyl(2.5*mm, 0, 30*mm, M['suction_tip'], bevel=0.4*mm), node_name='perforated_tip', parent_node_name=name)
    s.add_geometry(cyl(2.5*mm, 30*mm, SHAFT_L, M['steel'], bevel=0.0), node_name='shaft', parent_node_name=name)
    y=SHAFT_L
    body = sweep_rect([[0,y,0],[0,y+70*mm,0]], 22*mm, 18*mm, M['handle'])
    s.add_geometry(body, node_name='valve_body', parent_node_name=name)
    for i,yy in enumerate((y+18*mm, y+48*mm)):   # trumpet pistons on top (+X)
        p = lathe([(0,0),(0,0),(5*mm,0),(5*mm,14*mm),(7*mm,14*mm),(7*mm,14*mm),(7*mm,18*mm),(6*mm,19*mm),(0,19*mm)],
                  M['accent'] if i==0 else M['insul'], seg=16)
        s.add_geometry(p, node_name=['irrigation_valve','suction_valve'][i], parent_node_name=name, transform=T(11*mm,yy,0)@rotZ(-90))
    for i,yy in enumerate((y+18*mm, y+48*mm)):  # tubing barbs toward handle (-X)
        barb = lathe([(0,0),(0,0),(3*mm,0),(3*mm,0),(3*mm,8*mm),(4*mm,9*mm),(2.8*mm,13*mm),(4*mm,14*mm),(2.8*mm,18*mm),(0,18*mm)], M['clear'], seg=14)
        s.add_geometry(barb, node_name=f'tube_barb_{i}', parent_node_name=name, transform=T(-11*mm,yy,0)@rotZ(90))
    return s

def laparoscope():
    name='laparoscope_10mm_30deg'; s=new_scene(name); R=5*mm; tan30=np.tan(np.radians(30))
    def bevel(V):  # 30 deg oblique face: shift distal ring along y by x*tan30
        V=V.copy(); m = V[:,1] < 0.5*mm; V[m,1] += (V[m,0]+R)*tan30; return V
    shaftm = lathe([(0,0),(0,0),(R,0),(R,0),(R,SHAFT_L)], M['steel'], seg=32, xform=bevel)
    s.add_geometry(shaftm, node_name='shaft', parent_node_name=name)
    ORIENT['next']=(np.sin(np.radians(30)),-np.cos(np.radians(30)),0)
    lens = lathe([(0,0.05*mm),(3.6*mm,0.05*mm),(3.6*mm,0.06*mm)], M['lens'], seg=32,
                 xform=lambda V: (V + np.array([0,0,0])) if False else np.c_[V[:,0], V[:,1]+(V[:,0]+R)*tan30, V[:,2]])
    s.add_geometry(lens, node_name='lens_face', parent_node_name=name)
    y=SHAFT_L
    head = lathe([(0,y),(R,y),(R,y),(7*mm,y+4*mm),(9*mm,y+12*mm),(9*mm,y+40*mm),(9*mm,y+40*mm),
                  (16*mm,y+44*mm),(16*mm,y+58*mm),(16*mm,y+58*mm),(10*mm,y+58*mm),(10*mm,y+56*mm),(0,y+56*mm)], M['steel'], seg=32)
    s.add_geometry(head, node_name='ocular_body', parent_node_name=name)
    s.add_geometry(lathe([(9.05*mm,y+20*mm),(9.05*mm,y+34*mm)], M['insul'], seg=32), node_name='grip_band', parent_node_name=name)
    s.add_geometry(lathe([(16.05*mm,y+46*mm),(16.05*mm,y+56*mm)], M['insul'], seg=32), node_name='eyepiece_cup', parent_node_name=name)
    post = lathe([(0,0),(4*mm,0),(4*mm,0),(4*mm,10*mm),(6*mm,11*mm),(6*mm,15*mm),(4*mm,16*mm),(4*mm,22*mm),(0,22*mm)], M['steel'], seg=20)
    s.add_geometry(post, node_name='light_post', parent_node_name=name, transform=T(8*mm,y+24*mm,0)@rotZ(-90))
    return s

def trocar(size):
    big = size==12
    name=f'trocar_{size}mm'; s=new_scene(name)
    ri = (size+0.5)/2*mm; ro = ri+0.8*mm; L = 100*mm
    hr = (17 if big else 11)*mm; hh = (40 if big else 30)*mm
    # cannula with retention ribs, oblique cut tip handled by short taper
    prof=[(ri,0),(ri,0),(ro,1.5*mm)]
    y=1.5*mm
    while y < 55*mm:
        prof += [(ro,y+2*mm),(ro+0.6*mm,y+3*mm),(ro,y+3.2*mm)]; y+=4*mm
    prof += [(ro,L)]
    inner = [(ri,L),(ri,0)]
    s.add_geometry(lathe(prof, M['clear'] if big else M['steel'], seg=24), node_name='cannula', parent_node_name=name)
    ORIENT['next']='inward'
    s.add_geometry(lathe(inner[::-1], M['clear'] if big else M['steel'], seg=24), node_name='cannula_inner', parent_node_name=name)
    house = lathe([(ri,L),(ri,L),(ro+2*mm,L),(hr,L+8*mm),(hr,L+hh),(hr,L+hh),(ri+1*mm,L+hh)], M['accent'], seg=28)
    s.add_geometry(house, node_name='seal_housing', parent_node_name=name)
    # insufflation stopcock (+X)
    port = lathe([(0,0),(0,0),(3*mm,0),(3*mm,14*mm),(4*mm,14*mm),(4*mm,20*mm),(2.2*mm,20*mm),(0,20*mm)], M['handle'], seg=14)
    s.add_geometry(port, node_name='insufflation_port', parent_node_name=name, transform=T(hr-2*mm,L+14*mm,0)@rotZ(-90))
    lever = sweep_rect([[hr+8*mm,L+14*mm,0],[hr+8*mm,L+14*mm,9*mm]], 3*mm, 6*mm, M['handle'], up=(1,0,0))
    s.add_geometry(lever, node_name='stopcock_lever', parent_node_name=name)
    # obturator (removable) - bladeless optical tip
    ob = (ri-0.3*mm)
    obt = [(0,-16*mm),(0.6*mm,-15.8*mm),(ob*0.45,-12*mm),(ob*0.8,-6*mm),(ob,-1*mm),(ob,L+hh)]
    s.graph.update(frame_to='obturator', frame_from=name, matrix=np.eye(4))
    s.add_geometry(lathe(obt, M['clear'], seg=24), node_name='obturator_shaft', parent_node_name='obturator')
    cap = lathe([(ob,L+hh),(hr*0.9,L+hh),(hr*0.95,L+hh+4*mm),(hr*0.95,L+hh+16*mm),(hr*0.8,L+hh+20*mm),(0,L+hh+21*mm)], M['handle'], seg=28)
    s.add_geometry(cap, node_name='obturator_cap', parent_node_name='obturator')
    return s

KIT = {
 'trocar_12mm': lambda: trocar(12),
 'trocar_5mm': lambda: trocar(5),
 'laparoscope_10mm_30deg': laparoscope,
 'maryland_dissector_5mm': lambda: ring_instrument('maryland_dissector_5mm', maryland_jaw),
 'atraumatic_grasper_5mm': lambda: ring_instrument('atraumatic_grasper_5mm', fenestrated_jaw, cautery=False),
 'metzenbaum_scissors_5mm': lambda: ring_instrument('metzenbaum_scissors_5mm', scissor_blade, hinge_y=16*mm, ratchet=False),
 'clip_applier_10mm': lambda: ring_instrument('clip_applier_10mm', clip_jaw, shaft_r=5*mm, cautery=False, hinge_y=22*mm, knob_r=11*mm, ratchet=False),
 'l_hook_electrode_5mm': l_hook,
 'suction_irrigator_5mm': suction_irrigator,
}
if __name__ == '__main__':
    for k,f in KIT.items():
        sc = f()
        p = f'{OUT}/{k}.glb'; sc.export(p)
        tris = sum(len(g.faces) for g in sc.geometry.values())
        ext = sc.extents/mm
        print(f'{k:28s} tris={tris:6d}  size={os.path.getsize(p)/1024:7.0f} KB  extents(mm)={np.round(ext,1)}')
