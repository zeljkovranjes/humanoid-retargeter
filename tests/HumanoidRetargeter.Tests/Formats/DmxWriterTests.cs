using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using HumanoidRetargeter.Core.Formats.Dmx;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Formats;

public class DmxWriterTests
{
    // ---------------------------------------------------------------- inputs

    private static SkeletonModel GoldenSkeleton()
        => SkeletonModel.Create(new[]
        {
            new BoneDefinition("hip", null, new XForm(new Vector3(1f, 2f, 3f), Quaternion.Identity)),
            new BoneDefinition("knee", "hip",
                new XForm(new Vector3(0f, 4f, 0f), new Quaternion(0.5f, 0.5f, 0.5f, 0.5f))),
        });

    private static Clip GoldenClip()
    {
        var frames = new List<XForm[]>();
        for (var i = 0; i < 3; i++)
        {
            frames.Add(new[]
            {
                new XForm(new Vector3(1f, 2f, 3f + 0.5f * i), Quaternion.Identity),
                new XForm(new Vector3(0f, 4f, 0f), new Quaternion(0.5f, 0.5f, 0.5f, 0.5f)),
            });
        }

        return new Clip("anim", 30f, false, frames);
    }

    private static DmxWriteOptions GoldenOptions()
        => new() { Name = "golden", SourceNote = "golden.fbx" };

    private static string WriteGolden()
        => DmxWriter.Write(GoldenSkeleton(), GoldenClip(), GoldenOptions());

    // ---------------------------------------------------------------- 1. golden string

