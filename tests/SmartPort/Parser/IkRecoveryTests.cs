using HumanoidRetargeter.Target;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.IO;
using HumanoidRetargeterVrf.ResourceTypes;
using HumanoidRetargeterVrf.Serialization.KeyValues;
using Xunit;

namespace SmartPort.Parser.Tests;

public class IkRecoveryTests
{
    sealed class NoMaterials : IFileLoader
    {
        public Resource? LoadFile(string file) => null;
        public Resource? LoadFileCompiled(string file) => null;
    }

    [CompiledFixtureFact]
    public void RecoveryKeepsCompleteIkRuntimeDataForGraphHandLocks()
    {
        using var resource = new Resource();
        resource.Read(Path.Combine(Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!, "models/player/human/frank_mp.vmdl_c"));
        var model = Assert.IsType<Model>(resource.DataBlock);
        var expected = Kv3.Parse(new KV3File(model.KeyValues.GetSubCollection("ikdata")).ToString()).Root;
        var recovered = new ModelExtract(resource, new NoMaterials()).ToValveModel();
        var doc = (KvObject)Kv3.Parse(recovered).Root;
        var nodes = (KvArray)((KvObject)doc["rootNode"])["children"];
        var gameData = Assert.Single(nodes.Items.OfType<KvObject>().Where(n => n.GetString("_class") == "GameDataList"));
        var ik = Assert.Single(((KvArray)gameData["children"]).Items.OfType<KvObject>().Where(n => n.GetString("game_class") == "ikdata"));
        Assert.True(KvValue.DeepEquals(expected, ik["game_keys"]));
        Assert.Contains("left_arm_IK", recovered);
        Assert.Contains("right_arm_IK", recovered);
    }
}
