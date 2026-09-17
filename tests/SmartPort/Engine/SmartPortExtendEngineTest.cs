using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Editor;
using Sandbox;

// Copy into the Editor folder of an isolated test project with this library installed.
// Never runs unless the exact project root is explicitly opted in.
public static class SmartPortExtendEngineTest
{
    static bool started;
    static string output;
    static readonly Dictionary<string, object> results = new();

    [EditorEvent.Frame]
    public static void Tick()
    {
        var expected = Environment.GetEnvironmentVariable("HR_SMART_PORT_EXTEND_TEST_PROJECT");
        if (started || Project.Current is null || string.IsNullOrEmpty(expected)
            || !string.Equals(Path.GetFullPath(Project.Current.GetRootPath()).TrimEnd('/', '\\'),
                Path.GetFullPath(expected).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
            || !AssetSystem.All.Any()) return;
        started = true;
        output = Path.Combine(expected, "smart-port-extend-result.json");
        _ = Run();
    }

    static void Save() => File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

    static async Task Run()
    {
        try
        {
            results["started"] = DateTime.UtcNow; Save();
            var source = AssetSystem.FindByPath("models/player/human/frank_mp.vmdl") ?? throw new Exception("Missing compiled fixture");
            var target = AssetSystem.FindByPath("animations/retargeted/retargeted_bunny.vmdl") ?? throw new Exception("Missing target fixture");
            if (Model.Load(target.Path).IsError) throw new Exception("Target failed to load");
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HumanoidRetargeter.Editor.SmartPortModels")).FirstOrDefault(t => t != null)
                ?? throw new Exception("SmartPortModels was not compiled into the editor library");
            var method = type.GetMethod("CreateExtendedAsync", BindingFlags.Static | BindingFlags.NonPublic);
            Action<string> progress = message => { results["stage"] = message; Save(); };
            var name = "extend_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var task = (Task)method.Invoke(null, new object[] { source, target, "smart_port", name, progress, CancellationToken.None });
            await task;
            var write = task.GetType().GetProperty("Result").GetValue(task);
            var compiled = (bool)write.GetType().GetProperty("Compiled").GetValue(write);
            results["compiled"] = compiled;
            results["errors"] = write.GetType().GetProperty("Errors").GetValue(write); Save();
            if (!compiled) throw new Exception("Smart Port verification failed");
            var path = "smart_port/" + name + ".vmdl";
            var model = Model.Load(path);
            results["sequences"] = model.AnimationCount;
            results["graph"] = model.AnimGraph.Name; Save();
            var skin = Model.Load(target.Path);
            var skinBones = skin.Bones.AllBones.Where(b => b.Parent != null &&
                (b.Name.StartsWith("finger_") || b.Name.StartsWith("arm_lower_") || b.Name.StartsWith("hand_")
                || b.Name.StartsWith("leg_lower_") || b.Name.StartsWith("ankle_")) && !b.Name.Contains("twist")
                && !b.Name.Contains("IK") && !b.Name.Contains("ikrule")).ToArray();

            // Actual editor play mode: component animation updates are driven by the game,
            // not SceneModel.Update calls or the retargeter's preview widget.
            var session = SceneEditorSession.CreateDefault();
            session.MakeActive();
            using (session.Scene.Push())
            {
                var go = session.Scene.CreateObject(); go.Name = "Smart Port Character";
                var renderer = go.Components.Create<SkinnedModelRenderer>();
                renderer.Model = model; renderer.UseAnimGraph = true;
                var original = session.Scene.CreateObject(); original.Name = "Original";
                var originalRenderer = original.Components.Create<SkinnedModelRenderer>();
                originalRenderer.Model = skin; originalRenderer.UseAnimGraph = true;
                var cameraObject = session.Scene.CreateObject(); cameraObject.Name = "Capture Camera";
                cameraObject.Components.Create<CameraComponent>().FieldOfView = 38;
                foreach (var yaw in new[] { 25f, 145f, 265f })
                {
                    var light = session.Scene.CreateObject(); light.WorldRotation = Rotation.From(35, yaw, 0);
                    light.Components.Create<DirectionalLight>().LightColor = Color.White * .8f;
                }
                var floor = session.Scene.CreateObject(); floor.Name = "Floor";
                var box = floor.Components.Create<BoxCollider>();
                box.Scale = new Vector3(4000, 4000, 4); box.Center = new Vector3(0, 0, -2);
            }
            EditorScene.Play(false, session);
            await Task.Delay(1500);
            if (!Game.IsPlaying || Game.ActiveScene is null || Game.ActiveScene.IsEditor) throw new Exception("Did not enter game play mode");
            results["inGame"] = true; Save();
            var actor = Game.ActiveScene.GetAllComponents<SkinnedModelRenderer>().Single(r => r.GameObject.Name == "Smart Port Character");
            var baseline = Game.ActiveScene.GetAllComponents<SkinnedModelRenderer>().Single(r => r.GameObject.Name == "Original");
            var footsteps = 0;
            actor.OnFootstepEvent += _ => footsteps++;
            var movement = new List<object>();
            foreach (var (style, speed) in new[] { (0, 100f), (1, 120f), (2, 240f) })
            {
                foreach (var driven in new[] { actor, baseline })
                {
                    driven.Set("b_grounded", true); driven.Set("move_style", style); driven.Set("weapon_pose", 0);
                    driven.Set("move_x", speed); driven.Set("move_speed", speed); driven.Set("move_groundspeed", speed);
                    driven.Set("wish_x", speed); driven.Set("wish_groundspeed", speed);
                }
                await Task.Delay(1000);
                var boneNames = model.Bones.AllBones.Where(b => b.Name.Contains("ankle") || b.Name.Contains("leg_lower") || b.Name.Contains("hand")).Select(b => b.Name).ToArray();
                if (boneNames.Length < 4) throw new Exception("Insufficient limb bones for locomotion assertions");
                var initial = boneNames.Select(n => actor.SceneModel.GetBoneWorldTransform(n).Position - actor.WorldPosition).ToArray();
                float maxMotion = 0;
                float maxStretch = 0;
                for (var frame = 0; frame < 100; frame++)
                {
                    await Task.Delay(20);
                    actor.WorldPosition += Vector3.Forward * speed * .02f;
                    baseline.WorldPosition = actor.WorldPosition;
                    foreach (var bone in skinBones)
                    {
                        var bindLength = bone.LocalTransform.Position.Distance(bone.Parent.LocalTransform.Position);
                        var length = actor.SceneModel.GetBoneWorldTransform(bone.Name).Position.Distance(
                            actor.SceneModel.GetBoneWorldTransform(bone.Parent.Name).Position);
                        // Preserve procedural graph scaling too, rather than forcing fixed bind lengths.
                        var originalLength = baseline.SceneModel.GetBoneWorldTransform(bone.Name).Position.Distance(
                            baseline.SceneModel.GetBoneWorldTransform(bone.Parent.Name).Position);
                        maxStretch = MathF.Max(maxStretch, MathF.Abs(length - originalLength));
                        if (!float.IsFinite(length) || MathF.Abs(length - originalLength) > .15f)
                            throw new Exception($"Changed skin bone {bone.Name}: bind={bindLength}, original={originalLength}, animated={length}");
                    }
                    for (var b = 0; b < boneNames.Length; b++)
                    {
                        var pos = actor.SceneModel.GetBoneWorldTransform(boneNames[b]).Position - actor.WorldPosition;
                        var distance = pos.Distance(initial[b]);
                        if (!float.IsFinite(distance) || pos.Length > 200) throw new Exception($"Non-finite or stretched limb {boneNames[b]}: {pos} (initial {initial[b]}, style {style})");
                        maxMotion = MathF.Max(maxMotion, distance);
                    }
                }
                movement.Add(new { style, speed, maxMotion, maxStretch });
                if (maxMotion < 1) throw new Exception("Limbs did not animate during locomotion");
            }
            results["locomotion"] = movement;
            results["footsteps"] = footsteps;
            if (footsteps == 0) throw new Exception("No footstep events during walking/running");
            // The graph must actually switch into the imported branch and back.
            actor.Set("move_x", 0f); actor.Set("move_speed", 0f); actor.Set("move_groundspeed", 0f);
            actor.Set("wish_x", 0f); actor.Set("wish_groundspeed", 0f); actor.Set("move_style", 0);
            await Task.Delay(700);
            var originalHand = actor.SceneModel.GetBoneWorldTransform("hand_L").Position - actor.WorldPosition;
            baseline.SceneModel.RenderingEnabled = false;
            void Capture(string label)
            {
                var camera = Game.ActiveScene.GetAllComponents<CameraComponent>().Single(c => c.GameObject.Name == "Capture Camera");
                camera.WorldPosition = actor.WorldPosition + new Vector3(130, 65, 65);
                camera.WorldRotation = Rotation.LookAt(actor.WorldPosition + new Vector3(0, 0, 37) - camera.WorldPosition);
                var pixmap = new Pixmap(768, 768);
                if (!camera.RenderToPixmap(pixmap)) throw new Exception("Capture failed: " + label);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output), "extend-" + label + ".png"), pixmap.GetPng());
            }
            Capture("original");
            var guide = File.ReadAllLines(Path.Combine(Project.Current.GetAssetsPath(), "smart_port", name + "_smart_port", "clips.txt"));
            int ClipId(string clip) => int.Parse(guide.Single(l => l.Contains(": " + clip + " (")).Split(':')[0]);
            actor.Set("hr_smartport_clip", ClipId("hr_port_bindPose"));
            await Task.Delay(700);
            var importedHand = actor.SceneModel.GetBoneWorldTransform("hand_L").Position - actor.WorldPosition;
            results["importedBranchMotion"] = importedHand.Distance(originalHand);
            if (importedHand.Distance(originalHand) < 4) throw new Exception("Imported clip selector did not change the pose");
            Capture("imported");
            actor.Set("hr_smartport_clip", 0);
            await Task.Delay(700);
            var resumedHand = actor.SceneModel.GetBoneWorldTransform("hand_L").Position - actor.WorldPosition;
            results["resumedHandError"] = resumedHand.Distance(originalHand);
            if (resumedHand.Distance(originalHand) > 3) throw new Exception("Original graph did not resume");
            Capture("resumed");
            actor.Set("hr_smartport_clip", ClipId("hr_port_bindPose_delta"));
            await Task.Delay(700);
            var additiveHand = actor.SceneModel.GetBoneWorldTransform("hand_L").Position - actor.WorldPosition;
            results["zeroAdditiveError"] = additiveHand.Distance(resumedHand);
            if (additiveHand.Distance(resumedHand) > 3) throw new Exception("Zero additive replaced or distorted the original pose");
            Capture("additive");
            results["passed"] = true;
        }
        catch (Exception e) { results["passed"] = false; results["error"] = e.ToString(); }
        finally
        {
            results["completed"] = true; Save();
            if (Game.IsPlaying) EditorScene.Stop();
            await Task.Delay(1000);
            EditorUtility.Quit(true);
        }
    }
}