    // Hand-written following dev/m0/ref_idlepose.dmx (fbx2dmx output) element-for-element:
    // root DmElement with inline DmeModel (children/jointList GUID refs, bind DmeTransformList,
    // axisSystem, makefile, exportTags), a second top-level DmeAnimationList with explicit id,
    // then one top-level DmeTransform and DmeJoint per bone. GUIDs are the deterministic
    // MD5-derived values for options.Name "golden" (precomputed by hand, scheme:
    // MD5(name + "\n" + path) -> Guid bytes).
    private const string GoldenExpected =
        "<!-- dmx encoding keyvalues2_noids 4 format model 22 -->\n" +
        "\"DmElement\"\n" +
        "{\n" +
        "\t\"name\" \"string\" \"root\"\n" +
        "\t\"skeleton\" \"DmeModel\"\n" +
        "\t{\n" +
        "\t\t\"name\" \"string\" \"golden\"\n" +
        "\t\t\"transform\" \"DmeTransform\"\n" +
        "\t\t{\n" +
        "\t\t\t\"position\" \"vector3\" \"0 0 0\"\n" +
        "\t\t\t\"orientation\" \"quaternion\" \"0 0 0 1\"\n" +
        "\t\t}\n" +
        "\t\t\n" +
        "\t\t\"shape\" \"element\" \"\"\n" +
        "\t\t\"visible\" \"bool\" \"1\"\n" +
        "\t\t\"children\" \"element_array\" \n" +
        "\t\t[\n" +
        "\t\t\t\"element\" \"4deafc74-7bc5-4395-5718-c4d5cac742dc\"\n" +
        "\t\t]\n" +
        "\t\t\"jointList\" \"element_array\" \n" +
        "\t\t[\n" +
        "\t\t\t\"element\" \"4deafc74-7bc5-4395-5718-c4d5cac742dc\",\n" +
        "\t\t\t\"element\" \"4dc7f450-b76e-db2e-c29c-c4867ab66e82\"\n" +
        "\t\t]\n" +
        "\t\t\"baseStates\" \"element_array\" \n" +
        "\t\t[\n" +
        "\t\t\t\"DmeTransformList\"\n" +
        "\t\t\t{\n" +
        "\t\t\t\t\"name\" \"string\" \"bind\"\n" +
        "\t\t\t\t\"transforms\" \"element_array\" \n" +
        "\t\t\t\t[\n" +
        "\t\t\t\t\t\"DmeTransform\"\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\"name\" \"string\" \"hip\"\n" +
        "\t\t\t\t\t\t\"position\" \"vector3\" \"1 2 3\"\n" +
        "\t\t\t\t\t\t\"orientation\" \"quaternion\" \"0 0 0 1\"\n" +
        "\t\t\t\t\t},\n" +
        "\t\t\t\t\t\"DmeTransform\"\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\"name\" \"string\" \"knee\"\n" +
        "\t\t\t\t\t\t\"position\" \"vector3\" \"0 4 0\"\n" +
        "\t\t\t\t\t\t\"orientation\" \"quaternion\" \"0.5 0.5 0.5 0.5\"\n" +
        "\t\t\t\t\t}\n" +
        "\t\t\t\t]\n" +
        "\t\t\t}\n" +
        "\t\t]\n" +
        "\t\t\"upAxis\" \"string\" \"Y\"\n" +
        "\t\t\"axisSystem\" \"DmeAxisSystem\"\n" +
        "\t\t{\n" +
        "\t\t\t\"upAxis\" \"int\" \"2\"\n" +
        "\t\t\t\"forwardParity\" \"int\" \"2\"\n" +
        "\t\t\t\"coordSys\" \"int\" \"0\"\n" +
        "\t\t}\n" +
        "\t\t\n" +
        "\t\t\"animationList\" \"element\" \"91d3ec4b-3362-8761-8ecf-913e72a7aa43\"\n" +
        "\t}\n" +
        "\t\n" +
        "\t\"makefile\" \"DmeDCCMakefile\"\n" +
        "\t{\n" +
        "\t\t\"name\" \"string\" \"makefile\"\n" +
        "\t\t\"sources\" \"element_array\" \n" +
        "\t\t[\n" +
        "\t\t\t\"DmeSource\"\n" +
        "\t\t\t{\n" +
        "\t\t\t\t\"name\" \"string\" \"golden.fbx\"\n" +
        "\t\t\t}\n" +
        "\t\t]\n" +
        "\t}\n" +
        "\t\n" +
        "\t\"exportTags\" \"DmeExportTags\"\n" +
        "\t{\n" +
        "\t\t\"name\" \"string\" \"exportTags\"\n" +
        "\t\t\"date\" \"string\" \"2026/01/01\"\n" +
        "\t\t\"time\" \"string\" \"12:00:00 am\"\n" +
        "\t\t\"user\" \"string\" \"retargeter\"\n" +
        "\t\t\"machine\" \"string\" \"retargeter\"\n" +
        "\t\t\"app\" \"string\" \"humanoid-retargeter\"\n" +
        "\t\t\"appVersion\" \"string\" \"1.0\"\n" +
        "\t\t\"cmdLine\" \"string\" \"humanoid-retargeter\"\n" +
        "\t\t\"pwd\" \"string\" \"\"\n" +
        "\t}\n" +
        "\t\n" +
        "\t\"animationList\" \"element\" \"91d3ec4b-3362-8761-8ecf-913e72a7aa43\"\n" +
        "}\n" +
        "\n" +
        "\"DmeAnimationList\"\n" +
        "{\n" +
        "\t\"id\" \"elementid\" \"91d3ec4b-3362-8761-8ecf-913e72a7aa43\"\n" +
        "\t\"name\" \"string\" \"anim\"\n" +
        "\t\"animations\" \"element_array\" \n" +
        "\t[\n" +
        "\t\t\"DmeChannelsClip\"\n" +
        "\t\t{\n" +
        "\t\t\t\"name\" \"string\" \"anim\"\n" +
        "\t\t\t\"timeFrame\" \"DmeTimeFrame\"\n" +
        "\t\t\t{\n" +
        "\t\t\t\t\"start\" \"time\" \"0.0000\"\n" +
        "\t\t\t\t\"duration\" \"time\" \"0.0667\"\n" +
        "\t\t\t\t\"offset\" \"time\" \"0.0000\"\n" +
        "\t\t\t\t\"scale\" \"float\" \"1\"\n" +
        "\t\t\t}\n" +
        "\t\t\t\n" +
        "\t\t\t\"color\" \"color\" \"0 0 0 0\"\n" +
        "\t\t\t\"text\" \"string\" \"\"\n" +
        "\t\t\t\"mute\" \"bool\" \"0\"\n" +
        "\t\t\t\"trackGroups\" \"element_array\" \n" +
        "\t\t\t[\n" +
        "\t\t\t]\n" +
        "\t\t\t\"displayScale\" \"float\" \"1\"\n" +
        "\t\t\t\"channels\" \"element_array\" \n" +
        "\t\t\t[\n" +
        ChannelHipP +
        ChannelHipO +
        ChannelKneeP +
        ChannelKneeO +
        "\t\t\t]\n" +
        "\t\t\t\"frameRate\" \"int\" \"30\"\n" +
        "\t\t}\n" +
        "\t]\n" +
        "}\n" +
        "\n" +
        "\"DmeTransform\"\n" +
        "{\n" +
        "\t\"id\" \"elementid\" \"cc650a2b-9bb1-dbea-8586-d2532ac9c2d8\"\n" +
        "\t\"name\" \"string\" \"hip\"\n" +
        "\t\"position\" \"vector3\" \"1 2 3\"\n" +
        "\t\"orientation\" \"quaternion\" \"0 0 0 1\"\n" +
        "}\n" +
        "\n" +
        "\"DmeTransform\"\n" +
        "{\n" +
        "\t\"id\" \"elementid\" \"fb451bde-2c07-d6c8-ae32-0d6a1e3cc068\"\n" +
        "\t\"name\" \"string\" \"knee\"\n" +
        "\t\"position\" \"vector3\" \"0 4 0\"\n" +
        "\t\"orientation\" \"quaternion\" \"0.5 0.5 0.5 0.5\"\n" +
        "}\n" +
        "\n" +
        "\"DmeJoint\"\n" +
        "{\n" +
        "\t\"id\" \"elementid\" \"4deafc74-7bc5-4395-5718-c4d5cac742dc\"\n" +
        "\t\"name\" \"string\" \"hip\"\n" +
        "\t\"transform\" \"element\" \"cc650a2b-9bb1-dbea-8586-d2532ac9c2d8\"\n" +
        "\t\"shape\" \"element\" \"\"\n" +
        "\t\"visible\" \"bool\" \"1\"\n" +
        "\t\"children\" \"element_array\" \n" +
        "\t[\n" +
        "\t\t\"element\" \"4dc7f450-b76e-db2e-c29c-c4867ab66e82\"\n" +
        "\t]\n" +
        "}\n" +
        "\n" +
        "\"DmeJoint\"\n" +
        "{\n" +
        "\t\"id\" \"elementid\" \"4dc7f450-b76e-db2e-c29c-c4867ab66e82\"\n" +
        "\t\"name\" \"string\" \"knee\"\n" +
        "\t\"transform\" \"element\" \"fb451bde-2c07-d6c8-ae32-0d6a1e3cc068\"\n" +
        "\t\"shape\" \"element\" \"\"\n" +
        "\t\"visible\" \"bool\" \"1\"\n" +
        "\t\"children\" \"element_array\" \n" +
        "\t[\n" +
        "\t]\n" +
        "}\n" +
        "\n";

