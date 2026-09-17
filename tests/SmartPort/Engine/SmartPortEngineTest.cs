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
public static class SmartPortEngineTest
{
    static bool started;
    static string output;
    static readonly Dictionary<string, object> results = new();

    [EditorEvent.Frame]
    public static void Tick()
    {
        var expected = Environment.GetEnvironmentVariable("HR_SMART_PORT_TEST_PROJECT");
        if (started || Project.Current is null || string.IsNullOrEmpty(expected)
            || !string.Equals(Path.GetFullPath(Project.Current.GetRootPath()).TrimEnd('/', '\\'),
                Path.GetFullPath(expected).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
            || !AssetSystem.All.Any()) return;
        started = true;
        output = Path.Combine(expected, "smart-port-result.json");
        _ = Run();
    }

    static void Save() => File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

    static async Task Run()
    {
        try
        {
            results["started"] = DateTime.UtcNow; Save();
            var source = AssetSystem.FindByPath("models/player/human/frank_mp.vmdl") ?? throw new Exception("Missing compiled fixture");
            var target = AssetSystem.FindByPath("smart_probe/target.vmdl") ?? throw new Exception("Missing target fixture");
            if (!target.Compile(true)) throw new Exception("Target failed to compile");
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HumanoidRetargeter.Editor.SmartPortModels")).FirstOrDefault(t => t != null)
                ?? throw new Exception("SmartPortModels was not compiled into the editor library");
            var method = type.GetMethod("CreateAsync", BindingFlags.Static | BindingFlags.NonPublic);
            Action<string> progress = message => { results["stage"] = message; Save(); };
            var name = "test_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
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

            // Actual editor play mode: component animation updates are driven by the game,
            // not SceneModel.Update calls or the retargeter's preview widget.
            var session = SceneEditorSession.CreateDefault();
            session.MakeActive();
            using (session.Scene.Push())
            {
                var go = session.Scene.CreateObject(); go.Name = "Smart Port Character";
                var renderer = go.Components.Create<SkinnedModelRenderer>();
                renderer.Model = model; renderer.UseAnimGraph = true;
                var floor = session.Scene.CreateObject(); floor.Name = "Floor";
                var box = floor.Components.Create<BoxCollider>();
                box.Scale = new Vector3(4000, 4000, 4); box.Center = new Vector3(0, 0, -2);
            }
            EditorScene.Play(false, session);
            await Task.Delay(1500);
            if (!Game.IsPlaying || Game.ActiveScene is null || Game.ActiveScene.IsEditor) throw new Exception("Did not enter game play mode");
            results["inGame"] = true; Save();
            var actor = Game.ActiveScene.GetAllComponents<SkinnedModelRenderer>().Single(r => r.GameObject.Name == "Smart Port Character");
            var footsteps = 0;
            actor.OnFootstepEvent += _ => footsteps++;
            var movement = new List<object>();
            foreach (var (style, speed) in new[] { (0, 100f), (4, 120f), (5, 240f) })
            {
                actor.Set("b_grounded", true); actor.Set("move_style", style); actor.Set("weapon_pose", 0);
                actor.Set("move_x", speed); actor.Set("move_speed", speed); actor.Set("move_groundspeed", speed);
                actor.Set("wish_x", speed); actor.Set("wish_groundspeed", speed);
                await Task.Delay(1000);
                var boneNames = model.Bones.AllBones.Where(b => b.Name.Contains("ankle") || b.Name.Contains("leg_lower") || b.Name.Contains("hand")).Select(b => b.Name).ToArray();
                if (boneNames.Length < 4) throw new Exception("Insufficient limb bones for locomotion assertions");
                var initial = boneNames.Select(n => actor.SceneModel.GetBoneWorldTransform(n).Position - actor.WorldPosition).ToArray();
                float maxMotion = 0;
                for (var frame = 0; frame < 100; frame++)
                {
                    await Task.Delay(20);
                    actor.WorldPosition += Vector3.Forward * speed * .02f;
                    for (var b = 0; b < boneNames.Length; b++)
                    {
                        var pos = actor.SceneModel.GetBoneWorldTransform(boneNames[b]).Position - actor.WorldPosition;
                        var distance = pos.Distance(initial[b]);
                        if (!float.IsFinite(distance) || pos.Length > 200) throw new Exception("Non-finite or stretched limb in game");
                        maxMotion = MathF.Max(maxMotion, distance);
                    }
                }
                movement.Add(new { style, speed, maxMotion });
                if (maxMotion < 1) throw new Exception("Limbs did not animate during locomotion");
            }
            results["locomotion"] = movement;
            results["footsteps"] = footsteps;
            if (footsteps == 0) throw new Exception("No footstep events during walking/running");
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
