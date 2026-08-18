using System;
using CommunityToolkit.Mvvm.ComponentModel;
using drive_desktop.Services;
using FluentValidation;
using FluentValidation.Results;

namespace drive_desktop.Models;

public class UserPreferences
{
    // 是否启用深色模式
    public bool DarkMode { get; set; } = false;

    // 是否启用文件直链功能
    public bool IsDirectLinkEnabled { get; set; } = false;

    // 是否启用WebDAV功能
    public bool IsWebDAVEnabled { get; set; } = false;
    public CustomViewDto[] CustomView { get; set; }
}

public partial class CustomView : ObservableValidator
{
    ///视图的名称，例如'我的视频'或'工作报表'"
    [ObservableProperty] private string _name;

    //"视图类型：0 代表基于关键字筛选(如后缀名)，1 代表文件夹绝对路径快捷跳转(如'/工作')"
    [ObservableProperty] private int _type;

    ///FontAwesome图标字符串，例如 'fa-solid fa-folder', 'fa-solid fa-film' 等。"
    [ObservableProperty] private string _icon;
    
    //颜色
    [ObservableProperty] private string _color;

    ///"视图关键词或文件夹路径的数组。如果 Type=0，传入关键字数组(如 ['.mp4', '.avi'])；如果 Type=1，传入包含绝对路径的单元素数组(如 ['/我的文件/报表'])。")
    [ObservableProperty] private string[] _Keywords;
    
    private CustomViewValidator val = new ();
    /// <summary>
    /// 获取表单验证对象
    /// </summary>
    /// <returns></returns>
    public ValidationResult GetValidationResult()
    {
        return val.Validate(this);
    }
}
/// <summary>
/// logininfo的表单验证器 为了aot支持
/// </summary>
public class CustomViewValidator : AbstractValidator<CustomView>
{
    public CustomViewValidator()
    {
     
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("视图名不能为空")
            .MaximumLength(20).WithMessage("视图名长度不能大于20");
       
        RuleFor(x => x.Keywords)
            .Must(arr => arr is { Length: >= 1 and <= 20 })
            .WithMessage("关键字不能少于1个且不能大于20个");

       
    }
}
public class CustomViewDto
{
    public string Name { get; set; }
    public int Type { get; set; }
    public string Icon { get; set; }
    public string[] Keywords { get; set; }
}

public partial class CustomViewIcon: ObservableValidator
{
    /// <summary>
    /// 对应html类名
    /// </summary>
    [NotifyPropertyChangedFor(nameof(Color))]
    [ObservableProperty] private string _name;
    /// <summary>
    /// 对应u编码
    /// </summary>  
    [ObservableProperty] private string _icon;
    /// <summary>
    /// 颜色
    /// </summary>
    public string Color => ConstantResourceService.FileIconHelper.GetSideIconColor(Name);
    /// <summary>
    /// 是否选中
    /// </summary>
    [ObservableProperty] private bool _isChecked ;
}

public class ShowUserInfo 
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } 
    public string Nickname { get; set; } 
    public string AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RootFolderId { get; set; }
    public string Email { get; set; }
    public UserPreferences Preferences { get; set; }
    public int Status { get; set; }
    
  
}