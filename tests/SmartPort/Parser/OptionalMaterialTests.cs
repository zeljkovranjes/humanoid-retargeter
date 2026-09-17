using System.Reflection;
using System.Text;
using HumanoidRetargeter.Editor;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.IO;
using Xunit;

namespace SmartPort.Parser.Tests;

public class OptionalMaterialTests
{
    const string Outfit = "models/human_clothes/default_outfit/default_outfit_male_lower.vmat";

    sealed class FailingLoader(Exception error) : IFileLoader
    {
        public Resource LoadFile(string file) => throw error;
        public Resource LoadFileCompiled(string file) => LoadFile(file);
    }

    [Fact]
    public void MissingOutfitMetadataUsesVertexBufferSemantics()
    {
        var loader = new FailingLoader(new FileNotFoundException("Missing material", Outfit));
        Assert.Empty(ModelExtract.ReadMaterialInputSignature(loader, Outfit).Elements);
    }

    [Fact]
    public void CorruptMetadataAndCancellationAreNotHidden()
    {
        Assert.Throws<InvalidDataException>(() => ModelExtract.ReadMaterialInputSignature(
            new FailingLoader(new InvalidDataException("Corrupt material")), Outfit));
        Assert.Throws<OperationCanceledException>(() => ModelExtract.ReadMaterialInputSignature(
            new FailingLoader(new OperationCanceledException()), Outfit));
    }

    [Theory]
    [InlineData("required.vmesh")]
    [InlineData("required.vphys")]
    [InlineData("required.vanim")]
    [InlineData("required.vagrp")]
    [InlineData("required.vmdl")]
    public void RequiredDependenciesStillFailWithTheirFilename(string path)
    {
        var type = typeof(CompiledAssetRecovery).GetNestedType("Loader", BindingFlags.NonPublic)!;
        using var loader = (IDisposable)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { new Dictionary<string, string>(), CancellationToken.None }, null)!;
        var error = Assert.Throws<FileNotFoundException>(() => ((IFileLoader)loader).LoadFileCompiled(path));
        Assert.Equal(path, error.FileName);
        Assert.Contains(path, error.Message);
    }

    [CompiledFixtureFact]
    public void MissingMaterialFilesDoNotBlockRecoveryOrEraseMaterialAssignments()
    {
        var fixture = Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!;
        var paths = Directory.GetFiles(fixture, "*_c", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".vmat_c", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => Path.GetRelativePath(fixture, p)[..^2].Replace('\\', '/'), p => p, StringComparer.OrdinalIgnoreCase);
        const string model = "models/player/human/frank_mp.vmdl";
        var output = Path.Combine(Path.GetTempPath(), "hr-smart-port-no-materials-" + Guid.NewGuid().ToString("N"));
        var text = CompiledAssetRecovery.Recover(paths[model], model, output, "recovered", paths, default);
        using var resource = new Resource();
        resource.Read(paths[model]);
        Assert.NotNull(resource.ExternalReferences);
        var materials = resource.ExternalReferences.ResourceRefInfoList.Where(r => r.Name.EndsWith(".vmat")).Select(r => r.Name).ToArray();
        Assert.NotEmpty(materials);
        var files = Directory.GetFiles(output, "*.dmx", SearchOption.AllDirectories);
        Assert.Equal(398, files.Length); // Seven meshes and all 391 animation sources.
        var meshText = string.Join("\n", files.Where(f => Path.GetFileName(f).StartsWith("frank_mp_")).Select(f => Encoding.UTF8.GetString(File.ReadAllBytes(f))));
        foreach (var material in materials) Assert.Contains(material, text + meshText);
    }
}
