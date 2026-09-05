using System.Numerics;
using HumanoidRetargeter.Formats.Fbx;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class FbxMaterialColorTests
{
    [Theory]
    [InlineData("DiffuseColor", 0.4f)]
    [InlineData("Diffuse", 0.5f)]
    public void PreservesSolidColorsAndAppliesFactorOnlyOnce(string property, float expected)
    {
        var material = new FbxNode("Material");
        var properties = new FbxNode("Properties70");
        material.Children.Add(properties);
        properties.Children.Add(Property(property, 0.5, 0.25, 1.0));
        properties.Children.Add(Property("DiffuseFactor", 0.8));
        var color = FbxMaterialColor.Read(material)!.Value;
        Assert.Equal(expected, color.X, 5);
        Assert.Equal(expected / 2, color.Y, 5);
    }

    [Fact]
    public void MissingColorIsNotInvented()
        => Assert.Null(FbxMaterialColor.Read(new FbxNode("Material")));

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    public void DetectsAuthoredVertexRgbWithoutTreatingAlphaAsColor(double red, bool expected)
    {
        var mesh = new FbxNode("Geometry");
        var layer = new FbxNode("LayerElementColor");
        var colors = new FbxNode("Colors");
        colors.Properties.Add(new[] { red, 1.0, 1.0, 0.0 });
        layer.Children.Add(colors);
        mesh.Children.Add(layer);
        Assert.Equal(expected, FbxMaterialColor.HasVertexColors(mesh));
    }

    static FbxNode Property(string name, params double[] values)
    {
        var node = new FbxNode("P");
        node.Properties.AddRange(new object[] { name, "", "", "" });
        node.Properties.AddRange(values.Cast<object>());
        return node;
    }
}
