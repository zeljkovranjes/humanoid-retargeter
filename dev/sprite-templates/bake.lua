-- Dumps Bonebase's own templates and skin, part by part, for bake_glb.py.
-- usage: lua bake.lua <dump folder> <Bonebase src folder, trailing slash>
local root=arg[2]
local function load(p) return dofile(root..p) end
local Buffer=load("kit/buffer.lua")
local Rig=load("engine/rig.lua")
local Skin=load("engine/skin.lua")
local Anim=load("engine/anim.lua")
local T=load("engine/templates.lua"); T.bind(Rig)
if Skin.bind then Skin.bind(Buffer) end
local out=arg[1]
local function q(s) return '"'..tostring(s)..'"' end
for _,name in ipairs({"Profile","Humanoid","Chibi"}) do
  local doc=assert(T.build(name,64,64))
  local world=Rig.solve(doc)
  local layers=Skin.render(doc,world,{outline="pixel"})
  local safe=name:lower()
  local f=io.open(out.."/"..safe..".json","w")
  local function bulges(list)
    local t = {}
    for i, q in ipairs(list or {}) do t[i] = string.format("[%f,%f]", q[1], q[2]) end
    return "[" .. table.concat(t, ",") .. "]"
  end
  f:write(string.format('{"canvas":[%d,%d],"bones":[', doc.canvas.w, doc.canvas.h))
  for i,b in ipairs(doc.bones) do
    local w=world[b.id]
    f:write((i>1 and "," or "")..string.format('{"id":%d,"name":%s,"parent":%d,"part":%d,"hx":%f,"hy":%f,"tx":%f,"ty":%f,"r0":%f,"r1":%f,"bulgeL":%s,"bulgeR":%s}',b.id,q(b.name),b.parent or 0,b.part,w.hx,w.hy,w.tx,w.ty,b.rHead,b.rTail,bulges(b.bulgeL),bulges(b.bulgeR)))
  end
  f:write('],"parts":[')
  for i,L in ipairs(layers) do
    local buf=Skin.layerBuffer(L,{fillAlpha=255})
    local fn=string.format("%s_%d.raw",safe,L.part.id)
    local r=io.open(out.."/"..fn,"wb")
    local t={}
    for k=1,#buf.d do t[k]=string.char(buf.d[k]) end
    r:write(table.concat(t)); r:close()
    f:write((i>1 and "," or "")..string.format('{"id":%d,"name":%s,"z":%d,"blend":%f,"color":[%d,%d,%d],"x":%d,"y":%d,"w":%d,"h":%d,"file":%s}',L.part.id,q(L.part.name),L.part.z,L.part.blend or 0,L.part.color[1],L.part.color[2],L.part.color[3],L.x0,L.y0,buf.w,buf.h,q(fn)))
  end
  f:write(']}'); f:close()
  -- what the C# port of the skin must reproduce pixel for pixel
  local rest=Skin.compose(layers,64,64,{fillAlpha=255})
  local g=io.open(out.."/"..safe..".rest.rgba","wb")
  local c={}
  for k=1,#rest.d do c[k]=string.char(rest.d[k]) end
  g:write(table.concat(c)); g:close()
  print(name,#doc.bones,"bones",#layers,"parts")
end
