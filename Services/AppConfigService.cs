using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using drive_desktop.Models;
using Refit;

namespace drive_desktop.Services;

public class AppConfigService
{
    private readonly string _configFilePath = Path.Combine(AppContext.BaseDirectory, "config.json");


    public AppConfigModel Config { get; set; }

    public AppConfigService()
    {
        InitializeConfig();
    }

    private void InitializeConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                // 文件不存在，实例化默认配置并保存
                Config = new AppConfigModel();
                Save();
            }
            else
            {
                // 文件存在，直接读取
                string json = File.ReadAllText(_configFilePath);


                Config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfigModel)
                         ?? new AppConfigModel();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"读取配置文件失败: {ex.Message}");
            Config = new AppConfigModel();
        }
    }

    /// <summary>
    /// 外部修改 Config 的属性后，调用此方法将修改保存到本地文件
    /// </summary>
    public void Save()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true // 让生成的 json 有换行和缩进，方便人类阅读
        };


        string json = JsonSerializer.Serialize(Config, typeof(AppConfigModel), new AppConfigJsonContext(options));
        File.WriteAllText(_configFilePath, json);
    }
}

public class AppConfigModel
{
    /// <summary>
    /// 服务器ip
    /// </summary>
    public string ServerIp { get; set; } = "https://u.luzycloud.top:8443";

    /// <summary>
    /// 账号
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// 密码
    /// </summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// 下载位置
    /// </summary>
    public string DownloadLocation { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");

    /// <summary>
    /// 主题
    /// </summary>
    public int Theme { get; set; } = 0;

    /// <summary>
    /// jwt
    /// </summary>
    public string JWT { get; set; } = string.Empty;
    /// <summary>
    /// 同时下载的数量
    /// </summary>
    public int DownloadSemaphore { get; set; } = 1;
    /// <summary>
    /// 同时上传的数量
    /// </summary>
    public int UploadSemaphore { get; set; } = 5;
}

[JsonSerializable(typeof(AppConfigModel))]
[JsonSerializable(typeof(LoginInfo))]
[JsonSerializable(typeof(DefaultMsg))]
[JsonSerializable(typeof(UserPreferences))]
[JsonSerializable(typeof(DefaultMsg<LoginResult>))]
[JsonSerializable(typeof(DefaultMsg<ShowUserInfo>))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(UserRegInfo))]
[JsonSerializable(typeof(ShowUserInfo))]
[JsonSerializable(typeof(CustomViewDto))]
[JsonSerializable(typeof(CustomViewDto[]))]
[JsonSerializable(typeof(DefaultMsg<UserFilesInfo>))]
[JsonSerializable(typeof(StorageCapacityInfo))]
[JsonSerializable(typeof(DefaultMsg<StorageCapacityInfo>))]
[JsonSerializable(typeof(UserFilesInfoItem))]
[JsonSerializable(typeof(UserDirsInfoItem))]
[JsonSerializable(typeof(FileOrDirReNameInfo))]
[JsonSerializable(typeof(FileOrDirMoveInfo))]
[JsonSerializable(typeof(FileShareDataDTO))]
[JsonSerializable(typeof(FileShareData))]
[JsonSerializable(typeof(SearchInfoDTO))]
[JsonSerializable(typeof(CloudInfo))]
[JsonSerializable(typeof(DefaultMsg<CloudInfo>))]
[JsonSerializable(typeof(UploadTaskModel))]
[JsonSerializable(typeof(DefaultMsg<UploadTaskModel>))]
[JsonSerializable(typeof(UploadMetaModel))]
[JsonSerializable(typeof(UploadedChunkModel))]
[JsonSerializable(typeof(MultipartUploadInitRequest))]
[JsonSerializable(typeof(ShareInfoPrivateDto))]
[JsonSerializable(typeof(UserInfoEdit))]
[JsonSerializable(typeof(UserPasswordEdit))]
[JsonSerializable(typeof(FileShareInfoDto))]
[JsonSerializable(typeof(DefaultMsg<FileShareInfoDto>))]
[JsonSerializable(typeof(DefaultMsg<ShareInfoPrivateDto[]>))]
[JsonSerializable(typeof(DefaultMsg<DashboardStatisticsDto>))]
[JsonSerializable(typeof(AiChatRequest))]
internal partial class AppConfigJsonContext : JsonSerializerContext
{
}