    private const string ChannelHipP =
        "\t\t\t\t\"DmeChannel\"\n" +
        "\t\t\t\t{\n" +
        "\t\t\t\t\t\"name\" \"string\" \"hip_p\"\n" +
        "\t\t\t\t\t\"fromElement\" \"element\" \"\"\n" +
        "\t\t\t\t\t\"fromAttribute\" \"string\" \"\"\n" +
        "\t\t\t\t\t\"fromIndex\" \"int\" \"0\"\n" +
        "\t\t\t\t\t\"toElement\" \"element\" \"cc650a2b-9bb1-dbea-8586-d2532ac9c2d8\"\n" +
        "\t\t\t\t\t\"toAttribute\" \"string\" \"position\"\n" +
        "\t\t\t\t\t\"toIndex\" \"int\" \"0\"\n" +
        "\t\t\t\t\t\"mode\" \"int\" \"3\"\n" +
        "\t\t\t\t\t\"log\" \"DmeVector3Log\"\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\"name\" \"string\" \"vector3 log\"\n" +
        "\t\t\t\t\t\t\"layers\" \"element_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\"DmeVector3LogLayer\"\n" +
        "\t\t\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\t\t\"name\" \"string\" \"vector3 log\"\n" +
        "\t\t\t\t\t\t\t\t\"times\" \"time_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0000\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0333\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0667\"\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"curvetypes\" \"int_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"values\" \"vector3_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t\t\"1 2 3\",\n" +
        "\t\t\t\t\t\t\t\t\t\"1 2 3.5\",\n" +
        "\t\t\t\t\t\t\t\t\t\"1 2 4\"\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"compressed\" \"binary\" \n" +
        "\t\t\t\t\t\t\t\t\"\n" +
        "\t\t\t\t\t\t\t\t\"\n" +
        "\t\t\t\t\t\t\t}\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"curveinfo\" \"element\" \"\"\n" +
        "\t\t\t\t\t\t\"usedefaultvalue\" \"bool\" \"0\"\n" +
        "\t\t\t\t\t\t\"defaultvalue\" \"vector3\" \"0 0 0\"\n" +
        "\t\t\t\t\t\t\"bookmarksX\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"bookmarksY\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"bookmarksZ\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t}\n" +
        "\t\t\t\t\t\n" +
        "\t\t\t\t},\n";

