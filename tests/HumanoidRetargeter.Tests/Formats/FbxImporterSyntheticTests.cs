using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

/// <summary>
/// FbxImporter behavior tests on small hand-written ASCII FBX scenes (the tokenizer's ASCII
/// fallback makes synthetic fixtures cheap to express inline).
/// </summary>
public class FbxImporterSyntheticTests
{
    private static SourceScene Import(string ascii)
        => FbxImporter.Import(Encoding.UTF8.GetBytes(ascii));

    // ---- finding 7: kept bones beyond a non-kept Mesh node -------------------------------

    // No LimbNodes and no animation: the last-resort fallback keeps every non-Mesh model.
    // "attachment" hangs UNDER the Mesh node, so the kept set is non-contiguous — the
    // traversal must recurse through the Mesh and re-parent attachment to torso.
    private const string MeshSandwichScene = """
        Objects: {
            Model: 1, "Model::torso", "Null" {
                Properties70: {
                    P: "Lcl Translation", "Lcl Translation", "", "A",0.0,10.0,0.0
                }
            }
            Model: 2, "Model::cloth", "Mesh" {
            }
            Model: 3, "Model::attachment", "Null" {
                Properties70: {
                    P: "Lcl Translation", "Lcl Translation", "", "A",0.0,5.0,0.0
                }
            }
        }
        Connections: {
            C: "OO",2,1
            C: "OO",3,2
        }
        """;

    [Fact]
    public void Fallback_KeptBoneBeyondMeshNode_IsImportedAndReparented()
    {
        var scene = Import(MeshSandwichScene);
        var skel = scene.Skeleton;

        Assert.Equal(2, skel.Count); // torso + attachment; the Mesh stays excluded
        int torso = skel.IndexOf("torso");
        int attachment = skel.IndexOf("attachment");
        Assert.True(torso >= 0, "missing torso");
        Assert.True(attachment >= 0, "missing attachment (dropped behind the Mesh node)");

        // Re-parented to the nearest kept ancestor, local offset preserved.
        Assert.Equal(torso, skel[attachment].ParentIndex);
        Assert.Equal(new Vector3(0f, 5f, 0f), skel[attachment].RestLocal.Pos);
    }

    // ---- findings 3 + 4: static translation override and multi-stack disagreement --------

    // Two animation stacks drive bone's Lcl Translation X with DIFFERENT static curves
    // (5 in take1, 9 in take2). The first stack must win the rest override, and the
    // disagreement must surface as a SourceScene note naming the bone.
    private const string TwoStackScene = """
        Objects: {
            Model: 1, "Model::torso", "Null" {
            }
            Model: 2, "Model::bone", "LimbNode" {
                Properties70: {
                    P: "Lcl Translation", "Lcl Translation", "", "A",1.0,2.0,3.0
                }
            }
            AnimationStack: 10, "AnimStack::take1", "" {
            }
            AnimationLayer: 11, "AnimLayer::layer1", "" {
            }
            AnimationCurveNode: 12, "AnimCurveNode::T", "" {
            }
            AnimationCurve: 13, "AnimCurve::c1", "" {
                KeyTime: *2 {
                    a: 0,46186158000
                }
                KeyValueFloat: *2 {
                    a: 5.0,5.0
                }
            }
            AnimationStack: 20, "AnimStack::take2", "" {
            }
            AnimationLayer: 21, "AnimLayer::layer2", "" {
            }
            AnimationCurveNode: 22, "AnimCurveNode::T", "" {
            }
            AnimationCurve: 23, "AnimCurve::c2", "" {
                KeyTime: *2 {
                    a: 0,46186158000
                }
                KeyValueFloat: *2 {
                    a: 9.0,9.0
                }
            }
        }
        Connections: {
            C: "OO",2,1
            C: "OO",11,10
            C: "OO",21,20
            C: "OO",12,11
            C: "OO",22,21
            C: "OP",12,2, "Lcl Translation"
            C: "OP",22,2, "Lcl Translation"
            C: "OP",13,12, "d|X"
            C: "OP",23,22, "d|X"
        }
        """;

    [Fact]
    public void TwoStacks_DisagreeingStaticTranslation_FirstStackWinsAndNoteIsAppended()
    {
        var scene = Import(TwoStackScene);
        var skel = scene.Skeleton;

        int bone = skel.IndexOf("bone");
        Assert.True(bone >= 0, "missing bone");

        // First stack's static X (5) overrides the Lcl default (1); Y/Z keep their values.
        var rest = skel[bone].RestLocal.Pos;
        Assert.Equal(5f, rest.X, 3);
        Assert.Equal(2f, rest.Y, 3);
        Assert.Equal(3f, rest.Z, 3);

        // Finding 3 invariant: the override equals the frame-0 sampled local translation of
        // the clip the static channels belong to, regardless of any ancestor scale.
        Assert.Equal(2, scene.Clips.Count);
        var frame0 = scene.Clips[0].Frames[0];
        Assert.Equal(0f, Vector3.Distance(rest, frame0[bone].Pos), 3);

        // Finding 4: the cross-stack disagreement (5 vs 9) is surfaced as a note.
        var note = Assert.Single(scene.Notes);
        Assert.Contains("bone", note);
        Assert.Contains("stack", note, StringComparison.OrdinalIgnoreCase);
    }

    // Same scene but with AGREEING statics in both stacks: no note.
    [Fact]
    public void TwoStacks_AgreeingStaticTranslation_NoNote()
    {
        var scene = Import(TwoStackScene.Replace("a: 9.0,9.0", "a: 5.0,5.0"));
        Assert.Empty(scene.Notes);
        Assert.Equal(5f, scene.Skeleton[scene.Skeleton.IndexOf("bone")].RestLocal.Pos.X, 3);
    }
}
