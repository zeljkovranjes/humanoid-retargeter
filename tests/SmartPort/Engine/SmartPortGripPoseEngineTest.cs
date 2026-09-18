using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Editor;
using Sandbox;

// Complements the grip-drift test: a stable hand in the wrong place must not pass.
public static class SmartPortGripPoseEngineTest
{
    static bool started;
    [EditorEvent.Frame]
    public static void Tick()
    {
        var root = Environment.GetEnvironmentVariable("HR_SMART_PORT_GRIP_POSE_PROJECT");
        if (started || Project.Current is null || string.IsNullOrEmpty(root)
            || !string.Equals(Path.GetFullPath(Project.Current.GetRootPath()).TrimEnd('/', '\\'),
                Path.GetFullPath(root).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
            || !AssetSystem.All.Any()) return;
        started = true; _ = Run(root);
    }

    static async Task Run(string root)
    {
        var data = new Dictionary<string, object>();
        void Save() => File.WriteAllText(Path.Combine(root, "grip-pose-result.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            data["started"] = DateTime.UtcNow; Save();
            var port = Environment.GetEnvironmentVariable("HR_SMART_PORT_GRIP_MODEL");
            if (string.IsNullOrWhiteSpace(port)) throw new Exception("Set HR_SMART_PORT_GRIP_MODEL to the newly ported Human Citizen asset");
            const string source = "models/player/human/frank_mp.vmdl";
            var session = SceneEditorSession.CreateDefault(); session.MakeActive();
            using (session.Scene.Push())
            {
                foreach (var path in new[] { source, port })
                {
                    var go = session.Scene.CreateObject(); go.Name = path;
                    var actor = go.Components.Create<SkinnedModelRenderer>();
                    actor.Model = Model.Load(path); actor.UseAnimGraph = true;
                    if (actor.Model.IsError) throw new Exception("Missing model " + path);
                }
                var camera = session.Scene.CreateObject(); camera.Name = "Grip Camera";
                camera.WorldPosition = new Vector3(110, 70, 70);
                camera.WorldRotation = Rotation.LookAt(new Vector3(0, 0, 45) - camera.WorldPosition);
                camera.Components.Create<CameraComponent>().FieldOfView = 40;
                foreach (var yaw in new[] { 25f, 145f, 265f })
                {
                    var light = session.Scene.CreateObject(); light.WorldRotation = Rotation.From(35, yaw, 0);
                    light.Components.Create<DirectionalLight>().LightColor = Color.White * .8f;
                }
            }
            EditorScene.Play(false, session); await Task.Delay(1500);
            if (!Game.IsPlaying || Game.ActiveScene is null || Game.ActiveScene.IsEditor) throw new Exception("Did not enter play mode");
            var actors = Game.ActiveScene.GetAllComponents<SkinnedModelRenderer>().ToArray();
            var original = actors.Single(a => a.Model.Name == source);
            var target = actors.Single(a => a.Model.Name == port);
            var observations = new List<object>(); data["observations"] = observations;
            foreach (var hold in new[] { 6, 5, 7 }) // Rifle, SMG, shotgun in the source graph.
            foreach (var speed in new[] { 0f, 120f, 240f })
            {
                float maxError = 0;
                for (var frame = 0; frame < 160; frame++)
                {
                    foreach (var actor in actors)
                    {
                        actor.Set("b_grounded", true); actor.Set("holdtype", hold); actor.Set("weapon_pose", 0);
                        actor.Set("move_style", speed == 0 ? 0 : speed < 200 ? 4 : 5);
                        actor.Set("aim_body", Vector3.Forward); actor.Set("aim_body_weight", 1f);
                        actor.Set("aim_head", Vector3.Forward); actor.Set("aim_head_weight", 1f);
                        actor.Set("move_x", speed); actor.Set("move_speed", speed); actor.Set("move_groundspeed", speed);
                        actor.Set("wish_x", speed); actor.Set("wish_groundspeed", speed);
                    }
                    await Task.Delay(20);
                    if (frame < 100) continue;
                    foreach (var hand in new[] { "hand_L", "hand_R" })
                        maxError = MathF.Max(maxError, RelativeHand(original, hand).Distance(RelativeHand(target, hand)));
                }
                observations.Add(new { hold, speed, maxError,
                    sourceHand = RelativeHand(original, "hand_L").ToString(), targetHand = RelativeHand(target, "hand_L").ToString() }); Save();
                if (hold == 6)
                {
                    var camera = Game.ActiveScene.GetAllComponents<CameraComponent>().Single(c => c.GameObject.Name == "Grip Camera");
                    foreach (var actor in actors)
                    {
                        foreach (var other in actors) other.WorldPosition = other == actor ? Vector3.Zero : new Vector3(0, 1000, 0);
                        await Task.Delay(150);
                        var pixmap = new Pixmap(900, 900);
                        if (!camera.RenderToPixmap(pixmap)) throw new Exception("Render failed");
                        File.WriteAllBytes(Path.Combine(root, $"grip-pose-{(actor == original ? "source" : "target")}-{speed}.png"), pixmap.GetPng());
                    }
                    foreach (var actor in actors) actor.WorldPosition = Vector3.Zero;
                }
                // These fixtures have comparable proportions. Allow fitted hand/socket size
                // differences, not a hand on the opposite side of the torso (~25 units).
                if (!float.IsFinite(maxError) || maxError > 3) throw new Exception($"Grip pose differs from source: {maxError} units, hold {hold}, speed {speed}");
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

    static Vector3 RelativeHand(SkinnedModelRenderer actor, string name)
    {
        var grip = actor.SceneModel.GetAttachment("hold_R", true) ?? throw new Exception("Missing weapon attachment");
        var bone = actor.Model.Bones.AllBones.Single(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        return grip.Rotation.Inverse * (actor.SceneModel.GetBoneWorldTransform(bone.Name).Position - grip.Position);
    }
}