    private const string ChannelHipO =
        "\t\t\t\t\"DmeChannel\"\n" +
        "\t\t\t\t{\n" +
        "\t\t\t\t\t\"name\" \"string\" \"hip_o\"\n" +
        "\t\t\t\t\t\"fromElement\" \"element\" \"\"\n" +
        "\t\t\t\t\t\"fromAttribute\" \"string\" \"\"\n" +
        "\t\t\t\t\t\"fromIndex\" \"int\" \"0\"\n" +
        "\t\t\t\t\t\"toElement\" \"element\" \"cc650a2b-9bb1-dbea-8586-d2532ac9c2d8\"\n" +
        "\t\t\t\t\t\"toAttribute\" \"string\" \"orientation\"\n" +
        "\t\t\t\t\t\"toIndex\" \"int\" \"0\"\n" +
        "\t\t\t\t\t\"mode\" \"int\" \"3\"\n" +
        "\t\t\t\t\t\"log\" \"DmeQuaternionLog\"\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\"name\" \"string\" \"quaternion log\"\n" +
        "\t\t\t\t\t\t\"layers\" \"element_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\"DmeQuaternionLogLayer\"\n" +
        "\t\t\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\t\t\"name\" \"string\" \"quaternion log\"\n" +
        "\t\t\t\t\t\t\t\t\"times\" \"time_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0000\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0333\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0667\"\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"curvetypes\" \"int_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"values\" \"quaternion_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t\t\"0 0 0 1\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0 0 0 1\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0 0 0 1\"\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"compressed\" \"binary\" \n" +
        "\t\t\t\t\t\t\t\t\"\n" +
        "\t\t\t\t\t\t\t\t\"\n" +
        "\t\t\t\t\t\t\t}\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"curveinfo\" \"element\" \"\"\n" +
        "\t\t\t\t\t\t\"usedefaultvalue\" \"bool\" \"0\"\n" +
        "\t\t\t\t\t\t\"defaultvalue\" \"quaternion\" \"0 0 0 1\"\n" +
        "\t\t\t\t\t\t\"bookmarksX\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"bookmarksY\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"bookmarksZ\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t}\n" +
        "\t\t\t\t\t\n" +
        "\t\t\t\t},\n";

