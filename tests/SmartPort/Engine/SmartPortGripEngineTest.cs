using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Editor;
using Sandbox;

// Read-only playback of a fresh Smart Port output in an explicitly opted-in scratch project.
public static class SmartPortGripEngineTest
{
    static bool started;
    [EditorEvent.Frame]
    public static void Tick()
    {
        var root = Environment.GetEnvironmentVariable("HR_SMART_PORT_GRIP_TEST_PROJECT");
        if (started || Project.Current is null || string.IsNullOrEmpty(root)
            || !string.Equals(Path.GetFullPath(Project.Current.GetRootPath()).TrimEnd('/', '\\'),
                Path.GetFullPath(root).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
            || !AssetSystem.All.Any()) return;
        started = true;
        _ = Run(root);
    }

    static async Task Run(string root)
    {
        var data = new Dictionary<string, object>();
        void Save() => File.WriteAllText(Path.Combine(root, "grip-test-result.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            data["started"] = DateTime.UtcNow; Save();
            var target = Environment.GetEnvironmentVariable("HR_SMART_PORT_GRIP_MODEL");
            if (string.IsNullOrWhiteSpace(target)) throw new Exception("Set HR_SMART_PORT_GRIP_MODEL to a freshly generated port's asset path");
            data["model"] = target;
            var session = SceneEditorSession.CreateDefault(); session.MakeActive();
            using (session.Scene.Push())
                foreach (var path in new[] { "models/player/human/frank_mp.vmdl", target })
                {
                    var go = session.Scene.CreateObject(); go.Name = path;
                    var actor = go.Components.Create<SkinnedModelRenderer>();
                    actor.Model = Model.Load(path); actor.UseAnimGraph = true;
                    if (actor.Model.IsError) throw new Exception("Missing model " + path);
                }
            EditorScene.Play(false, session); await Task.Delay(1500);
            if (!Game.IsPlaying || Game.ActiveScene is null || Game.ActiveScene.IsEditor) throw new Exception("Did not enter play mode");
            var actors = Game.ActiveScene.GetAllComponents<SkinnedModelRenderer>().ToArray();
            var hands = actors.ToDictionary(a => a, a => a.Model.Bones.AllBones
                .Where(b => string.Equals(b.Name, "hand_L", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(b.Name, "hand_R", StringComparison.OrdinalIgnoreCase))
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).Select(b => b.Name).ToArray());
            if (hands.Values.Any(names => names.Length != 2)) throw new Exception("Missing hand bones");
            var observations = new List<object>(); data["observations"] = observations;
            var reference = new Dictionary<SkinnedModelRenderer, Vector3[]>();
            foreach (var (style, x, y) in new[] { (0, 0f, 0f), (4, 120f, 0f), (5, 240f, 0f),
                (4, 0f, 120f), (4, 0f, -120f), (4, -120f, 0f), (4, 85f, 85f),
                (4, 85f, -85f), (4, -85f, 85f), (4, -85f, -85f) })
            {
                var samples = actors.ToDictionary(a => a, a => new List<Vector3[]>());
                var feet = actors.ToDictionary(a => a, a => new List<Vector3>());
                for (var frame = 0; frame < 180; frame++)
                {
                    foreach (var actor in actors)
                    {
                        actor.Set("b_grounded", true); actor.Set("holdtype", 6); actor.Set("weapon_pose", 0); actor.Set("move_style", style);
                        actor.Set("aim_body", Vector3.Forward); actor.Set("aim_body_weight", 1f);
                        actor.Set("aim_head", Vector3.Forward); actor.Set("aim_head_weight", 1f);
                        var speed = MathF.Sqrt(x * x + y * y);
                        actor.Set("move_x", x); actor.Set("move_y", y); actor.Set("move_speed", speed); actor.Set("move_groundspeed", speed);
                        actor.Set("wish_x", x); actor.Set("wish_y", y); actor.Set("wish_groundspeed", speed);
                    }
                    await Task.Delay(20);
                    if (frame < 80) continue;
                    foreach (var actor in actors)
                    {
                        var grip = actor.SceneModel.GetAttachment("hold_R", true) ?? throw new Exception("Missing weapon attachment");
                        samples[actor].Add(hands[actor].Select(b => grip.Rotation.Inverse *
                            (actor.SceneModel.GetBoneWorldTransform(b).Position - grip.Position)).ToArray());
                        var ankle = actor.Model.Bones.AllBones.First(b => string.Equals(b.Name, "ankle_L", StringComparison.OrdinalIgnoreCase));
                        feet[actor].Add(actor.SceneModel.GetBoneWorldTransform(ankle.Name).Position - actor.WorldPosition);
                    }
                }
                foreach (var actor in actors)
                {
                    if (style == 0) reference[actor] = Enumerable.Range(0, 2).Select(side =>
                        samples[actor].Aggregate(Vector3.Zero, (sum, v) => sum + v[side]) / samples[actor].Count).ToArray();
                    var footMotion = feet[actor].Max(v => v.Distance(feet[actor][0]));
                    if (style != 0 && footMotion < 1) throw new Exception("Locomotion did not animate: " + actor.Model.Name);
                    foreach (var side in new[] { 0, 1 })
                    {
                        var drift = samples[actor].Max(v => v[side].Distance(reference[actor][side]));
                        observations.Add(new { model = actor.Model.Name, style, x, y, side, drift, footMotion }); Save();
                        if (!float.IsFinite(drift) || drift > .15f)
                            throw new Exception($"Hand slipped relative to weapon: {actor.Model.Name}, side {side}, style {style}, velocity {x},{y}, drift {drift}");
                    }
                }
            }
            data["passed"] = true;
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
