using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using drive_desktop.Models;
using drive_desktop.Views;
using FluentValidation.Results;
using Pure.DI;
using Refit;
using Ursa.Controls;

namespace drive_desktop.Services;

public class WebApiService
{
    
    public ICloudDriveUserApi UserApi { get; }
    public ICloudDriveFileApi FileApi { get; }
    /// <summary>
    /// webapi初始化
    /// </summary>
    /// <param name="configuration"></param>
    public WebApiService(AppConfigService configuration)
    {
        //aot序列化设置
        var settings = new RefitSettings(
            new SystemTextJsonContentSerializer(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    TypeInfoResolver = AppConfigJsonContext.Default
                }
            )
        )
        {
            AuthorizationHeaderValueGetter = (message, cancellationToken) =>
            {
                var token = configuration.Config.JWT;
                return new ValueTask<string>(token);
            }
        };

        //自定义http流控制返回内容
        var httpClient = new HttpClient(new GlobalFallbackHandler(new HttpClientHandler()))
        {
            BaseAddress = new Uri(configuration.Config.ServerIp)
        };
        
        UserApi = RestService.For<ICloudDriveUserApi>(httpClient,settings);
        FileApi = RestService.For<ICloudDriveFileApi>(httpClient,settings);
    }
    /// <summary>
    /// 表单验证校验弹窗提示
    /// </summary>
    /// <param name="validator"></param>
    /// <returns></returns>
    public bool FormValidation(ValidationResult validationResult, WindowToastManager wtm)
    {
        if (!validationResult.IsValid)
        {
            var firstError = validationResult.Errors.First().ErrorMessage;
            wtm?.Show(
                new Toast(firstError), 
                type: NotificationType.Warning
            );
        
            return false; 
        }
        return true; 
    }
}
public class GlobalFallbackHandler : DelegatingHandler
{
    public GlobalFallbackHandler(
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response =
                await base.SendAsync(
                    request,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string responseText =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);

                Debug.WriteLine(
                    $"HTTP {(int)response.StatusCode} " +
                    $"{response.StatusCode}：{responseText}");
            }

            // 原样返回，让 Refit 根据状态码抛出 ApiException
            return response;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            // 保留原始网络异常
            throw;
        }
    }
}