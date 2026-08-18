using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using drive_desktop.Models;

namespace drive_desktop.Services;

public static class EnumerableExtensions
{
    // 自定义一个原地的 ForEach 扩展方法
    public static void Mutate<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
        {
            action(item);
        }
    }
}

public class ConstantResourceService
{
    /// <summary>
    /// 随机字符串
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    public static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// 快速计算hash256
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4*1024 * 1024, // 1MB缓冲
            useAsync: true);

        using var sha256 = SHA256.Create();

        byte[] hash = await sha256.ComputeHashAsync(stream);

        return Convert.ToHexString(hash);
    }
    /// <summary>
    /// 图标hash表
    /// </summary>
    public Dictionary<string, string> CustomViewIconDict { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "fa-filter", "\uf0b0" },
        { "fa-folder", "\uf07b" },
        { "fa-image", "\uf03e" },
        { "fa-film", "\uf008" },
        { "fa-music", "\uf001" },
        { "fa-file-lines", "\uf15c" },
        { "fa-file-zipper", "\uf1c6" },
        { "fa-file-word", "\uf1c2" },
        { "fa-file-excel", "\uf1c3" },
        { "fa-file-pdf", "\uf1c1" },
        { "fa-code", "\uf1c9" },
        { "fa-database", "\uf1c0" },
        { "fa-star", "\uf005" },
        { "fa-heart", "\uf004" },
        { "fa-tag", "\uf02b" },
        { "fa-bookmark", "\uf02e" },
        { "fa-plane", "\uf072" },
        { "fa-gamepad", "\uf11b" },
        { "fa-book", "\uf02d" },
    };

    public Dictionary<string, string> CustomViewIconDictReversed { get; } = new()
    {
        { "\uf0b0", "fa-filter" },
        { "\uf07b", "fa-folder" },
        { "\uf03e", "fa-image" },
        { "\uf008", "fa-film" },
        { "\uf001", "fa-music" },
        { "\uf15c", "fa-file-lines" },
        { "\uf1c6", "fa-file-zipper" },
        { "\uf1c2", "fa-file-word" },
        { "\uf1c3", "fa-file-excel" },
        { "\uf1c1", "fa-file-pdf" },
        { "\uf1c9", "fa-code" },
        { "\uf1c0", "fa-database" },
        { "\uf005", "fa-star" },
        { "\uf004", "fa-heart" },
        { "\uf02b", "fa-tag" },
        { "\uf02e", "fa-bookmark" },
        { "\uf072", "fa-plane" },
        { "\uf11b", "fa-gamepad" },
        { "\uf02d", "fa-book" }
    };

    public string GetSidebarClassName(string icon)
    {
        return CustomViewIconDictReversed.GetValueOrDefault(icon, "fa-filter");
    }

    /// <summary>
    /// 网页类名转换成字体u码
    /// </summary>
    /// <param name="icon"></param>
    /// <returns></returns>
    public string GetSidebarIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return "\uf07b"; // 返回默认图标
        }

        ReadOnlySpan<char> span = icon.AsSpan();

        int index = span.IndexOf("fa-solid");
        if (index != -1)
        {
            span = span.Slice(index + 8);
        }

        span = span.Trim();
        return CustomViewIconDict.GetValueOrDefault(span.ToString(), "\uf07b");
    }

    //全局共享的Options
    private static readonly JsonSerializerOptions AotOptions = new JsonSerializerOptions
    {
        TypeInfoResolver = AppConfigJsonContext.Default,
    };

    /// <summary>
    /// 对象转 JSON 字符串 (AOT 安全)
    /// </summary>
    public static string ToJson<T>(T data)
    {
        return JsonSerializer.Serialize(data, AotOptions);
    }

    /// <summary>
    /// JSON 字符串转对象 (AOT 安全)
    /// </summary>
    public static T? FromJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, AotOptions);
    }


    /// <summary>
    /// 添加默认视图 返回成功数
    /// </summary>
    /// <param name="cvs"></param>
    public int AddDefaultCustomView(ObservableCollection<CustomView> cvs)
    {
        int i = 0;

        void TryAdd(string name, string[] keywords, string iconName)
        {
            bool isExist = cvs.Any(x => x.Keywords != null &&
                                        x.Keywords.OrderBy(k => k).SequenceEqual(keywords.OrderBy(k => k)));

            if (!isExist)
            {
                cvs.Add(new CustomView()
                {
                    Name = name,
                    Keywords = keywords,
                    Icon = GetSidebarIcon(iconName)
                });
                i++;
            }
        }

        // 依次尝试添加，如果 Keywords 已经存在于列表中，就会被自动跳过
        TryAdd("所有文档", [".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".md"], "fa-file-lines");
        TryAdd("所有图片", [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff"], "fa-image");
        TryAdd("所有视频", [".mp4", ".avi", ".mkv", ".mov", ".wmv"], "fa-film");
        TryAdd("所有音频", [".mp3", ".wav", ".flac", ".aac"], "fa-music");
        TryAdd("压缩文件", [".zip", ".rar", ".7z", ".tar", ".gz"], "fa-file-zipper");
        TryAdd("前端代码", [".js", ".ts", ".vue", ".html", ".css", ".json"], "fa-code");
        TryAdd("后端代码", [".cs", ".py", ".java", ".cpp", ".go", ".sql"], "fa-database");
        return i;
    }

    public static class FileIconHelper
    {
        
        public static string GetSideIconColor(string name) => name switch
        {
            "fa-filter" => "#607D8B",      // 蓝灰色
            "fa-folder" => "#FFCA28",      // 金黄色
            "fa-image" => "#00BCD4",       // 青蓝色
            "fa-film" => "#9C27B0",        // 紫色
            "fa-music" => "#E91E63",       // 粉红色
            "fa-file-lines" => "#9E9E9E",  // 灰色
            "fa-file-zipper" => "#FF9800", // 橙色
            "fa-file-word" => "#2B579A",   // 深蓝色 (Word)
            "fa-file-excel" => "#217346",  // 深绿色 (Excel)
            "fa-file-pdf" => "#F40F02",    // 亮红色 (PDF)
            "fa-code" => "#3F51B5",        // 靛蓝色
            "fa-database" => "#009688",    // 蓝绿色
            "fa-star" => "#FFC107",        // 琥珀黄
            "fa-heart" => "#F44336",       // 红色
            "fa-tag" => "#8BC34A",         // 浅绿色
            "fa-bookmark" => "#03A9F4",    // 浅蓝色
            "fa-plane" => "#2196F3",       // 纯蓝色
            "fa-gamepad" => "#673AB7",     // 深紫色
            "fa-book" => "#795548",        // 棕色
            _ => "#808080"                 // 默认颜色 (Fallback)
        };
        public static readonly FontFamily FASolid =
            new("avares://drive-desktop/Assets/ViewAssets/Font Awesome 6 Free-Solid-900.otf#Font Awesome 6 Free");

        public static readonly FontFamily FABrands =
            new("avares://drive-desktop/Assets/ViewAssets/Font Awesome 6 Brands-Regular-400.otf#Font Awesome 6 Brands");

        public static readonly FontFamily FARegular =
            new("avares://drive-desktop/Assets/ViewAssets/Font Awesome 6 Free-Regular-400.otf#Font Awesome 6 Free");



        // 默认文件图标 (fa-file) 和 颜色
        private static readonly FileTypeInfo DefaultFileInfo = new("\uf15b", FASolid, "#8D9095");

        // 使用忽略大小写的字典，避免 .JPG 和 .jpg 匹配失败
        private static readonly Dictionary<string, FileTypeInfo> FileMap = new(StringComparer.OrdinalIgnoreCase)
        {
            //图片
            { "jpg", new("\uf1c5", FASolid, "#8e44ad", true, "图片") },
            { "jpeg", new("\uf1c5", FASolid, "#8e44ad", true, "图片") },
            { "png", new("\uf1c5", FASolid, "#8e44ad", true, "图片") },
            { "gif", new("\uf1c5", FASolid, "#8e44ad", true, "图片") },
            { "bmp", new("\uf1c5", FASolid, "#8e44ad", true, "图片") },
            { "webp", new("\uf1c5", FASolid, "#8e44ad", true, "图片") },
            { "ico", new("\uf1c5", FASolid, "#8e44ad", true, "图片") },
            { "svg", new("\uf1c5", FASolid, "#e67e22", true, "图片") },

            // === 文档 ===
            { "pdf", new("\uf1c1", FASolid, "#e33e33", false, "文档2007") },
            { "doc", new("\uf1c2", FASolid, "#2b579a", false, "文档") },
            { "docx", new("\uf1c2", FASolid, "#2b579a", false, "文档2007") },
            { "xls", new("\uf1c3", FASolid, "#217346", false, "文档") },
            { "xlsx", new("\uf1c3", FASolid, "#217346", false, "文档2007") },
            { "csv", new("\uf6dd", FASolid, "#217346", false, "文档") },
            { "ppt", new("\uf1c4", FASolid, "#d24726", false, "文档") },
            { "pptx", new("\uf1c4", FASolid, "#d24726", false, "文档2007") },
            { "md", new("\uf60f", FABrands, "#606266", false, "文档") },

            // === 代码 ===
            { "txt", new("\uf15c", FASolid, "#606266", false, "代码") },
            { "xml", new("\uf1c9", FASolid, "#cf8f2f", false, "代码") },
            { "js", new("\uf3b8", FABrands, "#f1e05a", false, "代码") },
            { "ts", new("\uf3b8", FABrands, "#3178c6", false, "代码") },
            { "vue", new("\uf41f", FABrands, "#41b883", false, "代码") },
            { "html", new("\uf13b", FABrands, "#e34c26", false, "代码") },
            { "css", new("\uf38b", FABrands, "#563d7c", false, "代码") },
            { "json", new("\uf1c9", FASolid, "#cf8f2f", false, "代码") },
            { "java", new("\uf4e4", FABrands, "#b07219", false, "代码") },
            { "jar", new("\uf4e4", FABrands, "#f89820", false, "代码") },
            { "py", new("\uf3e2", FABrands, "#3572a5", false, "代码") },
            { "go", new("\ue40f", FABrands, "#00add8", false, "代码") },
            { "c", new("\uf1c9", FASolid, "#3178c6", false, "代码") },
            { "cpp", new("\uf1c9", FASolid, "#3178c6", false, "代码") },
            { "sql", new("\uf1c0", FASolid, "#f0ad4e", false, "代码") },
            { "cs", new("\uf1c9", FASolid, "#68217A", false, "代码") },

            // === 压缩包 ===
            { "zip", new("\uf1c6", FASolid, "#f1c40f", false, "压缩包") },
            { "rar", new("\uf1c6", FASolid, "#f1c40f", false, "压缩包") },
            { "7z", new("\uf1c6", FASolid, "#f1c40f", false, "压缩包") },
            { "tar", new("\uf1c6", FASolid, "#f1c40f", false, "压缩包") },
            { "gz", new("\uf1c6", FASolid, "#f1c40f", false, "压缩包") },

            // === 媒体 ===
            { "mp3", new("\uf1c7", FASolid, "#ff9f43", false, "音频") },
            { "wav", new("\uf1c7", FASolid, "#ff9f43", false, "音频") },
            { "ogg", new("\uf1c7", FASolid, "#ff9f43", false, "音频") },
            { "mp4", new("\uf1c8", FASolid, "#3498db", false, "视频") },
            { "avi", new("\uf1c8", FASolid, "#3498db", false, "视频") },
            { "mkv", new("\uf1c8", FASolid, "#3498db", false, "视频") },
            { "mov", new("\uf1c8", FASolid, "#3498db", false, "视频") },

            // === 其他 ===
            { "exe", new("\uf17a", FABrands, "#00a8ff", false, "应用") },
            { "apk", new("\uf17b", FABrands, "#a4c639", false, "应用") },
            { "iso", new("\uf51f", FASolid, "#95a5a6", false, "系统") },
            { "nbt", new("\uf1b2", FASolid, "#4caf50", false, "系统") }
        };


        /// <summary>
        /// 获取完整的图标信息对象
        /// </summary>
        public static FileTypeInfo GetFileInfo(string fileNameOrExtension)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrExtension)) return DefaultFileInfo;

            // 自动提取后缀逻辑：
            // 找到最后一个 '.' 的位置。如果没有点，就把整个字符串当成后缀。
            int lastDotIndex = fileNameOrExtension.LastIndexOf('.');
            string ext = lastDotIndex >= 0
                ? fileNameOrExtension.Substring(lastDotIndex + 1)
                : fileNameOrExtension;

            // 去字典里尝试匹配
            return FileMap.TryGetValue(ext, out var info) ? info : DefaultFileInfo;
        }

        /// <summary>
        /// 单独获取 U码
        /// </summary>
        public static string GetIconCode(string extension) => GetFileInfo(extension).IconCode;

        /// <summary>
        /// 单独获取 Hex 颜色
        /// </summary>
        public static string GetColor(string extension) => GetFileInfo(extension).HexColor;

        /// <summary>
        /// 单独获取 FontFamily
        /// </summary>
        public static FontFamily GetFontFamily(string extension) => GetFileInfo(extension).FontFamilyResource;

        /// <summary>
        /// 单独获取文件分类（如 "图片"、"文档"、"视频" 等）
        /// </summary>
        public static string GetFileTypeName(string extension) => GetFileInfo(extension).TypeName;
    }
    
    /// <summary>
    /// 自动检测字符数组编码并安全转换为 String (完美兼容 BOM、无BOM UTF-8、GBK/ANSI)
    /// </summary>
    public static string DetectEncodingAndDecodeText(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) 
            return string.Empty;
        
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 识别常见文件的 BOM 头部
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 LE
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 BE

        // 没有 BOM 时，尝试“严格模式”的 UTF-8 解码
        // 第二个参数 throwOnInvalidBytes 设置为 true：一旦发现非法的 UTF-8 字节序列，立刻抛出异常
        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            //如果在严格 UTF-8 校验下崩溃了，说明大概率是中文 Windows 默认的 ANSI (GBK / GB2312)
            try
            {
                var gbkEncoding = Encoding.GetEncoding("GBK");
                return gbkEncoding.GetString(bytes);
            }
            catch
            {
                //直接按操作系统默认字符集读取
                return Encoding.Default.GetString(bytes);
            }
        }
    }
}