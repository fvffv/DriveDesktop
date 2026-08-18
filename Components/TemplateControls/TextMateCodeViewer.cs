using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using drive_desktop.Views;
using TextMateSharp.Grammars;
using Ursa.Controls;

namespace drive_desktop.Components.TemplateControls;

/// <summary>
/// 封装 AvaloniaEdit + TextMate 的高级代码预览/编辑组件
/// 支持：自动根据文件名识别语法、自动跟随全局深浅色主题切换
/// </summary>
public class TextMateCodeViewer : TextEditor
{
    protected override Type StyleKeyOverride => typeof(TextEditor);
    // 🌟 1. 注册文件名/文件路径依赖属性（如 "config.json" 或 "Program.cs"）
    public static readonly StyledProperty<string?> FileNameProperty =
        AvaloniaProperty.Register<TextMateCodeViewer, string?>(nameof(FileName));

    public string? FileName
    {
        get => GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    private RegistryOptions? _registryOptions;
    private TextMate.Installation? _textMateInstallation;

    public TextMateCodeViewer()
    {
        // 默认配置为只读预览模式（如果你需要编辑，在 XAML 里设为 IsReadOnly="False" 即可）
        IsReadOnly = true;
        ShowLineNumbers = true;

        // 内置优质等宽字体与中文字体回退链
        FontFamily = new Avalonia.Media.FontFamily("Consolas, Cascadia Code, JetBrains Mono, Microsoft YaHei UI, monospace");
        FontSize = 14;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // 🌟 初始化 TextMate 引擎（根据当前默认主题判断初始配色）
        var initialTheme = IsDarkTheme() ? ThemeName.DarkPlus : ThemeName.LightPlus;
        _registryOptions = new RegistryOptions(initialTheme);
        _textMateInstallation = this.InstallTextMate(_registryOptions, true, ex => 
        {
           
        });
        // 如果初始就已经给定了文件名，立即解析语法
        UpdateGrammar();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 🌟 监听文件名属性变化：一旦修改绑定文件名，立即切换语法高亮
        if (change.Property == FileNameProperty)
        {
            UpdateGrammar();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 🌟 控件挂载到窗口时，订阅全局主题切换事件
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // 🌟 核心规范：控件销毁移除时，主动取消订阅，彻底防止内存泄漏！
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        }
    }

    /// <summary>
    /// 当系统或用户切换全局深/浅色主题时触发
    /// </summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_registryOptions == null || _textMateInstallation == null) return;

        var targetTheme = IsDarkTheme() ? ThemeName.DarkPlus : ThemeName.LightPlus;
        _textMateInstallation.SetTheme(_registryOptions.LoadTheme(targetTheme));
    }

    /// <summary>
    /// 根据传入的文件名解析后缀并更新高亮规则
    /// </summary>
    private void UpdateGrammar()
    {
        if (_registryOptions == null || _textMateInstallation == null || string.IsNullOrWhiteSpace(FileName))
            return;

        // 提取后缀名 (比如从 "D:\files\config.json" 提取出 ".json")
        string extension = Path.GetExtension(FileName);

        // 如果没有后缀名（比如传入的直接是语言标识 "csharp" 或 "sql"）
        if (string.IsNullOrEmpty(extension))
        {
            extension = FileName.StartsWith(".") ? FileName : $".{FileName}";
        }

        // 去 TextMate 注册表里查找对应后缀的语言范围
        var language = _registryOptions.GetLanguageByExtension(extension);

        if (language != null)
        {
            _textMateInstallation.SetGrammar(_registryOptions.GetScopeByLanguageId(language.Id));
        }
    }

    private static bool IsDarkTheme()
    {
        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    }
}