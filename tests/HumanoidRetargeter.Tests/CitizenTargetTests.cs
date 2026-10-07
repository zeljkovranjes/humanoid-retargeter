using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Facade end-to-end against the built-in classic (4-finger) s&amp;box citizen target
/// (<see cref="RetargetTargetSpec.SboxCitizen"/>): real fixture bytes in, DMX/vmdl text out.
/// The source (mixamo) maps pinky roles, the citizen target has no pinky bones — the
/// conversion must succeed with the pinky roles simply unassigned on the target side.
/// </summary>
public class CitizenTargetTests
{
    private static string FixturePath(params string[] parts)
        => Path.Combine(new[] { AppContext.BaseDirectory, "fixtures" }.Concat(parts).ToArray());

    /// <summary>Resolves a repo-relative path by walking up to the .sbproj directory.</summary>
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found.");
    }

    private static readonly Lazy<RetargetTargetSpec> CitizenTarget = new(()
        => RetargetTargetSpec.SboxCitizen(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox_citizen.json"))));

    private static RetargetRequest ZombieCrawlRequest() => new()
    {
        SourceData = File.ReadAllBytes(FixturePath("fbx", "Zombie Crawl.fbx")),
        SourceFileName = "Zombie Crawl.fbx",
    };

    [Fact]
    public void SboxCitizen_SpecShape()
    {
        var spec = CitizenTarget.Value;
        Assert.Equal("sbox_citizen", spec.Rig.Name);
        Assert.Equal(RetargetTargetSpec.SboxSourceScale, spec.VmdlScale);
        Assert.Equal("models/citizen/citizen.vmdl", spec.BaseModelPath);
        Assert.Equal("pelvis", spec.DefaultRootBone);
        Assert.Equal(TargetUpAxis.YUpCm, spec.UpAxis);
    }

    [Fact]
    public void Convert_ZombieCrawl_SboxCitizenTarget_Succeeds()
    {
        var result = Retargeter.Convert(ZombieCrawlRequest(), CitizenTarget.Value);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);

        // DMX carries citizen bone channel pairs (position + orientation).
        Assert.False(string.IsNullOrEmpty(clip.DmxContent));
        Assert.Contains("pelvis_p", clip.DmxContent);
        Assert.Contains("pelvis_o", clip.DmxContent);
        Assert.Contains("spine_0_p", clip.DmxContent);
        Assert.Contains("spine_0_o", clip.DmxContent);
        Assert.Contains("hand_L_p", clip.DmxContent);

        // The citizen has no pinky bones at all — nothing pinky-named may appear.
        Assert.DoesNotContain("finger_pinky", clip.DmxContent);

        // Face bones are present in the skeleton AND carry rest-local channel pairs:
        // nothing re-drives them in a compiled sequence (the model's AnimConstraintList
        // never references eye/ear/face bones, and the engine's eye look-at only runs in
        // game), so channel-less face joints bake statically — eyes out of their sockets
        // in ModelDoc. Shipped fbx2dmx clips (dev/m0/ref_idlepose.dmx) carry them too.
        Assert.Contains("\"eye_L\"", clip.DmxContent);
        Assert.Contains("eye_L_p", clip.DmxContent);
        Assert.Contains("eye_L_o", clip.DmxContent);

        // Y-up cm rig: the DMX declares Y-up (resourcecompiler converts at compile time).
        Assert.Contains("\"upAxis\" \"string\" \"Y\"", clip.DmxContent);

        // The source (mixamo) maps the pinky roles; the target simply has no bone for them.
        // That must neither fail the clip nor add the human-male CopyPinky note (the classic
        // citizen base model has no pinky bones, hence no CopyPinky constraints to fight).
        Assert.NotNull(clip.Mapping);
        Assert.Equal("mixamo", clip.Mapping!.ProfileName);
        Assert.False(clip.Mapping.NeedsUserDecision);
        Assert.DoesNotContain(clip.Mapping.Notes, n => n.Contains("CopyPinky"));

        // Preview data: one local-XForm set per citizen bone per frame.
        Assert.NotNull(clip.SolvedFrames);
        Assert.All(clip.SolvedFrames!, f => Assert.Equal(95, f.Length));

        // The standalone vmdl points at the classic citizen base model.
        Assert.Contains("models/citizen/citizen.vmdl", result.StandaloneVmdl);
        Assert.Contains(clip.ClipName, result.StandaloneVmdl);
    }

    [Fact]
    public void Convert_SboxCitizenTarget_IsDeterministic()
    {
        var a = Retargeter.Convert(ZombieCrawlRequest(), CitizenTarget.Value);
        var b = Retargeter.Convert(ZombieCrawlRequest(), CitizenTarget.Value);
        Assert.Equal(a.Clips[0].DmxContent, b.Clips[0].DmxContent);
        Assert.Equal(a.StandaloneVmdl, b.StandaloneVmdl);
    }

    /// <summary>
    /// REGRESSION (eyes out of their sockets in ModelDoc, introduced by the face-bone
    /// reclassification in 3a1ab4b): face bones (eye/ear/face_lid) are ConstraintDriven by
    /// class but NOTHING re-drives them when a compiled sequence plays — the model's
    /// AnimConstraintList only references twist/helper bones and the engine's eye look-at
    /// runs in game only. A face joint left channel-less in the DMX bakes statically, so
    /// the eyeballs detach from the moving head in ModelDoc (the editor preview was fine:
    /// it drives every bone via SetBoneOverride and never reads the compiled animation).
    /// Contract pinned here, matching both the pre-regression output and the shipped
    /// fbx2dmx reference (dev/m0/ref_idlepose.dmx):
    /// (1) every face bone carries a position+orientation channel pair,
    /// (2) those channels are CONSTANT at the bone's rest local (the eyes ride the head),
    /// (3) twist/helper ConstraintDriven bones stay channel-less joints (the constraint
    ///     list drives them on every evaluated frame — design §3 unchanged).
    /// </summary>
    [Fact]
    public void Convert_SboxCitizenTarget_FaceBonesCarryConstantRestChannels()
    {
        var result = Retargeter.Convert(ZombieCrawlRequest(), CitizenTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        var dmx = clip.DmxContent;

        // (1) channel pairs for every face bone; (3) none for twist/helper/clothing bones.
        string[] faceBones =
        {
            "eye_L", "eye_R", "ear_L", "ear_R",
            "face_lid_lower_L", "face_lid_lower_R", "face_lid_upper_L", "face_lid_upper_R",
        };
        foreach (var bone in faceBones)
        {
            Assert.Contains($"\"{bone}_p\"", dmx);
            Assert.Contains($"\"{bone}_o\"", dmx);
        }
        string[] engineDrivenBones =
        {
            "arm_upper_L_twist1", "arm_lower_R_twist0", "leg_upper_R_twist1",
            "arm_elbow_helper_L", "leg_knee_helper_R", "neck_clothing",
        };
        foreach (var bone in engineDrivenBones)
        {
            Assert.Contains($"\"{bone}\"", dmx);        // joint + bind stay in the DMX
            Assert.DoesNotContain($"\"{bone}_p\"", dmx); // ...but no channels
            Assert.DoesNotContain($"\"{bone}_o\"", dmx);
        }

        // (2) the eye channels are constant at the rest local: every value in the eye_L
        // position log equals the eye_L bind (DmeTransform "position"), every frame.
        var bindPos = System.Text.RegularExpressions.Regex.Match(dmx,
            "\"name\" \"string\" \"eye_L\"\\s*\n\\s*\"position\" \"vector3\" \"([^\"]+)\"");
        Assert.True(bindPos.Success, "eye_L bind DmeTransform not found in DMX");

        var channel = System.Text.RegularExpressions.Regex.Match(dmx,
            "\"eye_L_p\"[\\s\\S]*?\"values\" \"vector3_array\"\\s*\\[([\\s\\S]*?)\\]");
        Assert.True(channel.Success, "eye_L_p channel value array not found in DMX");
        var values = System.Text.RegularExpressions.Regex.Matches(channel.Groups[1].Value, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(clip.SolvedFrames!.Count, values.Count);
        Assert.All(values, v => Assert.Equal(bindPos.Groups[1].Value, v));
    }
}
