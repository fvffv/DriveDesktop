using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using drive_desktop.Models;
using drive_desktop.Services;
using LiveMarkdown.Avalonia;

namespace drive_desktop.ViewModels;

public partial class AiChatViewModel: ViewModelBase
{
    /// <summary>
    /// Ai对话记录
    /// </summary>
    [ObservableProperty] private ObservableCollection<AiChat> _aiChats;
    /// <summary>
    /// 用户要发送的内容
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SeedAiChatCommand))]
    private string _userInput;

    /// <summary>
    /// 是否正在接收 AI 的流式回复
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SeedAiChatCommand))]
    private bool _isSending;
    public AiChatViewModel()
    {
        AiChats = new ObservableCollection<AiChat>()
        {
            new AiChatAiMsg
            {
                Content = new ObservableStringBuilder("你好！我是星云智能助手。\n我可以帮你快速查找文件、总结长文档，或者解答网盘的使用问题。有什么我可以帮你的吗？")
            },
            new AiChatUserMsg
            {
                Content = "帮我把所有过期的分享链接清理掉。"
            },
            new AiChatAiMsg
            {
                Content = new ObservableStringBuilder("好的，已为你扫描并清理了 4 个 过期的分享链接。\n\n你可以随时在「分享管理」页面查看最新状态。")
            },
        };
    }

    private readonly WebApiService _webApiService;
    private readonly UserInfoService _userInfoService;
    private readonly AppConfigService _appConfigService;
    public AiChatViewModel(AppConfigService appConfigService,UserInfoService userInfoService, WebApiService webApiService)
    {
        _appConfigService =  appConfigService;
        _userInfoService = userInfoService;
        _webApiService = webApiService;
    }

    /// <summary>
    /// 发送消息给AI
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSeedAiChat))]
    private async Task SeedAiChat()
    {
        const string thinkingText = "思考中...";
        var userInput = UserInput.Trim();
        var chats = AiChats ??= new ObservableCollection<AiChat>();
        var history = chats.Select(chat => chat switch
        {
            AiChatUserMsg userMessage => new ChatMessage
            {
                Role = "user",
                Content = userMessage.Content
            },
            AiChatAiMsg aiMessage => new ChatMessage
            {
                Role = "ai",
                Content = aiMessage.Content.ToString()
            },
            _ => null
        }).Where(message => message is not null).Cast<ChatMessage>().ToList();

        var request = new AiChatRequest
        {
            UserInput = userInput,
            History = history,
            Context = new ClientContext
            {
            CurrentFolderId = _userInfoService.CurrentDirectoryId,
            CurrentPath =  _userInfoService.CurrentDirectoryPath,
            BackEnd = _appConfigService.Config.ServerIp+"/driveassets/",
            CurrentDomain = _appConfigService.Config.ServerIp,
            Client = "desktop"
            }
        };

        var aiContent = new ObservableStringBuilder();
        var aiMessage = new AiChatAiMsg { Content = aiContent };
        var isAiMessageShown = false;

        void ShowAiMessage()
        {
            if (isAiMessageShown)
            {
                return;
            }

            chats.Add(aiMessage);
            isAiMessageShown = true;
            if (UserInput == thinkingText)
            {
                UserInput = string.Empty;
            }
        }

        chats.Add(new AiChatUserMsg { Content = userInput });
        IsSending = true;
        UserInput = thinkingText;

        try
        {
            await using var responseStream =
                await _webApiService.UserApi.ProcessCommandStreamAsync(request);
            using var reader = new StreamReader(responseStream, Encoding.UTF8);
            var buffer = new char[1024];

            while (await reader.ReadAsync(buffer, 0, buffer.Length) is var readCount && readCount > 0)
            {
                aiContent.Append(new string(buffer, 0, readCount));
                ShowAiMessage();
            }

            if (aiContent.Length == 0)
            {
                aiContent.Append("星云没有收到有效回复，请稍后重试。");
                ShowAiMessage();
            }
        }
        catch (System.Exception exception)
        {
            Debug.WriteLine(exception);
            if (aiContent.Length > 0)
            {
                aiContent.Append("\n\n");
            }

            aiContent.Append("回复接收失败，请检查网络或重新登录后再试。");
            ShowAiMessage();
        }
        finally
        {
            if (UserInput == thinkingText)
            {
                UserInput = string.Empty;
            }

            IsSending = false;
        }
    }

    private bool CanSeedAiChat() =>
        !IsSending && !string.IsNullOrWhiteSpace(UserInput);
}
