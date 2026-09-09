using CommunityToolkit.Mvvm.ComponentModel;
using TextMateSharp.Themes;

namespace drive_desktop.ViewModels;

public partial class VideoPreviewViewModel:ViewModelBase
{
    [ObservableProperty] private string _videoSource;
    [ObservableProperty] private string _title = "视频播放器";
    public VideoPreviewViewModel()
    {
        
    }

    public VideoPreviewViewModel(string videoSource,string name)
    {
        VideoSource =  videoSource;
        Title = name;
    }
}