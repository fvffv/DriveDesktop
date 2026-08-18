using System.IO;
using System.Threading.Tasks;
using drive_desktop.Models;
using Refit;

namespace drive_desktop.Services;

public interface ICloudDriveUserApi
{
    /// <summary>
    /// 流式处理 AI 网盘助手命令
    /// </summary>
    [Post("/api/User/ProcessCommandStream")]
    [Headers("Authorization: Bearer")]
    Task<Stream> ProcessCommandStreamAsync([Body] AiChatRequest request);

    /// <summary>
    /// 登录
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [Post("/api/User/LoginUser")]
    Task<DefaultMsg<LoginResult>> LoginAsync([Body] LoginInfo request);
    
    /// <summary>
    /// 注册
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [Post("/api/User/RegisterUser")]
    Task<DefaultMsg> RegisterUserAsync([Body] UserRegInfo request);
    /// <summary>
    /// 发送验证码
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [Get("/api/User/SeedEmailCode")]
    Task<DefaultMsg> SeedEmailCode([Query] string email);
    /// <summary>
    /// 获取用户信息  
    /// </summary>
    /// <returns></returns>
    [Headers("Authorization: Bearer")]
    [Get("/api/User/GetUserInfo")]
    Task<DefaultMsg<ShowUserInfo>> GetUserInfo();
    /// <summary>
    /// 上传头像 (最大限制 2MB)
    /// </summary>
    [Multipart]
    [Headers("Authorization: Bearer")]
    [Post("/api/User/UploadAvatar")]
    Task<DefaultMsg> UploadAvatarAsync([AliasAs("file")] StreamPart file);

