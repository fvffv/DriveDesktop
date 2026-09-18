namespace PortraitRetouchPlugin.Core;

/// <summary>不可变精修参数；强度为 0～100，曝光为 -2～2 EV，其余调色为 -100～100。</summary>
internal sealed record RetouchSettings
{
    /// <summary>磨皮强度，保留眼睛、嘴唇及强边缘。</summary>
    public double Smooth { get; init; }
    /// <summary>面部肤色提亮强度。</summary>
    public double Whiten { get; init; }
    /// <summary>双颊红润强度。</summary>
    public double Blush { get; init; }
    /// <summary>下半脸局部收窄强度。</summary>
    public double Slim { get; init; }
    /// <summary>眼睛局部放大强度。</summary>
    public double Eyes { get; init; }
    /// <summary>眼白局部提亮强度。</summary>
    public double EyeLight { get; init; }
    /// <summary>牙齿低饱和亮部的去黄提亮强度。</summary>
    public double Teeth { get; init; }
    /// <summary>全图曝光补偿，单位 EV。</summary>
    public double Exposure { get; init; }
    /// <summary>全图对比度调整。</summary>
    public double Contrast { get; init; }
    /// <summary>亮部压低或提升。</summary>
    public double Highlights { get; init; }
    /// <summary>暗部压低或提升。</summary>
    public double Shadows { get; init; }
    /// <summary>全图饱和度调整，-100 为黑白。</summary>
    public double Saturation { get; init; }
    /// <summary>全图冷暖偏移。</summary>
    public double Warmth { get; init; }
    /// <summary>细节锐化强度。</summary>
    public double Sharpness { get; init; }
    /// <summary>边缘暗角强度。</summary>
    public double Vignette { get; init; }
    /// <summary>美颜目标人脸编号，-1 表示所有已检测人脸。</summary>
    public int FaceIndex { get; init; } = -1;

    /// <summary>创建可继续手动调整的常用预设。</summary>
    internal static RetouchSettings Preset(string name)
    {
        return name switch
        {
            "自然" => new() { Smooth = 30, Whiten = 10, Blush = 5, EyeLight = 10 },
            "清透" => new() { Smooth = 42, Whiten = 22, EyeLight = 15, Exposure = .12, Shadows = 12, Saturation = -5 },
            "暖肤" => new() { Smooth = 25, Whiten = 8, Blush = 15, Warmth = 12, Highlights = -10 },
            "质感" => new() { Smooth = 12, Contrast = 12, Saturation = -10, Sharpness = 20, Vignette = 12 },
            _ => new()
        };
    }
}

/// <summary>归一化修复笔触；半径相对于图片短边，不随预览缩放改变导出位置。</summary>
internal readonly record struct HealSpot(float X, float Y, float Radius);

/// <summary>一条可撤销的完整编辑记录。</summary>
internal sealed record EditSnapshot(RetouchSettings Settings, HealSpot[] Spots);
