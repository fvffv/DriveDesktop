using System.Linq;
using drive_desktop.Services.Plugins;
using Drive.Plugin.Abi;

namespace drive_desktop.ViewModels;

public partial class FilePageViewModel
{
    private void PublishPluginSelection() => PluginEventHub.Publish(new()
    {
        Id = DriveEventId.SelectionChanged,
        Selection = (FileInfos ?? []).Where(x => x.IsChecked).Select(PluginDtoMapper.File)
            .Concat((FolderInfos ?? []).Where(x => x.IsChecked).Select(x => PluginDtoMapper.Folder(x))).Take(200).ToArray()
    });
}