    private const string ChannelKneeP =
        "\t\t\t\t\"DmeChannel\"\n" +
        "\t\t\t\t{\n" +
        "\t\t\t\t\t\"name\" \"string\" \"knee_p\"\n" +
        "\t\t\t\t\t\"fromElement\" \"element\" \"\"\n" +
        "\t\t\t\t\t\"fromAttribute\" \"string\" \"\"\n" +
        "\t\t\t\t\t\"fromIndex\" \"int\" \"0\"\n" +
        "\t\t\t\t\t\"toElement\" \"element\" \"fb451bde-2c07-d6c8-ae32-0d6a1e3cc068\"\n" +
        "\t\t\t\t\t\"toAttribute\" \"string\" \"position\"\n" +
        "\t\t\t\t\t\"toIndex\" \"int\" \"0\"\n" +
        "\t\t\t\t\t\"mode\" \"int\" \"3\"\n" +
        "\t\t\t\t\t\"log\" \"DmeVector3Log\"\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\"name\" \"string\" \"vector3 log\"\n" +
        "\t\t\t\t\t\t\"layers\" \"element_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\"DmeVector3LogLayer\"\n" +
        "\t\t\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\t\t\"name\" \"string\" \"vector3 log\"\n" +
        "\t\t\t\t\t\t\t\t\"times\" \"time_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0000\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0333\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0667\"\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"curvetypes\" \"int_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"values\" \"vector3_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t\t\"0 4 0\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0 4 0\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0 4 0\"\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"compressed\" \"binary\" \n" +
        "\t\t\t\t\t\t\t\t\"\n" +
        "\t\t\t\t\t\t\t\t\"\n" +
        "\t\t\t\t\t\t\t}\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"curveinfo\" \"element\" \"\"\n" +
        "\t\t\t\t\t\t\"usedefaultvalue\" \"bool\" \"0\"\n" +
        "\t\t\t\t\t\t\"defaultvalue\" \"vector3\" \"0 0 0\"\n" +
        "\t\t\t\t\t\t\"bookmarksX\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"bookmarksY\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"bookmarksZ\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t}\n" +
        "\t\t\t\t\t\n" +
        "\t\t\t\t},\n";

    private const string ChannelKneeO =
        "\t\t\t\t\"DmeChannel\"\n" +
        "\t\t\t\t{\n" +
        "\t\t\t\t\t\"name\" \"string\" \"knee_o\"\n" +
        "\t\t\t\t\t\"fromElement\" \"element\" \"\"\n" +
        "\t\t\t\t\t\"fromAttribute\" \"string\" \"\"\n" +
        "\t\t\t\t\t\"fromIndex\" \"int\" \"0\"\n" +
        "\t\t\t\t\t\"toElement\" \"element\" \"fb451bde-2c07-d6c8-ae32-0d6a1e3cc068\"\n" +
        "\t\t\t\t\t\"toAttribute\" \"string\" \"orientation\"\n" +
        "\t\t\t\t\t\"toIndex\" \"int\" \"0\"\n" +
        "\t\t\t\t\t\"mode\" \"int\" \"3\"\n" +
        "\t\t\t\t\t\"log\" \"DmeQuaternionLog\"\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\"name\" \"string\" \"quaternion log\"\n" +
        "\t\t\t\t\t\t\"layers\" \"element_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\"DmeQuaternionLogLayer\"\n" +
        "\t\t\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\t\t\"name\" \"string\" \"quaternion log\"\n" +
        "\t\t\t\t\t\t\t\t\"times\" \"time_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0000\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0333\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.0667\"\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"curvetypes\" \"int_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"values\" \"quaternion_array\" \n" +
        "\t\t\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t\t\t\"0.5 0.5 0.5 0.5\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.5 0.5 0.5 0.5\",\n" +
        "\t\t\t\t\t\t\t\t\t\"0.5 0.5 0.5 0.5\"\n" +
        "\t\t\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\t\t\"compressed\" \"binary\" \n" +
        "\t\t\t\t\t\t\t\t\"\n" +
        "\t\t\t\t\t\t\t\t\"\n" +
        "\t\t\t\t\t\t\t}\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"curveinfo\" \"element\" \"\"\n" +
        "\t\t\t\t\t\t\"usedefaultvalue\" \"bool\" \"0\"\n" +
        "\t\t\t\t\t\t\"defaultvalue\" \"quaternion\" \"0 0 0 1\"\n" +
        "\t\t\t\t\t\t\"bookmarksX\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"bookmarksY\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t\t\"bookmarksZ\" \"time_array\" \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t]\n" +
        "\t\t\t\t\t}\n" +
        "\t\t\t\t\t\n" +
        "\t\t\t\t}\n";

