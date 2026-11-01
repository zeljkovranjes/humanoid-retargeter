# Packs one dumped Bonebase template into a rigged flat GLB, one layer per part.
# usage: bake_glb.py <dump folder> <template> <out.glb>
import json,struct,sys,io,os
from PIL import Image
src,name,out=sys.argv[1],sys.argv[2],sys.argv[3]
d=json.load(open(f'{src}/{name}.json'))
SC=8; AT=1024
bones=list(d['bones']); byid={b['id']:b for b in bones}
for b in list(bones):
    if b['name'].startswith('foot_'):
        bones.append({'id':900+b['id'],'name':b['name'].replace('foot_','toe_'),'parent':b['id'],'part':b['part'],'hx':b['tx'],'hy':b['ty'],'tx':b['tx']+(b['tx']-b['hx'])*.4,'ty':b['ty']+(b['ty']-b['hy'])*.4})
idx={b['id']:i for i,b in enumerate(bones)}
top=min(p['y'] for p in d['parts']); bottom=max(p['y']+p['h'] for p in d['parts'])
k=1.8/(bottom-top); hips=next(b for b in bones if b['name']=='hips'); ox=hips['hx']
def W(px,py): return ((px-ox)*k,(bottom-py)*k)
atlas=Image.new('RGBA',(AT,AT),(0,0,0,0)); ax=ay=4; rowh=0
P=[];N=[];U=[];J=[];Wt=[];I=[]
def along(px,py,b):
    dx,dy=b['tx']-b['hx'],b['ty']-b['hy']; l=dx*dx+dy*dy or 1
    return max(0,min(1,((px-b['hx'])*dx+(py-b['hy'])*dy)/l))
def inside(px,py,b):
    t=along(px,py,b); r=b['r0']+(b['r1']-b['r0'])*t+1.5
    return (px-b['hx']-(b['tx']-b['hx'])*t)**2+(py-b['hy']-(b['ty']-b['hy'])*t)**2<=r*r
for p in d['parts']:
    raw=open(f"{src}/{p['file']}",'rb').read()
    im=Image.frombytes('RGBA',(p['w'],p['h']),raw)
    big=im.resize((p['w']*SC,p['h']*SC),Image.NEAREST)
    if ax+big.width+4>AT: ax=4; ay+=rowh+4; rowh=0
    atlas.paste(big,(ax,ay)); rowh=max(rowh,big.height)
    pb=[b for b in bones if b['part']==p['id'] and b['id']<900]
    A=im.getchannel('A').load()
    # One capsule of the part's own pixels per bone, as skin.lua draws it. Its far end rides
    # the next joint, so a bone that shortens takes its skin along, like Bonebase's `s` key.
    for order,bone in enumerate(pb):
        child=next((c for c in bones if c['parent']==bone['id'] and abs(c['hx']-bone['tx'])+abs(c['hy']-bone['ty'])<1.5),None)
        z=p['z']*0.008+order*0.0004
        vid={}
        def V(i,j):
            if (i,j) in vid: return vid[(i,j)]
            px,py=p['x']+i,p['y']+j
            t=along(px,py,bone) if child else 0
            x,y=W(px,py); vid[(i,j)]=len(P)
            P.append((x,y,z));N.append((0,0,1));U.append(((ax+i*SC)/AT,(ay+j*SC)/AT))
            J.append((idx[bone['id']],idx[child['id']] if child else idx[bone['id']],0,0));Wt.append((1-t,t,0,0))
            return vid[(i,j)]
        for j in range(p['h']):
            for i in range(p['w']):
                if A[i,j]>127 and inside(p['x']+i+.5,p['y']+j+.5,bone):
                    tl,tr,br,bl=V(i,j),V(i+1,j),V(i+1,j+1),V(i,j+1)
                    I+=[bl,br,tr,bl,tr,tl]
    ax+=big.width+4
buf=io.BytesIO();png=io.BytesIO();atlas.save(png,'PNG');views=[];acc=[]
def view(data):
    while buf.tell()%4: buf.write(b'\0')
    s=buf.tell();buf.write(data);views.append({'buffer':0,'byteOffset':s,'byteLength':len(data)});return len(views)-1
def add(data,ct,c,ty,**kw):
    acc.append({'bufferView':view(data),'componentType':ct,'count':c,'type':ty,**kw});return len(acc)-1
aP=add(b''.join(struct.pack('<3f',*v) for v in P),5126,len(P),'VEC3',min=[min(v[i] for v in P) for i in range(3)],max=[max(v[i] for v in P) for i in range(3)])
aN=add(b''.join(struct.pack('<3f',*v) for v in N),5126,len(P),'VEC3')
aU=add(b''.join(struct.pack('<2f',*v) for v in U),5126,len(P),'VEC2')
aJ=add(b''.join(struct.pack('<4H',*v) for v in J),5123,len(P),'VEC4')
aW=add(b''.join(struct.pack('<4f',*v) for v in Wt),5126,len(P),'VEC4')
aI=add(b''.join(struct.pack('<I',i) for i in I),5125,len(I),'SCALAR')
jw={b['id']:W(b['hx'],b['hy']) for b in bones}
aB=add(b''.join(struct.pack('<16f',1,0,0,0,0,1,0,0,0,0,1,0,-jw[b['id']][0],-jw[b['id']][1],0,1) for b in bones),5126,len(bones),'MAT4')
vI=view(png.getvalue())
nodes=[]
for b in bones:
    q=jw.get(b['parent'],(0,0)); w=jw[b['id']]
    nodes.append({'name':b['name'].replace('_L','.L').replace('_R','.R'),'translation':[w[0]-q[0],w[1]-q[1],0],'children':[idx[c['id']] for c in bones if c['parent']==b['id']]})
nodes.append({'name':'bonebase_mesh','mesh':0,'skin':0})
roots=[idx[b['id']] for b in bones if not b['parent'] or b['parent'] not in jw]+[len(bones)]
mat=name+'_parts'
j={'asset':{'version':'2.0','generator':'bonebase template'},'scene':0,'scenes':[{'nodes':roots}],'nodes':nodes,
'skins':[{'joints':list(range(len(bones))),'inverseBindMatrices':aB}],'meshes':[{'name':'bonebase_mesh','primitives':[{'attributes':{'POSITION':aP,'NORMAL':aN,'TEXCOORD_0':aU,'JOINTS_0':aJ,'WEIGHTS_0':aW},'indices':aI,'material':0}]}],
'materials':[{'name':mat,'pbrMetallicRoughness':{'baseColorTexture':{'index':0},'metallicFactor':0,'roughnessFactor':1},'alphaMode':'MASK','alphaCutoff':.5,'doubleSided':True}],
'textures':[{'source':0}],'images':[{'bufferView':vI,'mimeType':'image/png','name':mat}],'buffers':[{'byteLength':0}],'bufferViews':views,'accessors':acc}
while buf.tell()%4: buf.write(b'\0')
j['buffers'][0]['byteLength']=buf.tell()
js=json.dumps(j).encode();js+=b' '*((4-len(js)%4)%4);bn=buf.getvalue()
open(out,'wb').write(struct.pack('<III',0x46546C67,2,12+8+len(js)+8+len(bn))+struct.pack('<II',len(js),0x4E4F534A)+js+struct.pack('<II',len(bn),0x004E4942)+bn)
print(out,len(P),'verts',len(I)//3,'tris',len(bones),'bones')
