import numpy as np, trimesh
from trimesh.visual import TextureVisuals
TAU = 2*np.pi
V_SCALE = 10.0   # 1 texture tile per 10 cm along length

ORIENT = {}
def _mesh(v, f, uv, mat):
    m = trimesh.Trimesh(vertices=np.asarray(v, float), faces=np.asarray(f, int), process=False)
    m.visual = TextureVisuals(uv=np.asarray(uv, float), material=mat)
    # drop zero-area faces (from duplicated "hard edge" profile points / poles)
    keep = m.area_faces > 1e-14
    m.update_faces(keep); m.remove_unreferenced_vertices()
    # global orientation: winding is consistent by construction, make it face outward
    o = ORIENT.pop('next', None)
    if o is None:
        c = m.vertices.mean(0)
        score = np.sum(m.area_faces*np.einsum('ij,ij->i', m.face_normals, m.triangles_center-c))
    elif isinstance(o, str):   # 'inward'
        c = m.vertices.mean(0)
        score = -np.sum(m.area_faces*np.einsum('ij,ij->i', m.face_normals, m.triangles_center-c))
    else:                      # explicit facing vector (flat parts)
        score = np.sum(m.area_faces*(m.face_normals@np.asarray(o,float)))
    if score < 0: m.invert()
    return m

def lathe(profile, mat, seg=24, xform=None):
    """Revolve [(r,y),...] around +Y. Repeat a point to get a hard edge."""
    p = np.asarray(profile, float)
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))]
    n = len(p); V=[]; UV=[]; F=[]
    for i in range(seg+1):
        a = TAU*i/seg
        for j in range(n):
            r, y = p[j]
            V.append([r*np.cos(a), y, -r*np.sin(a)]); UV.append([i/seg, d[j]*V_SCALE])
    for i in range(seg):
        for j in range(n-1):
            a0=i*n+j; a1=(i+1)*n+j
            F += [[a0, a1, a0+1], [a0+1, a1, a1+1]]
    V = np.array(V)
    if xform is not None: V = xform(V)
    return _mesh(V, F, UV, mat)

def _frames(path, up):
    path = np.asarray(path, float)
    t = np.gradient(path, axis=0); t /= np.linalg.norm(t, axis=1)[:,None]
    up = np.asarray(up, float)
    s = np.cross(t, up); s /= np.linalg.norm(s, axis=1)[:,None]
    u = np.cross(s, t)
    return t, s, u

def sweep_rect(path, w, h, mat, up=(0,0,1)):
    """Rectangular section swept along path. w,h scalar or per-point. Hard edges + end caps."""
    path = np.asarray(path, float); n=len(path)
    w = np.broadcast_to(w, n).astype(float); h = np.broadcast_to(h, n).astype(float)
    t, s, u = _frames(path, up)
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))]
    corners = [(-1,-1),(1,-1),(1,1),(-1,1)]
    V=[];UV=[];F=[]
    for k in range(4):
        c0, c1 = corners[k], corners[(k+1)%4]
        base=len(V)
        for i in range(n):
            for jj,c in enumerate((c0,c1)):
                V.append(path[i] + s[i]*c[0]*w[i]/2 + u[i]*c[1]*h[i]/2)
                UV.append([0.25*k + 0.25*jj, d[i]*V_SCALE])
        for i in range(n-1):
            a=base+2*i
            F += [[a, a+1, a+2],[a+1, a+3, a+2]]
    for i, flip in ((0,True),(n-1,False)):
        base=len(V)
        for c in corners:
            V.append(path[i] + s[i]*c[0]*w[i]/2 + u[i]*c[1]*h[i]/2)
            UV.append([0.5+0.5*c[0]*0.3, 0.5+0.5*c[1]*0.3])
        q=[[base,base+1,base+2],[base,base+2,base+3]]
        F += [[a,c,b] for a,b,c in q] if flip else q
    return _mesh(V, F, UV, mat)

def tube(path, r, mat, seg=12, closed=False, up=(0,0,1), caps=True):
    path = np.asarray(path, float)
    if closed: path = np.vstack([path, path[:1]])
    n=len(path); r = np.broadcast_to(r, n).astype(float)
    t, s, u = _frames(path, up)
    if closed:  # consistent frame across seam
        t[-1]=t[0]; s[-1]=s[0]; u[-1]=u[0]
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))]
    V=[];UV=[];F=[]
    for i in range(n):
        for k in range(seg+1):
            a=TAU*k/seg
            V.append(path[i] + r[i]*(np.cos(a)*s[i] + np.sin(a)*u[i])); UV.append([k/seg, d[i]*V_SCALE])
    m=seg+1
    for i in range(n-1):
        for k in range(seg):
            a0=i*m+k; a1=(i+1)*m+k
            F += [[a0,a0+1,a1],[a0+1,a1+1,a1]]
    if caps and not closed:
        for i, flip in ((0,False),(n-1,True)):
            c=len(V); V.append(path[i]); UV.append([0.5,0.5])
            for k in range(seg):
                a0=i*m+k
                F.append([c,a0+1,a0] if not flip else [c,a0,a0+1])
    return _mesh(V, F, UV, mat)

def ring_path(cx, cy, rx, ry, n=20, z=0.0):
    a = np.linspace(0, TAU, n, endpoint=False)
    return np.c_[cx+rx*np.cos(a), cy+ry*np.sin(a), np.full(n, z)]

def arc_path(p0, p1, bulge, n=10, axis=0):
    """Line p0->p1 with a sideways bulge (quadratic)."""
    p0=np.asarray(p0,float); p1=np.asarray(p1,float); t=np.linspace(0,1,n)[:,None]
    P = p0*(1-t)+p1*t; off=np.zeros(3); off[axis]=bulge
    return P + 4*t*(1-t)*off

def rotX(deg):
    return trimesh.transformations.rotation_matrix(np.radians(deg),[1,0,0])
def rotZ(deg):
    return trimesh.transformations.rotation_matrix(np.radians(deg),[0,0,1])
def T(x,y,z):
    return trimesh.transformations.translation_matrix([x,y,z])
