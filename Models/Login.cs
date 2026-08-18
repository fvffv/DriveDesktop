
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentValidation;
using FluentValidation.Results;

namespace drive_desktop.Models;

public class Login
{
    
}
//一个用于webapi 一个用于vm
public record class LoginInfo(string usernameOrEmail, string password);
public partial class LoginInfoModel:ObservableValidator
{
    [ObservableProperty]
    private string _usernameOrEmail;
    [ObservableProperty]
    private string _password;
 
    private LoginInfoValidator val = new ();
/// <summary>
/// 获取表单验证对象
/// </summary>
/// <returns></returns>
    public ValidationResult GetValidationResult()
    {
        return val.Validate(this);
    }
    
    public static implicit operator LoginInfo(LoginInfoModel model)
    {
        return new LoginInfo(model.UsernameOrEmail, model.Password);
    }

}
/// <summary>
/// logininfo的表单验证器 为了aot支持
/// </summary>
public class LoginInfoValidator : AbstractValidator<LoginInfoModel>
{
    public LoginInfoValidator()
    {
     
        RuleFor(x => x.UsernameOrEmail)
            .NotEmpty().WithMessage("用户名或邮箱不能为空")
            .MinimumLength(3).WithMessage("账号长度不能小于 3");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("密码不能为空")
            .MinimumLength(6).WithMessage("密码至少需要 6 个字符");
    }
}

public record class LoginResult(string token, UserPreferences userPreferences);
public record class UserRegInfo(string UserName,string PassWord,string Email,string code);
public partial class UserRegInfoModel:ObservableValidator
{
    [ObservableProperty]
    private string _userName;

    [ObservableProperty]
    private string _password;

    [ObservableProperty]
    private string _email;

    [ObservableProperty]
    private string _code;

    private UserRegInfoValidator val = new ();
    public static implicit operator UserRegInfo(UserRegInfoModel model)
    {
        return new UserRegInfo(model.UserName,model.Password,model.Email,model.Code);
    }
    /// <summary>
    /// 获取表单验证对象
    /// </summary>
    /// <returns></returns>
    public ValidationResult GetValidationResult()
    {
        return val.Validate(this);
    }

   
}
public class UserRegInfoValidator : AbstractValidator<UserRegInfoModel>
{
    public UserRegInfoValidator()
    {
        // 用户名规则
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("用户名不能为空")
            .MinimumLength(3).WithMessage("用户名至少需要 3 个字符")
            .MaximumLength(20).WithMessage("用户名不能超过 20 个字符");

        // 密码规则
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("密码不能为空")
            .MinimumLength(6).WithMessage("密码至少需要 6 个字符");

        // 邮箱规则
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("邮箱不能为空")
            .EmailAddress().WithMessage("请输入正确的邮箱格式");

        // 验证码规则 
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("验证码不能为空");

    }
}