    [Fact]
    public void Write_TwoBoneThreeFrames_MatchesGoldenString()
    {
        var expected = GoldenExpected.Replace("\n", "\r\n");
        var actual = WriteGolden();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Write_GuidScheme_IsMd5OfNameAndPath()
    {
        // Independent re-derivation of the GUID scheme so the golden GUIDs are not
        // self-fulfilling: MD5 over UTF8("<name>\n<path>") interpreted as Guid bytes.
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("golden\nanimationList"));
        Assert.Equal(new Guid(hash), DmxWriter.ElementGuid("golden", "animationList"));
        Assert.Equal("91d3ec4b-3362-8761-8ecf-913e72a7aa43",
            DmxWriter.ElementGuid("golden", "animationList").ToString());
    }

    // ---------------------------------------------------------------- 2. determinism

    [Fact]
    public void Write_SameInputTwice_ProducesIdenticalOutput()
    {
        Assert.Equal(WriteGolden(), WriteGolden());
    }

    // ---------------------------------------------------------------- 3. culture invariance

    [Fact]
    public void Write_UnderGermanCulture_OutputUnchanged()
    {
        var invariantOutput = WriteGolden();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(invariantOutput, WriteGolden());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---------------------------------------------------------------- 4. structural audit of own output

    [Fact]
    public void Write_Structure_ChannelsTimesAndJointListConsistent()
    {
        var skeleton = GoldenSkeleton();
        var clip = GoldenClip();
        var output = DmxWriter.Write(skeleton, clip, GoldenOptions());

        // One position + one orientation channel per bone.
        Assert.Equal(skeleton.Count * 2, Regex.Matches(output, "\"DmeChannel\"").Count);
        foreach (var bone in skeleton.Bones)
        {
            Assert.Contains($"\"name\" \"string\" \"{bone.Name}_p\"", output);
            Assert.Contains($"\"name\" \"string\" \"{bone.Name}_o\"", output);
        }

        // jointList carries exactly one element ref per bone.
        var jointList = Regex.Match(output,
            "\"jointList\" \"element_array\" \r\n\t\t\\[(.*?)\\]", RegexOptions.Singleline);
        Assert.True(jointList.Success, "jointList array not found");
        Assert.Equal(skeleton.Count, Regex.Matches(jointList.Groups[1].Value, "\"element\"").Count);

        // Every times array holds one entry per frame.
        foreach (Match m in Regex.Matches(output,
            "\"times\" \"time_array\" \r\n\t*\\[(.*?)\\]", RegexOptions.Singleline))
        {
            var entries = Regex.Matches(m.Groups[1].Value, "\"[0-9]+\\.[0-9]{4}\"");
            Assert.Equal(clip.FrameCount, entries.Count);
        }

        // One top-level DmeJoint and one channel-target DmeTransform with id per bone.
        Assert.Equal(skeleton.Count, Regex.Matches(output, "^\"DmeJoint\"", RegexOptions.Multiline).Count);
        Assert.Equal(skeleton.Count, Regex.Matches(output, "^\"DmeTransform\"", RegexOptions.Multiline).Count);
    }

    // ---------------------------------------------------------------- channel exclusion

    [Fact]
    public void Write_ChannelExcludedBones_OmitsChannelPairButKeepsJointAndBind()
    {
        var skeleton = GoldenSkeleton();
        var clip = GoldenClip();
        var options = GoldenOptions();
        options.ChannelExcludedBones = new HashSet<int> { 1 }; // "knee"

        var output = DmxWriter.Write(skeleton, clip, options);

        // No channel pair for the excluded bone, channels intact for the rest.
        Assert.DoesNotContain("\"name\" \"string\" \"knee_p\"", output);
        Assert.DoesNotContain("\"name\" \"string\" \"knee_o\"", output);
        Assert.Contains("\"name\" \"string\" \"hip_p\"", output);
        Assert.Contains("\"name\" \"string\" \"hip_o\"", output);
        Assert.Equal(2, Regex.Matches(output, "\"DmeChannel\"").Count);

        // The excluded bone keeps its DmeJoint, jointList entry and bind transform.
        Assert.Equal(skeleton.Count, Regex.Matches(output, "^\"DmeJoint\"", RegexOptions.Multiline).Count);
        Assert.Equal(skeleton.Count, Regex.Matches(output, "^\"DmeTransform\"", RegexOptions.Multiline).Count);
        Assert.Contains("\"name\" \"string\" \"knee\"", output);
        var jointList = Regex.Match(output,
            "\"jointList\" \"element_array\" \r\n\t\t\\[(.*?)\\]", RegexOptions.Singleline);
        Assert.True(jointList.Success, "jointList array not found");
        Assert.Equal(skeleton.Count, Regex.Matches(jointList.Groups[1].Value, "\"element\"").Count);

        // The remaining last channel must close the array without a trailing comma.
        Assert.DoesNotContain("},\r\n\t\t\t]", output);
    }

    [Fact]
    public void Write_ChannelExcludedBonesNull_MatchesDefaultOutput()
    {
        var options = GoldenOptions();
        options.ChannelExcludedBones = null;
        Assert.Equal(WriteGolden(), DmxWriter.Write(GoldenSkeleton(), GoldenClip(), options));
    }

    [Fact]
    public void Write_AllBonesChannelExcluded_EmptyChannelsArray()
    {
        var options = GoldenOptions();
        options.ChannelExcludedBones = new HashSet<int> { 0, 1 };
        var output = DmxWriter.Write(GoldenSkeleton(), GoldenClip(), options);
        Assert.Empty(Regex.Matches(output, "\"DmeChannel\""));
        Assert.Contains("\"channels\" \"element_array\" \r\n\t\t\t[\r\n\t\t\t]", output);
    }

    // ---------------------------------------------------------------- quaternion hemisphere continuity

    [Fact]
    public void Write_HemisphereFlippedFrames_SuccessiveQuaternionValuesHaveNonNegativeDot()
    {
        // Frame 1 carries -q·delta: the same rotation as q·delta but on the opposite
        // hemisphere. The writer must re-align it so the engine's sample interpolation
        // does not spin the long way around.
        var delta = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.1f);
        var skeleton = GoldenSkeleton();
        var frames = new List<XForm[]>
        {
            new[]
            {
                new XForm(new Vector3(1f, 2f, 3f), Quaternion.Identity),
                new XForm(new Vector3(0f, 4f, 0f), new Quaternion(0.5f, 0.5f, 0.5f, 0.5f)),
            },
            new[]
            {
                new XForm(new Vector3(1f, 2f, 3f), Quaternion.Negate(delta)),
                new XForm(new Vector3(0f, 4f, 0f), Quaternion.Negate(new Quaternion(0.5f, 0.5f, 0.5f, 0.5f))),
            },
        };
        var clip = new Clip("anim", 30f, false, frames);

        var output = DmxWriter.Write(skeleton, clip, GoldenOptions());

        var arrays = Regex.Matches(output,
            "\"values\" \"quaternion_array\" \r\n\t*\\[(.*?)\\]", RegexOptions.Singleline);
        Assert.Equal(skeleton.Count, arrays.Count);

        foreach (Match m in arrays)
        {
            var quats = Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"")
                .Select(v => v.Groups[1].Value.Split(' '))
                .Select(p => new Quaternion(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture),
                    float.Parse(p[2], CultureInfo.InvariantCulture),
                    float.Parse(p[3], CultureInfo.InvariantCulture)))
                .ToList();
            Assert.Equal(clip.FrameCount, quats.Count);
            for (var i = 1; i < quats.Count; i++)
                Assert.True(Quaternion.Dot(quats[i - 1], quats[i]) >= 0f,
                    $"hemisphere flip survived between samples {i - 1} and {i}: {quats[i - 1]} -> {quats[i]}");
        }

        // The clip itself must not have been mutated by the writer.
        Assert.Equal(Quaternion.Negate(delta), clip.Frames[1][0].Rot);
    }

    [Fact]
    public void Write_FrameCountMismatch_Throws()
    {
        var skeleton = GoldenSkeleton();
        var clip = new Clip("bad", 30f, false, new List<XForm[]> { new XForm[1] });
        Assert.Throws<ArgumentException>(() => DmxWriter.Write(skeleton, clip, GoldenOptions()));
    }

    [Fact]
    public void Write_EmptyClip_Throws()
    {
        var skeleton = GoldenSkeleton();
        var clip = new Clip("empty", 30f, false);
        Assert.Throws<ArgumentException>(() => DmxWriter.Write(skeleton, clip, GoldenOptions()));
    }

    // ---------------------------------------------------------------- 5. reference-shape audit (citizen rig)

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Write_CitizenRig_EveryBoneIsJointAndChannelPair_HeaderMatchesReference()
    {
        var rigJson = File.ReadAllText(
            FindRepoFile(Path.Combine("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json")));
        var rig = TargetRig.Load(rigJson);
        var skeleton = rig.Skeleton;

        var frames = new List<XForm[]>();
        for (var f = 0; f < 2; f++)
        {
            var frame = new XForm[skeleton.Count];
            for (var i = 0; i < skeleton.Count; i++)
                frame[i] = skeleton[i].RestLocal;
            frames.Add(frame);
        }

        var clip = new Clip("citizen_dummy", 30f, false, frames);
        var output = DmxWriter.Write(skeleton, clip,
            new DmxWriteOptions { Name = "citizen_dummy", SourceNote = "citizen_dummy.fbx" });

        // Header must match the fbx2dmx reference's first line exactly.
        var referenceHeader = File.ReadLines(
            FindRepoFile(Path.Combine("dev", "m0", "ref_idlepose.dmx"))).First();
        Assert.Equal(referenceHeader, output[..output.IndexOf('\r')]);

        foreach (var bone in skeleton.Bones)
        {
            Assert.Contains($"\"name\" \"string\" \"{bone.Name}\"", output);
            Assert.Contains($"\"name\" \"string\" \"{bone.Name}_p\"", output);
            Assert.Contains($"\"name\" \"string\" \"{bone.Name}_o\"", output);
        }

        Assert.Equal(skeleton.Count, Regex.Matches(output, "^\"DmeJoint\"", RegexOptions.Multiline).Count);
        Assert.Equal(skeleton.Count * 2, Regex.Matches(output, "\"DmeChannel\"").Count);
    }
}
