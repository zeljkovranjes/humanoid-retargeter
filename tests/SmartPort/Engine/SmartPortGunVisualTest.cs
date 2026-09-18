using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Editor;
using Sandbox;

// Visual evidence only: no corrective offsets, bone merging or scaling.
public static class SmartPortGunVisualTest
{
    static bool started;
    static readonly Dictionary<SkinnedModelRenderer, ModelRenderer> guns = new();

    [EditorEvent.Frame]
    public static void Tick()
    {
        foreach (var pair in guns)
            if (pair.Key.IsValid() && pair.Value.IsValid() && pair.Key.SceneModel is not null)
                pair.Value.WorldTransform = pair.Key.SceneModel.GetAttachment("hold_R", true)
                    ?? throw new Exception("Missing weapon socket");
        var root = Environment.GetEnvironmentVariable("HR_SMART_PORT_GUN_VISUAL_PROJECT");
        if (started || Project.Current is null || string.IsNullOrEmpty(root)
            || !string.Equals(Path.GetFullPath(Project.Current.GetRootPath()).TrimEnd('/', '\\'),
                Path.GetFullPath(root).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
            || !AssetSystem.All.Any()) return;
        started = true; _ = Run(root);
    }

    static async Task Run(string root)
    {
        var data = new Dictionary<string, object>();
        void Save() => File.WriteAllText(Path.Combine(root, "gun-visual-result.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            var port = Environment.GetEnvironmentVariable("HR_SMART_PORT_GRIP_MODEL");
            if (string.IsNullOrWhiteSpace(port)) throw new Exception("Set HR_SMART_PORT_GRIP_MODEL to the custom Citizen Smart Port output");
            const string source = "models/citizen/citizen.vmdl";
            const string gun = "models/weapons/sbox_assault_m4a1/w_m4a1.vmdl";
            data["started"] = DateTime.UtcNow; data["model"] = port; data["gun"] = gun;
            data["attachment"] = "hold_R"; data["offset"] = "identity"; Save();
            var session = SceneEditorSession.CreateDefault(); session.MakeActive();
            using (session.Scene.Push())
            {
                foreach (var path in new[] { source, port })
                {
                    var go = session.Scene.CreateObject(); go.Name = path;
                    var actor = go.Components.Create<SkinnedModelRenderer>();
                    actor.Model = Model.Load(path); actor.UseAnimGraph = true;
                    if (actor.Model.IsError) throw new Exception("Missing character " + path);
                }
                var camera = session.Scene.CreateObject(); camera.Name = "Gun Camera";
                camera.WorldPosition = new Vector3(95, 85, 70);
                camera.WorldRotation = Rotation.LookAt(new Vector3(8, 0, 45) - camera.WorldPosition);
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
            using (Game.ActiveScene.Push())
                foreach (var actor in actors)
                {
                    var go = Game.ActiveScene.CreateObject(); go.Name = "M4A1 " + actor.Model.Name;
                    var renderer = go.Components.Create<ModelRenderer>(); renderer.Model = Model.Load(gun);
                    if (renderer.Model.IsError) throw new Exception("Missing M4A1 model/dependencies");
                    guns.Add(actor, renderer);
                }
            var captures = new List<string>(); data["screenshots"] = captures;
            var grips = new List<object>(); data["grips"] = grips;
            foreach (var speed in new[] { 0f, 120f, 240f })
            foreach (var actor in actors)
            {
                foreach (var other in actors) other.WorldPosition = other == actor ? Vector3.Zero : new Vector3(0, 1000, 0);
                for (var frame = 0; frame < 120; frame++)
                {
                    actor.Set("b_grounded", true); actor.Set("holdtype", 2); actor.Set("weapon_pose", 0);
                    actor.Set("move_style", speed == 0 ? 0 : speed < 200 ? 4 : 5);
                    actor.Set("aim_body", Vector3.Forward); actor.Set("aim_body_weight", 1f);
                    actor.Set("aim_head", Vector3.Forward); actor.Set("aim_head_weight", 1f);
                    actor.Set("move_x", speed); actor.Set("move_speed", speed); actor.Set("move_groundspeed", speed);
                    actor.Set("wish_x", speed); actor.Set("wish_groundspeed", speed);
                    await Task.Delay(20);
                }
                foreach (var fire in new[] { false, true })
                {
                    if (fire) { actor.Set("b_attack", true); await Task.Delay(100); }
                    var socket = actor.SceneModel.GetAttachment("hold_R", true) ?? throw new Exception("Missing grip");
                    guns[actor].WorldTransform = socket;
                    foreach (var hand in new[] { "hand_L", "hand_R" })
                    {
                        var bone = actor.Model.Bones.AllBones.Single(b => string.Equals(b.Name, hand, StringComparison.OrdinalIgnoreCase));
                        var relative = socket.Rotation.Inverse * (actor.SceneModel.GetBoneWorldTransform(bone.Name).Position - socket.Position);
                        grips.Add(new { model = actor.Model.Name, hand, speed, fire, relative = relative.ToString() });
                    }
                    var camera = Game.ActiveScene.GetAllComponents<CameraComponent>().Single(c => c.GameObject.Name == "Gun Camera");
                    var pixmap = new Pixmap(1200, 1200);
                    if (!camera.RenderToPixmap(pixmap)) throw new Exception("Render failed");
                    var name = $"gun-raw-{(actor.Model.Name == source ? "source" : "custom")}-{speed}-{(fire ? "fire" : "hold")}.png";
                    File.WriteAllBytes(Path.Combine(root, name), pixmap.GetPng()); captures.Add(name); Save();
                    actor.Set("b_attack", false);
                }
            }
            data["captured"] = true;
        }
        catch (Exception e) { data["error"] = e.ToString(); }
        finally
        {
            data["completed"] = true; Save(); guns.Clear();
            if (Game.IsPlaying) EditorScene.Stop();
            await Task.Delay(1000); EditorUtility.Quit(true);
        }
    }
}
