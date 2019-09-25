using System.Collections.Generic;
using UnityEditor.Experimental.AssetImporters;
using UnityEditor.ShaderGraph;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Text;

[ScriptedImporter(3, Extension)]
class ShaderSubGraphImporter : ScriptedImporter
{
    [ScriptedImporter(10, Extension)]
    class ShaderSubGraphImporter : ScriptedImporter
    {
        public const string Extension = "shadersubgraph";

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        static string[] GatherDependenciesFromSourceFile(string assetPath)
        {
            try
            {
                return MinimalGraphData.GetDependencyPaths(assetPath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return new string[0];
            }
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var graphAsset = ScriptableObject.CreateInstance<SubGraphAsset>();
            var subGraphPath = ctx.assetPath;
            var subGraphGuid = AssetDatabase.AssetPathToGUID(subGraphPath);
            graphAsset.assetGuid = subGraphGuid;
            var textGraph = File.ReadAllText(subGraphPath, Encoding.UTF8);
            var graphData = new GraphData { isSubGraph = true, assetGuid = subGraphGuid };
            var messageManager = new MessageManager();
            graphData.messageManager = messageManager;
            JsonUtility.FromJsonOverwrite(textGraph, graphData);

            try
            {
                ProcessSubGraph(graphAsset, graphData);
            }
            catch (Exception e)
            {
                graphAsset.isValid = false;
                Debug.LogException(e, graphAsset);
            }
            finally
            {
                if (messageManager.nodeMessagesChanged)
                {
                    graphAsset.isValid = false;
                    foreach (var pair in messageManager.GetNodeMessages())
                    {
                        var node = graphData.GetNodeFromTempId(pair.Key);
                        foreach (var message in pair.Value)
                        {
                            MessageManager.Log(node, subGraphPath, message, graphAsset);
                        }
                    }
                }
                messageManager.ClearAll();
            }

            Texture2D texture = Resources.Load<Texture2D>("Icons/sg_subgraph_icon@64");
            ctx.AddObjectToAsset("MainAsset", graphAsset, texture);
            ctx.SetMainObject(graphAsset);
        }

        static void ProcessSubGraph(SubGraphAsset asset, GraphData graph)
        {
            var registry = new FunctionRegistry(new ShaderStringBuilder(), true);
            registry.names.Clear();
            asset.functions.Clear();
            asset.nodeProperties.Clear();
            asset.isValid = true;

            graph.OnEnable();
            graph.messageManager.ClearAll();
            graph.ValidateGraph();

            var assetPath = AssetDatabase.GUIDToAssetPath(asset.assetGuid);
            asset.hlslName = NodeUtils.GetHLSLSafeName(Path.GetFileNameWithoutExtension(assetPath));
            asset.inputStructName = $"Bindings_{asset.hlslName}_{asset.assetGuid}";
            asset.functionName = $"SG_{asset.hlslName}_{asset.assetGuid}";
            asset.path = graph.path;

            var outputNode = (SubGraphOutputNode)graph.outputNode;

            asset.outputs.Clear();
            outputNode.GetInputSlots(asset.outputs);

            List<AbstractMaterialNode> nodes = new List<AbstractMaterialNode>();
            NodeUtils.DepthFirstCollectNodesFromNode(nodes, outputNode);

            asset.effectiveShaderStage = ShaderStageCapability.All;
            foreach (var slot in asset.outputs)
            {
                var stage = NodeUtils.GetEffectiveShaderStageCapability(slot, true);
                if (stage != ShaderStageCapability.All)
                {
                    asset.effectiveShaderStage = stage;
                    break;
                }
            }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var textGraph = File.ReadAllText(ctx.assetPath, Encoding.UTF8);
        var graph = JsonUtility.FromJson<GraphData>(textGraph);

        if (graph == null)
            return;
        
        graph.isSubGraph = true;

        var sourceAssetDependencyPaths = new List<string>();
        foreach (var node in graph.GetNodes<AbstractMaterialNode>())
            node.GetSourceAssetDependencies(sourceAssetDependencyPaths);

        var graphAsset = ScriptableObject.CreateInstance<MaterialSubGraphAsset>();
        graphAsset.subGraph = graph;

        ctx.AddObjectToAsset("MainAsset", graphAsset);
        ctx.SetMainObject(graphAsset);

        foreach (var sourceAssetDependencyPath in sourceAssetDependencyPaths.Distinct())
            ctx.DependsOnSourceAsset(sourceAssetDependencyPath);
    }
}
