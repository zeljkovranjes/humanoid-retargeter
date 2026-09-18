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

// A custom rig without stock attachments must receive calibrated copied sockets.
// Run only in an explicitly opted-in isolated project, never a user's active editor.
public static class SmartPortCopiedSocketEngineTest
{
    static bool started;
    [EditorEvent.Frame]
    public static void Tick()
    {
        var root = Environment.GetEnvironmentVariable("HR_SMART_PORT_COPIED_SOCKET_PROJECT");
        if (started || Project.Current is null || string.IsNullOrEmpty(root)
            || !string.Equals(Path.GetFullPath(Project.Current.GetRootPath()).TrimEnd('/', '\\'), Path.GetFullPath(root).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
            || !AssetSystem.All.Any()) return;
        started = true;
        _ = Run(root);
    }
    static async Task Run(string root)
    {
        var data = new Dictionary<string, object>();
        void Save() => File.WriteAllText(Path.Combine(root, "copied-socket-result.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            data["started"] = DateTime.UtcNow; Save();
            var source = AssetSystem.FindByPath("models/citizen/citizen.vmdl") ?? throw new Exception("Missing Citizen source");
            var target = AssetSystem.FindByPath("humanoid_retargeter_smoke/customfbx/catgirl_2_preview_bind_9e13ad1f.vmdl") ?? throw new Exception("Missing Catgirl target fixture");
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HumanoidRetargeter.Editor.SmartPortModels")).First(t => t != null);
            var name = "copied_socket_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            Action<string> progress = s => { data["stage"] = s; Save(); };
            var task = (Task)type.GetMethod("CreateAsync", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { source, target, "smart_port", name, progress, CancellationToken.None });
            await task;
            var result = task.GetType().GetProperty("Result").GetValue(task);
            data["compiled"] = result.GetType().GetProperty("Compiled").GetValue(result);
            data["errors"] = result.GetType().GetProperty("Errors").GetValue(result);
            data["model"] = "smart_port/" + name + ".vmdl"; Save();
            if (!(bool)data["compiled"]) throw new Exception("Compile failed");
            var session = SceneEditorSession.CreateDefault(); session.MakeActive();
            using (session.Scene.Push())
                foreach (var path in new[] { source.Path, (string)data["model"] })
                {
                    var go = session.Scene.CreateObject(); go.Name = path;
                    var actor = go.Components.Create<SkinnedModelRenderer>();
                    actor.Model = Model.Load(path); actor.UseAnimGraph = true;
                }
            EditorScene.Play(false, session); await Task.Delay(1500);
            if (!Game.IsPlaying || Game.ActiveScene is null || Game.ActiveScene.IsEditor) throw new Exception("Did not enter play mode");
            var actors = Game.ActiveScene.GetAllComponents<SkinnedModelRenderer>().ToArray();
            var observations = new List<object>();
            const int hold = 2; // Source graph's rifle hold type.
            foreach (var weight in new[] { 0f, 1f })
            foreach (var pitch in new[] { 0f, -25f, 25f })
            {
                for (var settle = 0; settle < 50; settle++)
                {
                    foreach (var actor in actors)
                    {
                        actor.Set("b_grounded", true); actor.Set("holdtype", hold); actor.Set("weapon_pose", 0);
                        actor.Set("aim_body", Rotation.From(pitch, 0, 0).Forward); actor.Set("aim_body_weight", weight);
                        actor.Set("aim_head", Rotation.From(pitch, 0, 0).Forward); actor.Set("aim_head_weight", 1f);
                    }
                    await Task.Delay(100);
                }
                var sourceActor = actors.Single(a => a.Model.Name == source.Path);
                var targetActor = actors.Single(a => a.Model.Name == (string)data["model"]);
                var sourceGrip = sourceActor.SceneModel.GetAttachment("hold_R", true).Value;
                var targetGrip = targetActor.SceneModel.GetAttachment("hold_R", true).Value;
                var a = sourceGrip.Rotation; var b = targetGrip.Rotation;
                var error = 2 * MathF.Acos(Math.Clamp(MathF.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w), 0, 1)) * 180 / MathF.PI;
                if (!float.IsFinite(error) || error > 3) throw new Exception($"Rifle attachment diverged {error} degrees (aim weight {weight}, pitch {pitch})");
                foreach (var actor in actors)
                {
                    var att = actor.SceneModel.GetAttachment("hold_R", true) ?? throw new Exception("Missing grip");
                    observations.Add(new { model = actor.Model.Name, hold, weight, pitch, error,
                        rotation = att.Rotation.Angles().ToString(), forward = att.Rotation.Forward.ToString(), position = att.Position.ToString() });
                }
            }
            data["observations"] = observations; data["passed"] = true;
        }
        catch (Exception e) { data["error"] = e.ToString(); data["passed"] = false; }
        finally
        {
            data["completed"] = true; Save();
            if (Game.IsPlaying) EditorScene.Stop();
            await Task.Delay(1000); EditorUtility.Quit(true);
        }
    }
}
