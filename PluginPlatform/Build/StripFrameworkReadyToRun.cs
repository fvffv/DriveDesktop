using System;
using System.IO;
using Microsoft.Build.Framework;
using Mono.Cecil;

/// <summary>将发布中间目录的 .NET 框架副本重写为完整 IL；不删除类型、成员或资源。</summary>
public sealed class StripFrameworkReadyToRun : Microsoft.Build.Utilities.Task
{
    /// <summary>当前 RID 的原始 .NET 运行时程序集；只用于确定待处理的文件名。</summary>
    [Required]
    public ITaskItem[] Assemblies { get; set; }

    /// <summary>本次 ILLink 输出目录，绝不修改 NuGet 缓存或原始运行时。</summary>
    [Required]
    public string Directory { get; set; }

    /// <summary>去除框架 DLL 中的 ReadyToRun 机器码，保留可供 JIT 执行的完整 IL 和元数据。</summary>
    /// <returns>全部处理成功返回 true，失败时记录构建错误并返回 false。</returns>
    public override bool Execute()
    {
        int rewritten = 0;
        long saved = 0;
        try
        {
            foreach (var item in Assemblies)
            {
                var path = Path.Combine(Directory, Path.GetFileName(item.ItemSpec));
                var temporary = path + ".pure-il.tmp";
                try
                {
                    using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false }))
                    {
                        var module = assembly.MainModule;
                        // 普通 IL/facade 不需要重写；这也保证增量发布不会反复改写文件。
                        if ((module.Attributes & ModuleAttributes.ILLibrary) == 0)
                        {
                            continue;
                        }
                        // 与 ILLink 的 OutputStep 写回 crossgen 程序集的方式一致。
                        module.Attributes |= ModuleAttributes.ILOnly;
                        module.Attributes &= ~ModuleAttributes.ILLibrary;
                        module.Architecture = TargetArchitecture.I386;
                        module.Characteristics |= ModuleCharacteristics.NoSEH;
                        assembly.Write(temporary, new WriterParameters { DeterministicMvid = true, WriteSymbols = false });
                    }
                    saved += new FileInfo(path).Length - new FileInfo(temporary).Length;
                    File.Copy(temporary, path, true);
                    rewritten++;
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
            Log.LogMessage(MessageImportance.High, "Runner framework IL: {0} assemblies rewritten, {1:N0} bytes of precompiled overhead removed.", rewritten, saved);
            return true;
        }
        catch (Exception error)
        {
            Log.LogErrorFromException(error, true);
            return false;
        }
    }
}
