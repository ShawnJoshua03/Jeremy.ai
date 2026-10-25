import numpy as np
from PIL import Image
from trimesh.visual.material import PBRMaterial
rng = np.random.default_rng(7)
N = 512

def blur_periodic(a, k, axis):
    out = np.zeros_like(a)
    for s in range(-k, k+1): out += np.roll(a, s, axis=axis)
    return out/(2*k+1)

def periodic_noise(scale_x, scale_y):
    a = rng.standard_normal((N, N))
    a = blur_periodic(a, scale_x, 1); a = blur_periodic(a, scale_y, 0)
    a -= a.min(); a /= a.max(); return a

def normal_from_height(h, strength):
    dx = (np.roll(h,-1,1)-np.roll(h,1,1))*strength
    dy = (np.roll(h,-1,0)-np.roll(h,1,0))*strength
    n = np.dstack([-dx, -dy, np.ones_like(h)]); n /= np.linalg.norm(n, axis=2, keepdims=True)
    return Image.fromarray(((n*0.5+0.5)*255).astype(np.uint8)).resize((256,256), Image.LANCZOS)

import io
def _jpeg(img, q=90):
    b = io.BytesIO(); img.convert('RGB').save(b, format='JPEG', quality=q, optimize=True); b.seek(0)
    out = Image.open(b); out.load(); out.format = 'JPEG'; return out
def rgb(a): return _jpeg(Image.fromarray((np.clip(a,0,1)*255).astype(np.uint8)))

def orm(rough, metal):
    """glTF metallicRoughness: G=roughness, B=metallic (R used as AO=1)."""
    return rgb(np.dstack([np.ones_like(rough), rough, np.broadcast_to(metal, rough.shape)]))

def make_materials():
    M = {}
    # Brushed surgical stainless: streaks run along the shaft (constant along v / image rows)
    streak = 0.7*periodic_noise(0, 40) + 0.3*periodic_noise(1, 6)
    base = 0.70 + 0.10*streak
    M['steel'] = PBRMaterial(name='brushed_stainless',
        baseColorTexture=rgb(np.dstack([base, base*1.0, base*1.02])),
        metallicRoughnessTexture=orm(0.22+0.18*streak, 1.0),
        normalTexture=normal_from_height(streak, 1.5),
        metallicFactor=1.0, roughnessFactor=1.0)
    # Black electrical insulation sleeve (matte polymer) with fine grain
    g = periodic_noise(1, 1)
    M['insul'] = PBRMaterial(name='insulation_black',
        baseColorTexture=rgb(np.dstack([0.035+0.02*g]*3)),
        metallicRoughnessTexture=orm(0.42+0.12*g, 0.0),
        normalTexture=normal_from_height(g, 0.6),
        metallicFactor=1.0, roughnessFactor=1.0)
    # Handle polymer: dark grey, lightly textured (bead-blast look)
    g2 = periodic_noise(2, 2)
    M['handle'] = PBRMaterial(name='handle_polymer',
        baseColorTexture=rgb(np.dstack([0.16+0.04*g2, 0.17+0.04*g2, 0.19+0.04*g2])),
        metallicRoughnessTexture=orm(0.62+0.15*g2, 0.0),
        normalTexture=normal_from_height(g2, 1.2),
        metallicFactor=1.0, roughnessFactor=1.0)
    # Accent polymer (trocar housing, rotation knob ring)
    M['accent'] = PBRMaterial(name='accent_blue',
        baseColorTexture=rgb(np.dstack([0.08+0.03*g2, 0.33+0.04*g2, 0.62+0.04*g2])),
        metallicRoughnessTexture=orm(0.45+0.1*g2, 0.0),
        metallicFactor=1.0, roughnessFactor=1.0)
    # Clear-ish obturator tip plastic (opaque for AR occlusion safety)
    M['clear'] = PBRMaterial(name='obturator_plastic',
        baseColorTexture=rgb(np.dstack([0.80+0.05*g, 0.85+0.05*g, 0.88+0.05*g])),
        metallicRoughnessTexture=orm(0.15+0.05*g, 0.0),
        metallicFactor=1.0, roughnessFactor=1.0)
    # Scope lens / fiber face
    M['lens'] = PBRMaterial(name='scope_lens',
        baseColorTexture=rgb(np.dstack([np.full((N,N),0.02), np.full((N,N),0.03), np.full((N,N),0.06)])),
        metallicRoughnessTexture=orm(np.full((N,N),0.05), 0.0),
        metallicFactor=1.0, roughnessFactor=1.0)
    # Suction tip: brushed steel with irrigation holes baked in base color
    hole = np.zeros((N,N))
    yy, xx = np.mgrid[0:N,0:N]
    for cx in (N*0.125, N*0.375, N*0.625, N*0.875):
        for cy in (N*0.08, N*0.16, N*0.24, N*0.76, N*0.84, N*0.92):
            hole = np.maximum(hole, (((xx-cx)**2+(yy-cy)**2) < (N*0.035)**2).astype(float))
    hb = base*(1-hole) + 0.02*hole
    M['suction_tip'] = PBRMaterial(name='suction_tip_holes',
        baseColorTexture=rgb(np.dstack([hb,hb,hb*1.02])),
        metallicRoughnessTexture=orm(0.22+0.18*streak*(1-hole)+0.7*hole, 1.0-hole),
        normalTexture=normal_from_height(streak*(1-hole) - 2*hole, 1.5),
        metallicFactor=1.0, roughnessFactor=1.0)
    return M
