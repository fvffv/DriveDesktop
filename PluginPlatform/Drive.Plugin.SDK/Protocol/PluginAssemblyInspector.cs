using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Drive.Plugin.SDK.Protocol;

/// <summary>
/// 只读取 PE/程序集元数据识别插件入口，不加载程序集，不执行静态构造函数或模块初始化器。
/// </summary>
internal static class PluginAssemblyInspector
{
    /// <summary>
    /// 找到直接声明 SDK DrivePluginAttribute 的类型；普通依赖和原生库返回空数组。
    /// </summary>
    /// <param name="path">待检查的 DLL 文件。</param>
    /// <returns>带插件标记的类型完整名称；多于一个表示插件打包有歧义。</returns>
    internal static string[] FindEntryTypes(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var pe = new PEReader(file);
            if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null) return [];
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return [];
            var entries = new List<string>();
            foreach (var handle in metadata.TypeDefinitions)
            {
                var definition = metadata.GetTypeDefinition(handle);
                foreach (var attributeHandle in definition.GetCustomAttributes())
                {
                    var attribute = metadata.GetCustomAttribute(attributeHandle);
                    if (IsPluginAttribute(metadata, attribute.Constructor))
                    {
                        entries.Add(GetTypeName(metadata, handle));
                        break;
                    }
                }
            }
            return entries.ToArray();
        }
        catch (BadImageFormatException)
        {
            // 原生 DLL、损坏文件和非程序集文件都不能作为普通托管插件入口。
            return [];
        }
    }

    /// <summary>
    /// 验证特性构造函数确实引用 Drive.Plugin.SDK，而不是仅有同名字符串。
    /// </summary>
    /// <param name="metadata">当前程序集的元数据读取器。</param>
    /// <param name="constructor">自定义特性构造函数句柄。</param>
    /// <returns>是否为 SDK 的 DrivePluginAttribute 构造函数。</returns>
    private static bool IsPluginAttribute(MetadataReader metadata, EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference) return false;
        var member = metadata.GetMemberReference((MemberReferenceHandle)constructor);
        if (!metadata.StringComparer.Equals(member.Name, ".ctor") || member.Parent.Kind != HandleKind.TypeReference) return false;
        var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        if (!metadata.StringComparer.Equals(type.Namespace, "Drive.Plugin.SDK") ||
            !metadata.StringComparer.Equals(type.Name, "DrivePluginAttribute") ||
            type.ResolutionScope.Kind != HandleKind.AssemblyReference) return false;
        var assembly = metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
        return metadata.StringComparer.Equals(assembly.Name, "Drive.Plugin.SDK");
    }

    /// <summary>
    /// 生成反射可识别的类型名称，嵌套入口后续由运行器按 SDK 规则拒绝。
    /// </summary>
    /// <param name="metadata">当前程序集元数据。</param>
    /// <param name="handle">类型定义句柄。</param>
    /// <returns>包含命名空间或外层类型的名称。</returns>
    private static string GetTypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var definition = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(definition.Name);
        var parent = definition.GetDeclaringType();
        if (!parent.IsNil) return GetTypeName(metadata, parent) + "+" + name;
        var typeNamespace = metadata.GetString(definition.Namespace);
        return typeNamespace.Length == 0 ? name : typeNamespace + "." + name;
    }
}
