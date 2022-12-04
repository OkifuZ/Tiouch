#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;


namespace Taichi {
    [ScriptedImporter(1, "tcm")] // 加载自定义格式的资源
    // asset pipeline 检测到该格式文件，就调用自定义的OnImortAsset方法
    public class AotModuleImporter : ScriptedImporter {
        public override void OnImportAsset(AssetImportContext ctx) {
            var guid = Guid.NewGuid().ToString();
            var tcm = File.ReadAllBytes(ctx.assetPath);
            AotModuleAsset asset = ScriptableObject.CreateInstance<AotModuleAsset>();
            asset.name = "tcm:" + ctx.assetPath;
            asset.Guid = guid;
            asset.ArchiveData = tcm;

            ctx.AddObjectToAsset("archive", asset);
            ctx.SetMainObject(asset);
        }
    }
}

#endif // UNITY_EDITOR
