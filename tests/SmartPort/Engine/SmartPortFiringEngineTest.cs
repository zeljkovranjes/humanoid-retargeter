using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Editor;
using Sandbox;

// Opt-in playback in an isolated scratch project; never run against a user's editor.
public static class SmartPortFiringEngineTest
{
    static bool started;
    [EditorEvent.Frame]
    public static void Tick()
    {
        var root = Environment.GetEnvironmentVariable("HR_SMART_PORT_FIRING_PROJECT");
        if (started || Project.Current is null || string.IsNullOrEmpty(root)
            || !string.Equals(Path.GetFullPath(Project.Current.GetRootPath()).TrimEnd('/', '\\'),
                Path.GetFullPath(root).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
            || !AssetSystem.All.Any()) return;
        started = true; _ = Run(root);
    }

    static async Task Run(string root)
    {
        var data = new Dictionary<string, object>();
        void Save() => File.WriteAllText(Path.Combine(root, "firing-result.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            data["started"] = DateTime.UtcNow;
            var port = Environment.GetEnvironmentVariable("HR_SMART_PORT_GRIP_MODEL");
            if (string.IsNullOrWhiteSpace(port)) throw new Exception("Set HR_SMART_PORT_GRIP_MODEL to the ported Human Citizen asset");
            data["model"] = port; Save();
            var session = SceneEditorSession.CreateDefault(); session.MakeActive();
            using (session.Scene.Push())
            {
                foreach (var path in new[] { "models/player/human/frank_mp.vmdl", port })
                {
                    var go = session.Scene.CreateObject(); go.Name = path;
                    go.WorldPosition = new Vector3(0, path == port ? 35 : -35, 0);
                    var actor = go.Components.Create<SkinnedModelRenderer>();
                    actor.Model = Model.Load(path); actor.UseAnimGraph = true;
                    if (actor.Model.IsError) throw new Exception("Missing model " + path);
                }
                var camera = session.Scene.CreateObject(); camera.Name = "Firing Camera";
                camera.WorldPosition = new Vector3(120, 0, 80);
                camera.WorldRotation = Rotation.LookAt(new Vector3(0, 0, 45) - camera.WorldPosition);
                camera.Components.Create<CameraComponent>().FieldOfView = 60;
                foreach (var yaw in new[] { 25f, 145f, 265f })
                {
                    var light = session.Scene.CreateObject(); light.WorldRotation = Rotation.From(35, yaw, 0);
                    light.Components.Create<DirectionalLight>().LightColor = Color.White * .8f;
                }
            }
            EditorScene.Play(false, session); await Task.Delay(1500);
            if (!Game.IsPlaying || Game.ActiveScene is null || Game.ActiveScene.IsEditor) throw new Exception("Did not enter play mode");
            var actors = Game.ActiveScene.GetAllComponents<SkinnedModelRenderer>().ToArray();
            var observations = new List<object>(); data["observations"] = observations;
            foreach (var hold in new[] { 6, 5, 7 })
            foreach (var speed in new[] { 0f, 120f, 240f })
            {
                void Parameters(bool fire)
                {
                    foreach (var actor in actors)
                    {
                        actor.Set("b_grounded", true); actor.Set("holdtype", hold); actor.Set("weapon_pose", 0);
                        actor.Set("move_style", speed == 0 ? 0 : speed < 200 ? 4 : 5);
                        actor.Set("aim_body", Vector3.Forward); actor.Set("aim_body_weight", 1f);
                        actor.Set("aim_head", Vector3.Forward); actor.Set("aim_head_weight", 1f);
                        actor.Set("move_x", speed); actor.Set("move_speed", speed); actor.Set("move_groundspeed", speed);
                        actor.Set("wish_x", speed); actor.Set("wish_groundspeed", speed);
                        if (fire) actor.Set("b_attack", true);
                    }
                }
                for (var frame = 0; frame < 100; frame++) { Parameters(false); await Task.Delay(20); }
                var idle = actors.ToDictionary(a => a, a => RelativeHand(a));
                var rotations = actors.ToDictionary(a => a, a => a.SceneModel.GetAttachment("hold_R", true).Value.Rotation);
                var drift = actors.ToDictionary(a => a, a => 0f);
                var recoil = actors.ToDictionary(a => a, a => 0f);
                for (var frame = 0; frame < 150; frame++)
                {
                    Parameters(frame % 25 == 0); await Task.Delay(20);
                    if (hold == 6 && speed == 0 && (frame == 6 || frame == 12 || frame == 18))
                    {
                        var camera = Game.ActiveScene.GetAllComponents<CameraComponent>().Single(c => c.GameObject.Name == "Firing Camera");
                        var pixmap = new Pixmap(1200, 900);
                        if (!camera.RenderToPixmap(pixmap)) throw new Exception("Render failed");
                        File.WriteAllBytes(Path.Combine(root, $"firing-{frame}.png"), pixmap.GetPng());
                    }
                    foreach (var actor in actors)
                    {
                        drift[actor] = MathF.Max(drift[actor], RelativeHand(actor).Distance(idle[actor]));
                        var direction = actor.SceneModel.GetAttachment("hold_R", true).Value.Rotation.Forward;
                        recoil[actor] = MathF.Max(recoil[actor], MathF.Acos(Math.Clamp(Vector3.Dot(rotations[actor].Forward, direction), -1, 1)) * 180 / MathF.PI);
                    }
                }
                foreach (var actor in actors)
                    observations.Add(new { model = actor.Model.Name, hold, speed, drift = drift[actor], recoil = recoil[actor] });
                Save();
                foreach (var actor in actors)
                {
                    if (!float.IsFinite(drift[actor]) || drift[actor] > .15f)
                        throw new Exception($"Support hand slipped while firing: {actor.Model.Name}, hold {hold}, speed {speed}, drift {drift[actor]}");
                    if (!float.IsFinite(recoil[actor]) || recoil[actor] < .5f)
                        throw new Exception("Firing did not visibly recoil: " + actor.Model.Name);
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

    static Vector3 RelativeHand(SkinnedModelRenderer actor)
    {
        var grip = actor.SceneModel.GetAttachment("hold_R", true) ?? throw new Exception("Missing weapon attachment");
        var bone = actor.Model.Bones.AllBones.Single(b => string.Equals(b.Name, "hand_L", StringComparison.OrdinalIgnoreCase));
        return grip.Rotation.Inverse * (actor.SceneModel.GetBoneWorldTransform(bone.Name).Position - grip.Position);
    }
}
