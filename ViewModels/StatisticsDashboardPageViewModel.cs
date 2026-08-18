using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using drive_desktop.Models;
using drive_desktop.Services;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace drive_desktop.ViewModels;

public partial class StatisticsDashboardPageViewModel : ViewModelBase
{
    private static readonly SKColor[] ChartColors =
    [
        SKColor.Parse("#3B82F6"),
        SKColor.Parse("#8B5CF6"),
        SKColor.Parse("#10B981"),
        SKColor.Parse("#F59E0B"),
        SKColor.Parse("#94A3B8")
    ];

    private readonly WebApiService _webApiService;

    /// <summary>
    /// 四个卡片数据
    /// </summary>
    [ObservableProperty]
    private SummaryData _summaryData = new();
    /// <summary>
    /// 文件类型空间分布数据（用于环形图）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FileTypeData> _fileTypeDatas = [];
    /// <summary>
    /// 近期上传趋势活跃度数据（用于平滑折线图）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TrendData> _trendDatas = [];
    /// <summary>
    /// 大文件空间占用排行数据（用于条形图）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TopFilesData> _topFilesData = [];

    [ObservableProperty]
    private SpaceCategoryData[] _spaceCategories = [];

    [ObservableProperty]
    private double[] _trendValues = [];

    [ObservableProperty]
    private string[] _trendLabels = [];

    [ObservableProperty]
    private double[] _topFileValues = [];

    [ObservableProperty]
    private string[] _topFileLabels = [];

    public Func<ChartPoint, string> TopFileToolTipFormatter { get; } =
        point => FormatBytes(point.Coordinate.PrimaryValue);
    public StatisticsDashboardPageViewModel(WebApiService webApiService)
    {
        _webApiService = webApiService;
        _ = LoadData();
    }


    /// <summary>
    /// 初始化数据
    /// </summary>
    public async Task LoadData()
    {
        var info = await _webApiService.FileApi.GetDataStatisticsAsync();
        if (info.Status==0)
        {
            SummaryData = new()
            {
                FileCount = info.Data.SummaryData.FileCount,
                UsedSpaceBytes =  info.Data.SummaryData.UsedSpaceBytes,
                ShareCount =  info.Data.SummaryData.ShareCount,
                TotalSpaceBytes =  info.Data.SummaryData.TotalSpaceBytes,
            };
            FileTypeDatas = new ObservableCollection<FileTypeData>(
                info.Data.FileTypeData?.Select(x => new FileTypeData
                {
                    Name = x.Name,
                    ValueBytes = x.ValueBytes
                }) ?? Enumerable.Empty<FileTypeData>()
            );
            
            TrendDatas = new ObservableCollection<TrendData>(
                info.Data.TrendData?.Select(x => new TrendData
                {
                    Dates = x.Dates,
                    Uploads = x.Uploads
                }) ?? Enumerable.Empty<TrendData>()
            );
            
            TopFilesData = new ObservableCollection<TopFilesData>(
                info.Data.TopFilesData?.Select(x => new TopFilesData
                {
                    Name = x.Name,
                    SizeByte = x.SizeByte
                }) ?? Enumerable.Empty<TopFilesData>()
            );

            UpdateChartData();
        }
        
        
    }

    private void UpdateChartData()
    {
        SpaceCategories = FileTypeDatas
            .Select((item, index) => new SpaceCategoryData
            {
                Name = item.Name,
                Values = [item.ValueBytes],
                Fill = new SolidColorPaint(ChartColors[index % ChartColors.Length])
            })
            .ToArray();

        TrendLabels = TrendDatas.Select(item => item.Dates).ToArray();
        TrendValues = TrendDatas.Select(item => (double)item.Uploads).ToArray();

        var topFiles = TopFilesData
            .OrderByDescending(item => item.SizeByte)
            .Take(5)
            .ToArray();

        TopFileLabels = topFiles.Select(item => item.Name).ToArray();
        TopFileValues = topFiles.Select(item => (double)item.SizeByte).ToArray();
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes <= 0) return "0 B";

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var unitIndex = 0;
        while (bytes >= 1024 && unitIndex < units.Length - 1)
        {
            bytes /= 1024;
            unitIndex++;
        }

        return $"{bytes:0.##} {units[unitIndex]}";
    }
}