    /// <summary>
    /// 更新用户信息
    /// </summary>
    [Post("/api/User/UpdateUserInfo")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> UpdateUserInfoAsync([Body] UserInfoEdit info);

    /// <summary>
    /// 更新用户密码
    /// </summary>
    [Post("/api/User/UpdatePassword")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> UpdatePasswordAsync([Body] UserPasswordEdit info);

    /// <summary>
    /// 更新用户偏好
    /// </summary>
    [Post("/api/User/UpdateUserPreferences")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> UpdateUserPreferencesAsync([Body] UserPreferences info);

    /// <summary>
    /// 更新用户侧边栏视图
    /// </summary>
    [Post("/api/User/UpdateUserView")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> UpdateUserViewAsync([Body] CustomViewDto[] info);
    
    /// <summary>
    /// 获取头像
    /// </summary>
    [Get("/driveassets/avatar/{imgName}")]
    Task<Stream> GetUserHeadImg(string imgName);
}

public interface ICloudDriveFileApi
{
    /// <summary>
    /// 文件上传接口
    /// </summary>
    [Multipart]
    [Post("/api/Files/UploadFile")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> UploadFileAsync([AliasAs("file")] StreamPart file, [AliasAs("folderId")] string folderId);

    /// <summary>
    /// 获取用户存储容量信息
    /// </summary>
    [Get("/api/Files/GetUserStorageCapacityInfo")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg<StorageCapacityInfo>> GetUserStorageCapacityInfoAsync();

    /// <summary>
    /// 根据ID转存文件或根据Hash256秒传
    /// </summary>
    [Get("/api/Files/SaveToFile")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> SaveToFileAsync([Query] string folderId, [Query] string fileId, [Query] string hash256);

    /// <summary>
    /// 获取用户根目录ID
    /// </summary>
    [Get("/api/Files/GetFolderRoot")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> GetFolderRootAsync();

    /// <summary>
    /// 获取网盘基本信息
    /// </summary>
    [Get("/api/Files/GetCloudInfo")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg<CloudInfo>> GetCloudInfoAsync();

    /// <summary>
    /// 获取目录的文件和文件夹列表
    /// </summary>
    [Get("/api/Files/GetUserDirectoryFileInfo")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg<UserFilesInfo>> GetUserDirectoryFileInfoAsync([Query] string folderId, [Query] int pageIndex = 1, [Query] int pageSize = 50);

    /// <summary>
    /// 新建文件夹
    /// </summary>
    [Get("/api/Files/CreateFolder")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> CreateFolderAsync([Query] string folderId, [Query] string name);

    /// <summary>
    /// 根据路径严格获取文件夹ID
    /// </summary>
    [Get("/api/Files/GetFolderByPathStrict")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> GetFolderByPathStrictAsync([Query] string rootPathId, [Query] string fullPath);

    /// <summary>
    /// 批量删除文件
    /// </summary>
    [Post("/api/Files/DeleteUserFile")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> DeleteUserFileAsync([Body] string[] fileIds);

    /// <summary>
    /// 批量删除文件夹
    /// </summary>
    [Post("/api/Files/DeleteUserFolder")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> DeleteUserFolderAsync([Body] string[] folderIds);

    /// <summary>
    /// 修改文件或文件夹名字
    /// </summary>
    [Post("/api/Files/RenameFileOrDir")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> RenameFileOrDirAsync([Body] FileOrDirReNameInfo info);

    /// <summary>
    /// 生成临时下载密钥 自己文件
    /// </summary>
    [Get("/api/Files/GetFileDownLoadTempKey")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> GetFileDownLoadTempKeyAsync([Query] string fileId);

    /// <summary>
    /// 生成临时下载密钥 (分享链接)
    /// </summary>
    [Get("/api/Files/GetShareTempDownLoadKey")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> GetShareTempDownLoadKeyAsync([Query] string shareKey, [Query] string pwd=null);

    /// <summary>
    /// 移动文件或文件夹到其他目录
    /// </summary>
    [Post("/api/Files/MoveFileOrDir")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> MoveFileOrDirAsync([Body] FileOrDirMoveInfo info);

    /// <summary>
    /// 创建分享链接
    /// </summary>
    [Post("/api/Files/CreateShareKey")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> CreateShareKeyAsync([Body] FileShareDataDTO info);

    /// <summary>
    /// 更新分享链接信息
    /// </summary>
    /// <param name="info">修改的信息(作为Body)</param>
    [Post("/api/Files/UpdateShareFileInfo")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> UpdateShareFileInfoAsync([Query] string shareId, [Query] bool isDel, [Body] FileShareDataDTO? fileShareData = null);

    /// <summary>
    /// 得到分享的下载信息
    /// </summary>
    [Get("/api/Files/GetShareInfo")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg<FileShareInfoDto>> GetShareInfoAsync([Query] string shareKey);

    /// <summary>
    /// 语义搜索文件
    /// </summary>
    [Post("/api/Files/SearchFiles")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg<UserFilesInfo>> SearchFilesAsync([Body]SearchInfoDTO searchInfoDto, [Query] bool isAI = true);

    /// <summary>
    /// 得到自己的分享文件列表
    /// </summary>
    [Get("/api/Files/GetShareFilesInfoPrivate")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg<ShareInfoPrivateDto[]>> GetShareFilesInfoPrivateAsync();

    /// <summary>
    /// 得到个人的统计信息（看板数据）
    /// </summary>
    [Get("/api/Files/GetDataStatistics")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg<DashboardStatisticsDto>> GetDataStatisticsAsync();
    /// <summary>
    /// 根据folderid获取完整路径
    /// </summary>
    [Get("/api/Files/GetFullFolderPath")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> GetFullFolderPathAsync([Query] string folderId);
    /// <summary>
    /// 根据单个id获取文件信息
    /// </summary>
    [Get("/api/Files/GetUserFileInfo")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> GetUserFileInfo([Query] string fileId);
    /// <summary>
    /// 根据单个id获取文件信息
    /// </summary>
    [Get("/api/Files/DownLoadKey/{key}")]
    [Headers("Authorization: Bearer")]
    Task<Stream> DownLoadKey(string key);
    
    
    /// <summary>
    /// 创建分片任务
    /// </summary>
    [Post("/api/Files/CreateMultipartUpload")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> CreateMultipartUpload([Body]MultipartUploadInitRequest req);
    /// <summary>
    /// 获取文件分片信息
    /// </summary>
    [Get("/api/Files/GetFileChunkInfo")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg<UploadTaskModel>> GetFileChunkInfo([Query] string uploadId);
    /// <summary>
    /// 请求合并分片文件
    /// </summary>
    [Get("/api/Files/MergeFiles")]
    [Headers("Authorization: Bearer")]
    Task<DefaultMsg> MergeFiles([Query]string uploadId, [Query]string folderId);
}
